﻿using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.CombatMode.Pacification;
using Content.Shared.Crawling; // Corvax-Wega-Crawling
using Content.Shared.Damage;
using Content.Shared.Damage.ForceSay;
using Content.Shared.Disease.Events; // Corvax-Wega-Disease
using Content.Shared.Emoting;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory.Events;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Pointing;
using Content.Shared.Pulling.Events;
using Content.Shared.Speech;
using Content.Shared.Standing;
using Content.Shared.Strip.Components;
using Content.Shared.Throwing;
using Robust.Shared.Physics.Components;

namespace Content.Shared.Mobs.Systems;

public partial class MobStateSystem
{
    //General purpose event subscriptions. If you can avoid it register these events inside their own systems
    private void SubscribeEvents()
    {
        SubscribeLocalEvent<MobStateComponent, BeforeGettingStrippedEvent>(OnGettingStripped);
        SubscribeLocalEvent<MobStateComponent, ChangeDirectionAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, UseAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, AttackAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, ConsciousAttemptEvent>(CheckConcious);
        SubscribeLocalEvent<MobStateComponent, ThrowAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, SpeakAttemptEvent>(OnSpeakAttempt);
        SubscribeLocalEvent<MobStateComponent, IsEquippingAttemptEvent>(OnEquipAttempt);
        SubscribeLocalEvent<MobStateComponent, EmoteAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, IsUnequippingAttemptEvent>(OnUnequipAttempt);
        SubscribeLocalEvent<MobStateComponent, DropAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, PickupAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, StartPullAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, UpdateCanMoveEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, StandAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, PointAttemptEvent>(CheckAct);
        SubscribeLocalEvent<MobStateComponent, TryingToSleepEvent>(OnSleepAttempt);
        SubscribeLocalEvent<MobStateComponent, CombatModeShouldHandInteractEvent>(OnCombatModeShouldHandInteract);
        SubscribeLocalEvent<MobStateComponent, AttemptPacifiedAttackEvent>(OnAttemptPacifiedAttack);
        SubscribeLocalEvent<MobStateComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<MobStateComponent, AttemptSneezeCoughEvent>(OnSneezeAttempt); // Corvax-Wega-Disease

        SubscribeLocalEvent<MobStateComponent, UnbuckleAttemptEvent>(OnUnbuckleAttempt);
    }

    private void OnUnbuckleAttempt(Entity<MobStateComponent> ent, ref UnbuckleAttemptEvent args)
    {
        // TODO is this necessary?
        // Shouldn't the interaction have already been blocked by a general interaction check?
        if (args.User == ent.Owner && IsIncapacitated(ent))
            args.Cancelled = true;
    }

    private void CheckConcious(Entity<MobStateComponent> ent, ref ConsciousAttemptEvent args)
    {
        switch (ent.Comp.CurrentState)
        {
            case MobState.Dead:
            case MobState.Critical:
                args.Cancelled = true;
                break;
        }
    }

    private void OnStateExitSubscribers(EntityUid target, MobStateComponent component, MobState state)
    {
        switch (state)
        {
            case MobState.Alive:
                //unused
                break;
            // Corvax-Wega-PreCritical-start
            case MobState.PreCritical:
                //unused
                break;
            // Corvax-Wega-PreCritical-end
            case MobState.Critical:
                _standing.Stand(target);
                break;
            case MobState.Dead:
                RemComp<CollisionWakeComponent>(target);
                _standing.Stand(target);
                break;
            case MobState.Invalid:
                //unused
                break;
            default:
                throw new NotImplementedException();
        }
    }

    private void OnStateEnteredSubscribers(EntityUid target, MobStateComponent component, MobState state)
    {
        // All of the state changes here should already be networked, so we do nothing if we are currently applying a
        // server state.
        if (_timing.ApplyingState)
            return;

        _blocker.UpdateCanMove(target); //update movement anytime a state changes
        switch (state)
        {
            case MobState.Alive:
                if (!TryComp(target, out CrawlingComponent? crawlingAlive) || !crawlingAlive.IsCrawling) // Corvax-Wega-Crawling
                    _standing.Stand(target); // Corvax-Wega-Crawling
                _appearance.SetData(target, MobStateVisuals.State, MobState.Alive);
                break;
            // Corvax-Wega-PreCritical-start
            case MobState.PreCritical:
                if (!TryComp(target, out CrawlingComponent? crawlingPreCritical) || !crawlingPreCritical.IsCrawling)
                    _standing.Stand(target);
                _appearance.SetData(target, MobStateVisuals.State, MobState.Critical);
                break;
            // Corvax-Wega-PreCritical-end
            case MobState.Critical:
                _standing.Down(target);
                _appearance.SetData(target, MobStateVisuals.State, MobState.Critical);
                break;
            case MobState.Dead:
                EnsureComp<CollisionWakeComponent>(target);
                _standing.Down(target);
                _appearance.SetData(target, MobStateVisuals.State, MobState.Dead);
                break;
            case MobState.Invalid:
                //unused;
                break;
            default:
                throw new NotImplementedException();
        }
    }

    #region Event Subscribers

    private void OnSleepAttempt(EntityUid target, MobStateComponent component, ref TryingToSleepEvent args)
    {
        if (IsDead(target, component))
            args.Cancelled = true;
    }

    // Corvax-Wega-Disease-start
    private void OnSneezeAttempt(EntityUid target, MobStateComponent component, ref AttemptSneezeCoughEvent args)
    {
        if (IsDead(target, component))
            args.Cancelled = true;
    }
    // Corvax-Wega-Disease-end

    private void OnGettingStripped(EntityUid target, MobStateComponent component, BeforeGettingStrippedEvent args)
    {
        // Incapacitated or dead targets get stripped two or three times as fast. Makes stripping corpses less tedious.
        // Corvax-Wega-PreCritical-change-start
        if (IsDead(target, component))
            args.Multiplier /= 4;
        else if (IsCritical(target, component))
            args.Multiplier /= 3;
        else if (IsPreCritical(target, component))
            args.Multiplier /= 2;
        // Corvax-Wega-PreCritical-change-end
    }

    private void OnSpeakAttempt(EntityUid uid, MobStateComponent component, SpeakAttemptEvent args)
    {
        if (HasComp<AllowNextCritSpeechComponent>(uid))
        {
            RemCompDeferred<AllowNextCritSpeechComponent>(uid);
            return;
        }

        CheckAct(uid, component, args);
    }

    private void CheckAct(EntityUid target, MobStateComponent component, CancellableEntityEventArgs args)
    {
        switch (component.CurrentState)
        {
            case MobState.Dead:
            case MobState.Critical:
                args.Cancel();
                break;
        }
    }

    private void OnEquipAttempt(EntityUid target, MobStateComponent component, IsEquippingAttemptEvent args)
    {
        // is this a self-equip, or are they being stripped?
        if (args.Equipee == target)
            CheckAct(target, component, args);
    }

    private void OnUnequipAttempt(EntityUid target, MobStateComponent component, IsUnequippingAttemptEvent args)
    {
        // is this a self-equip, or are they being stripped?
        if (args.Unequipee == target)
            CheckAct(target, component, args);
    }

    private void OnCombatModeShouldHandInteract(EntityUid uid, MobStateComponent component, ref CombatModeShouldHandInteractEvent args)
    {
        // Disallow empty-hand-interacting in combat mode
        // for non-dead mobs
        if (!IsDead(uid, component))
            args.Cancelled = true;
    }

    private void OnAttemptPacifiedAttack(Entity<MobStateComponent> ent, ref AttemptPacifiedAttackEvent args)
    {
        args.Cancelled = true;
    }

    private void OnDamageModify(Entity<MobStateComponent> ent, ref DamageModifyEvent args)
    {
        args.Damage *= _damageable.UniversalMobDamageModifier;
    }

    #endregion
}
