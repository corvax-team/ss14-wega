using Content.Shared.Disease;
using Content.Shared.Damage;
using JetBrains.Annotations;

namespace Content.Server.Disease.Effects
{
    /// <summary>
    /// Deals or heals damage to the host
    /// </summary>
    [UsedImplicitly]
    public sealed partial class DiseaseHealthChange : DiseaseEffect
    {
        [DataField("damage", required: true)]
        [ViewVariables(VVAccess.ReadWrite)]
        public DamageSpecifier Damage = default!;
        public override void Effect(DiseaseEffectArgs args)
        {
            var damageableSystem = args.EntityManager.System<DamageableSystem>();
            damageableSystem.TryChangeDamage(args.DiseasedEntity, Damage, true, false);
        }
    }
}
