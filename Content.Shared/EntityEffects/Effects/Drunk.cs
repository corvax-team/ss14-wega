using Content.Shared.Drunk;
using Content.Shared.Genetics; // Corvax-Wega-Genetics
using Robust.Shared.Prototypes;

namespace Content.Shared.EntityEffects.Effects;

public sealed partial class Drunk : EntityEffect
{
    /// <summary>
    ///     BoozePower is how long each metabolism cycle will make the drunk effect last for.
    /// </summary>
    [DataField]
    public float BoozePower = 3f;

    /// <summary>
    ///     Whether speech should be slurred.
    /// </summary>
    [DataField]
    public bool SlurSpeech = true;

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
        => Loc.GetString("reagent-effect-guidebook-drunk", ("chance", Probability));

    public override void Effect(EntityEffectBaseArgs args)
    {
        var boozePower = BoozePower;

        if (args is EntityEffectReagentArgs reagentArgs) {
            boozePower *= reagentArgs.Scale.Float();
        }

        if (args.EntityManager.HasComponent<SoberGenComponent>(args.TargetEntity)) // Corvax-Wega-Genetics
            boozePower *= 0.5f; // Corvax-Wega-Genetics

        var drunkSys = args.EntityManager.EntitySysManager.GetEntitySystem<SharedDrunkSystem>();
        drunkSys.TryApplyDrunkenness(args.TargetEntity, boozePower, SlurSpeech);
    }
}
