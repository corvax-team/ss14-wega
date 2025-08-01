using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Chat.Systems;
using Content.Server.EntityEffects;
using Content.Shared.Alert;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Prototypes;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Database;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.EffectConditions;
using Content.Shared.EntityEffects.Effects;
using Content.Shared.Mobs.Systems;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared.Strangulation; //Corvax-Wega-Strangulation

namespace Content.Server.Body.Systems;

[UsedImplicitly]
public sealed class RespiratorSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly AlertsSystem _alertsSystem = default!;
    [Dependency] private readonly AtmosphereSystem _atmosSys = default!;
    [Dependency] private readonly BodySystem _bodySystem = default!;
    [Dependency] private readonly DamageableSystem _damageableSys = default!;
    [Dependency] private readonly LungSystem _lungSystem = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly IPrototypeManager _protoMan = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly EntityEffectSystem _entityEffect = default!;

    private static readonly ProtoId<MetabolismGroupPrototype> GasId = new("Gas");

    public override void Initialize()
    {
        base.Initialize();

        // We want to process lung reagents before we inhale new reagents.
        UpdatesAfter.Add(typeof(MetabolizerSystem));
        SubscribeLocalEvent<RespiratorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RespiratorComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeLocalEvent<RespiratorComponent, ApplyMetabolicMultiplierEvent>(OnApplyMetabolicMultiplier);
        SubscribeLocalEvent<BodyComponent, InhaledGasEvent>(OnGasInhaled);
        SubscribeLocalEvent<BodyComponent, ExhaledGasEvent>(OnGasExhaled);
    }

    private void OnMapInit(Entity<RespiratorComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextUpdate = _gameTiming.CurTime + ent.Comp.UpdateInterval;
    }

    private void OnUnpaused(Entity<RespiratorComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.NextUpdate += args.PausedTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RespiratorComponent, BodyComponent>();
        while (query.MoveNext(out var uid, out var respirator, out var body))
        {
            if (_gameTiming.CurTime < respirator.NextUpdate)
                continue;

            respirator.NextUpdate += respirator.UpdateInterval;

            if (_mobState.IsDead(uid))
                continue;

            UpdateSaturation(uid, -(float)respirator.UpdateInterval.TotalSeconds, respirator);

            if (!_mobState.IsIncapacitated(uid) // cannot breathe in crit.
                && !HasComp<StrangulationComponent>(uid)) // cannot breathe when strangled //Corvax-Wega-Strangulation
            {
                switch (respirator.Status)
                {
                    case RespiratorStatus.Inhaling:
                        Inhale(uid);
                        respirator.Status = RespiratorStatus.Exhaling;
                        break;
                    case RespiratorStatus.Exhaling:
                        Exhale(uid);
                        respirator.Status = RespiratorStatus.Inhaling;
                        break;
                }
            }

            if (respirator.Saturation < respirator.SuffocationThreshold)
            {
                if (_gameTiming.CurTime >= respirator.LastGaspEmoteTime + respirator.GaspEmoteCooldown)
                {
                    respirator.LastGaspEmoteTime = _gameTiming.CurTime;
                    _chat.TryEmoteWithChat(uid,
                        respirator.GaspEmote,
                        ChatTransmitRange.HideChat,
                        ignoreActionBlocker: true);
                }

                TakeSuffocationDamage((uid, respirator));
                respirator.SuffocationCycles += 1;
                continue;
            }

            StopSuffocation((uid, respirator));
            respirator.SuffocationCycles = 0;
        }
    }

    public bool Inhale(Entity<RespiratorComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false))
            return false;

        // Inhale gas
        var ev = new InhaleLocationEvent
        {
            Respirator = entity.Comp,
        };
        RaiseLocalEvent(entity, ref ev);

        ev.Gas ??= _atmosSys.GetContainingMixture(entity.Owner, excite: true);

        if (ev.Gas is null)
        {
            return false;
        }

        var gas = ev.Gas.RemoveVolume(entity.Comp.BreathVolume);

        var inhaleEv = new InhaledGasEvent(gas);
        RaiseLocalEvent(entity, ref inhaleEv);

        return inhaleEv.Handled && inhaleEv.Succeeded;
    }

    public void Exhale(Entity<RespiratorComponent?> entity)
    {
        if (!Resolve(entity, ref entity.Comp, logMissing: false))
            return;

        // exhale gas

        var ev = new ExhaleLocationEvent();
        RaiseLocalEvent(entity, ref ev, broadcast: false);

        if (ev.Gas is null)
        {
            ev.Gas = _atmosSys.GetContainingMixture(entity.Owner, excite: true);

            // Walls and grids without atmos comp return null. I guess it makes sense to not be able to exhale in walls,
            // but this also means you cannot exhale on some grids.
            ev.Gas ??= GasMixture.SpaceGas;
        }

        var exhaleEv = new ExhaledGasEvent(ev.Gas);
        RaiseLocalEvent(entity, ref exhaleEv);
    }

    /// <summary>
    /// Returns true if the entity is above their SuffocationThreshold and alive.
    /// </summary>
    public bool IsBreathing(Entity<RespiratorComponent?> ent)
    {
        if (_mobState.IsIncapacitated(ent))
            return false;

        if (!Resolve(ent, ref ent.Comp))
            return false;

        return (ent.Comp.Saturation > ent.Comp.SuffocationThreshold);
    }

    /// <summary>
    /// Check whether or not an entity can metabolize inhaled air without suffocating or taking damage (i.e., no toxic
    /// gasses).
    /// </summary>
    public bool CanMetabolizeInhaledAir(Entity<RespiratorComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        if (!Inhale(ent))
            return false;

        // If we don't have a body we can't be poisoned by gas, yet...
        var success = TryMetabolizeGas((ent, ent.Comp));

        // Don't keep that gas in our lungs lest it poisons a poor nuclear operative.
        Exhale(ent);
        return success;
    }

    /// <summary>
    /// Check whether or not an entity can metabolize the given gas mixture without suffocating or taking damage
    /// (i.e., no toxic gasses).
    /// </summary>
    public bool CanMetabolizeGas(Entity<RespiratorComponent?> ent, GasMixture gas)
    {
        if (!Resolve(ent, ref ent.Comp))
            return false;

        var organs = _bodySystem.GetBodyOrganEntityComps<LungComponent>((ent, null));
        if (organs.Count == 0)
            return false;

        gas = new GasMixture(gas);
        var lungRatio = 1.0f / organs.Count;
        gas.Multiply(MathF.Min(lungRatio * gas.Volume / ent.Comp.BreathVolume, lungRatio));
        var solution = _lungSystem.GasToReagent(gas);

        float saturation = 0;
        foreach (var organ in organs)
        {
            saturation += GetSaturation(solution, organ.Owner, out var toxic);
            if (toxic)
                return false;
        }

        return saturation > ent.Comp.UpdateInterval.TotalSeconds;
    }

    public bool TryInhaleGasToBody(Entity<BodyComponent?> entity, GasMixture gas)
    {
        if (!Resolve(entity, ref entity.Comp))
            return false;

        var organs = _bodySystem.GetBodyOrganEntityComps<LungComponent>((entity, entity.Comp));
        if (organs.Count == 0)
            return false;

        var lungRatio = 1.0f / organs.Count;
        var splitGas = organs.Count == 1 ? gas : gas.RemoveRatio(lungRatio);
        foreach (var (organUid, lung, _) in organs)
        {
            // Merge doesn't remove gas from the giver.
            _atmosSys.Merge(lung.Air, splitGas);
            _lungSystem.GasToReagent(organUid, lung);
        }

        return true;
    }

    public void RemoveGasFromBody(Entity<BodyComponent> ent, GasMixture gas)
    {
        var outGas = new GasMixture(gas.Volume);

        var organs = _bodySystem.GetBodyOrganEntityComps<LungComponent>((ent, ent.Comp));
        if (organs.Count == 0)
            return;

        foreach (var (organUid, lung, _) in organs)
        {
            _atmosSys.Merge(outGas, lung.Air);
            lung.Air.Clear();

            if (_solutionContainerSystem.ResolveSolution(organUid, lung.SolutionName, ref lung.Solution))
                _solutionContainerSystem.RemoveAllSolution(lung.Solution.Value);
        }

        _atmosSys.Merge(gas, outGas);
    }

    /// <summary>
    /// Tries to safely metabolize the current solutions in a body's lungs.
    /// </summary>
    private bool TryMetabolizeGas(Entity<RespiratorComponent, BodyComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp2))
            return false;

        var organs = _bodySystem.GetBodyOrganEntityComps<LungComponent>((ent, null));
        if (organs.Count == 0)
            return false;

        float saturation = 0;
        foreach (var organ in organs)
        {
            var solution = _lungSystem.GasToReagent(organ.Comp1.Air);
            saturation += GetSaturation(solution, organ.Owner, out var toxic);
            if (toxic)
                return false;
        }

        return saturation > ent.Comp1.UpdateInterval.TotalSeconds;
    }

    /// <summary>
    /// Get the amount of saturation that would be generated if the lung were to metabolize the given solution.
    /// </summary>
    /// <remarks>
    /// This assumes the metabolism rate is unbounded, which generally should be the case for lungs, otherwise we get
    /// back to the old pulmonary edema bug.
    /// </remarks>
    /// <param name="solution">The reagents to metabolize</param>
    /// <param name="lung">The entity doing the metabolizing</param>
    /// <param name="toxic">Whether or not any of the reagents would deal damage to the entity</param>
    private float GetSaturation(Solution solution, Entity<MetabolizerComponent?> lung, out bool toxic)
    {
        toxic = false;
        if (!Resolve(lung, ref lung.Comp))
            return 0;

        if (lung.Comp.MetabolismGroups == null)
            return 0;

        float saturation = 0;
        foreach (var (id, quantity) in solution.Contents)
        {
            var reagent = _protoMan.Index<ReagentPrototype>(id.Prototype);
            if (reagent.Metabolisms == null)
                continue;

            if (!reagent.Metabolisms.TryGetValue(GasId, out var entry))
                continue;

            foreach (var effect in entry.Effects)
            {
                if (effect is HealthChange health)
                    toxic |= CanMetabolize(health) && health.Damage.AnyPositive();
                else if (effect is Oxygenate oxy && CanMetabolize(oxy))
                    saturation += oxy.Factor * quantity.Float();
            }
        }

        // TODO generalize condition checks
        // this is pretty janky, but I just want to bodge a method that checks if an entity can breathe a gas mixture
        // Applying actual reaction effects require a full ReagentEffectArgs struct.
        bool CanMetabolize(EntityEffect effect)
        {
            if (effect.Conditions == null)
                return true;

            foreach (var cond in effect.Conditions)
            {
                if (cond is OrganType organ && !_entityEffect.OrganCondition(organ, lung))
                    return false;
            }

            return true;
        }

        return saturation;
    }

    private void TakeSuffocationDamage(Entity<RespiratorComponent> ent)
    {
        if (ent.Comp.SuffocationCycles == 2)
            _adminLogger.Add(LogType.Asphyxiation, $"{ToPrettyString(ent):entity} started suffocating");

        _damageableSys.TryChangeDamage(ent, ent.Comp.Damage, interruptsDoAfters: false);

        if (ent.Comp.SuffocationCycles >= ent.Comp.SuffocationCycleThreshold)
        {
            // TODO: This is not going work with multiple different lungs, if that ever becomes a possibility
            var organs = _bodySystem.GetBodyOrganEntityComps<LungComponent>((ent, null));
            foreach (var entity in organs)
            {
                _alertsSystem.ShowAlert(ent, entity.Comp1.Alert);
            }
        }

        //Corvax-Wega-Strangulation-start
        if (TryComp<StrangulationComponent>(ent, out var strangleComp) && strangleComp != null)
            _damageableSys.TryChangeDamage(ent, strangleComp.Damage, interruptsDoAfters: false);
        else
        //Corvax-Wega-Strangulation-end
            _damageableSys.TryChangeDamage(ent, ent.Comp.Damage, interruptsDoAfters: false);
    }

    private void StopSuffocation(Entity<RespiratorComponent> ent)
    {
        if (ent.Comp.SuffocationCycles >= 2)
            _adminLogger.Add(LogType.Asphyxiation, $"{ToPrettyString(ent):entity} stopped suffocating");

        _damageableSys.TryChangeDamage(ent, ent.Comp.DamageRecovery);

        // TODO: This is not going work with multiple different lungs, if that ever becomes a possibility
        var organs = _bodySystem.GetBodyOrganEntityComps<LungComponent>((ent, null));
        foreach (var entity in organs)
        {
            _alertsSystem.ClearAlert(ent, entity.Comp1.Alert);
        }
    }

    public void UpdateSaturation(EntityUid uid, float amount, RespiratorComponent? respirator = null)
    {
        if (!Resolve(uid, ref respirator, false))
            return;

        respirator.Saturation += amount;
        respirator.Saturation =
            Math.Clamp(respirator.Saturation, respirator.MinSaturation, respirator.MaxSaturation);
    }

    private void OnApplyMetabolicMultiplier(
        Entity<RespiratorComponent> ent,
        ref ApplyMetabolicMultiplierEvent args)
    {
        // TODO REFACTOR THIS
        // This will slowly drift over time due to floating point errors.
        // Instead, raise an event with the base rates and allow modifiers to get applied to it.
        if (args.Apply)
        {
            ent.Comp.UpdateInterval *= args.Multiplier;
            ent.Comp.Saturation *= args.Multiplier;
            ent.Comp.MaxSaturation *= args.Multiplier;
            ent.Comp.MinSaturation *= args.Multiplier;
            return;
        }

        // This way we don't have to worry about it breaking if the stasis bed component is destroyed
        ent.Comp.UpdateInterval /= args.Multiplier;
        ent.Comp.Saturation /= args.Multiplier;
        ent.Comp.MaxSaturation /= args.Multiplier;
        ent.Comp.MinSaturation /= args.Multiplier;
    }

    private void OnGasInhaled(Entity<BodyComponent> entity, ref InhaledGasEvent args)
    {
        args.Handled = true;

        args.Succeeded = TryInhaleGasToBody((entity, entity.Comp), args.Gas);
    }

    private void OnGasExhaled(Entity<BodyComponent> entity, ref ExhaledGasEvent args)
    {
        args.Handled = true;

        RemoveGasFromBody(entity, args.Gas);
    }
}

[ByRefEvent]
public record struct InhaleLocationEvent(GasMixture? Gas, RespiratorComponent Respirator);

[ByRefEvent]
public record struct ExhaleLocationEvent(GasMixture? Gas);

[ByRefEvent]
public record struct InhaledGasEvent(GasMixture Gas, bool Handled = false, bool Succeeded = false);

[ByRefEvent]
public record struct ExhaledGasEvent(GasMixture Gas, bool Handled = false);
