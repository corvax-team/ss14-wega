using Content.Shared.StatusEffect;
using Content.Shared.Inventory;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Tools.Components;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Clothing; // Corvax-Wega-Wielder

namespace Content.Shared.Eye.Blinding.Systems
{
    public sealed class EyeProtectionSystem : EntitySystem
    {
        [Dependency] private readonly StatusEffectsSystem _statusEffectsSystem = default!;
        [Dependency] private readonly BlindableSystem _blindingSystem = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<RequiresEyeProtectionComponent, ToolUseAttemptEvent>(OnUseAttempt);
            SubscribeLocalEvent<RequiresEyeProtectionComponent, ItemToggledEvent>(OnWelderToggled);

            SubscribeLocalEvent<EyeProtectionComponent, ItemMaskToggledEvent>(OnMaskToggled); // Corvax-Wega-Wielder
            SubscribeLocalEvent<EyeProtectionComponent, GetEyeProtectionEvent>(OnGetProtection);
            SubscribeLocalEvent<EyeProtectionComponent, InventoryRelayedEvent<GetEyeProtectionEvent>>(OnGetRelayedProtection);
        }

        private void OnGetRelayedProtection(EntityUid uid, EyeProtectionComponent component,
            InventoryRelayedEvent<GetEyeProtectionEvent> args)
        {
            OnGetProtection(uid, component, args.Args);
        }

        private void OnGetProtection(EntityUid uid, EyeProtectionComponent component, GetEyeProtectionEvent args)
        {
            if (!component.Enabled) // Corvax-Wega-Wielder
                return; // Corvax-Wega-Wielder

            args.Protection += component.ProtectionTime;
        }

        // Corvax-Wega-Wielder-start
        private void OnMaskToggled(Entity<EyeProtectionComponent> ent, ref ItemMaskToggledEvent args)
        {
            ent.Comp.Enabled = !args.Mask.Comp.IsToggled;
        }
        // Corvax-Wega-Wielder-end

        private void OnUseAttempt(EntityUid uid, RequiresEyeProtectionComponent component, ToolUseAttemptEvent args)
        {
            if (!component.Toggled)
                return;

            if (!TryComp<BlindableComponent>(args.User, out var blindable) || blindable.IsBlind)
                return;

            var ev = new GetEyeProtectionEvent();
            RaiseLocalEvent(args.User, ev);

            var time = (float)(component.StatusEffectTime - ev.Protection).TotalSeconds;
            if (time <= 0)
                return;

            // Add permanent eye damage if they had zero protection, also somewhat scale their temporary blindness by
            // how much damage they already accumulated.
            _blindingSystem.AdjustEyeDamage((args.User, blindable), 1);
            var statusTimeSpan = TimeSpan.FromSeconds(time * MathF.Sqrt(blindable.EyeDamage));
            _statusEffectsSystem.TryAddStatusEffect(args.User, TemporaryBlindnessSystem.BlindingStatusEffect,
                statusTimeSpan, false, TemporaryBlindnessSystem.BlindingStatusEffect);
        }
        private void OnWelderToggled(EntityUid uid, RequiresEyeProtectionComponent component, ItemToggledEvent args)
        {
            component.Toggled = args.Activated;
            Dirty(uid, component);
        }
    }
}
