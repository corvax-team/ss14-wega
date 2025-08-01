using Content.Shared.Bed.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle.Components;
using Content.Shared.Disease;

namespace Content.Server.Disease.Cures
{
    /// <summary>
    /// Cures the disease after a certain amount of time
    /// strapped.
    /// </summary>
    public sealed partial class DiseaseBedrestCure : DiseaseCure
    {
        [ViewVariables(VVAccess.ReadWrite)]
        public int Ticker = 0;

        /// How many extra ticks you get for sleeping.
        [DataField("sleepMultiplier")]
        public int SleepMultiplier = 3;

        [DataField("maxLength", required: true)]
        [ViewVariables(VVAccess.ReadWrite)]
        public int MaxLength = 60;

        public override bool Cure(DiseaseEffectArgs args)
        {
            if (!args.EntityManager.TryGetComponent<BuckleComponent>(args.DiseasedEntity, out var buckle) ||
                !args.EntityManager.HasComponent<HealOnBuckleComponent>(buckle.BuckledTo))
                return false;

            var ticks = 1;
            if (args.EntityManager.HasComponent<SleepingComponent>(args.DiseasedEntity))
                ticks *= SleepMultiplier;

            if (buckle.Buckled)
                Ticker += ticks;
            return Ticker >= MaxLength;
        }

        public override string CureText()
        {
            return (Loc.GetString("diagnoser-cure-bedrest", ("time", MaxLength), ("sleep", (MaxLength / SleepMultiplier))));
        }
    }
}
