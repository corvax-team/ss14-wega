using Content.Shared.Actions;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Mindshield.Components;
using Robust.Shared.Containers;

namespace Content.Shared.Mindshield.FakeMindShield;

public sealed class SharedFakeMindShieldImplantSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actionsSystem = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SubdermalImplantComponent, FakeMindShieldToggleEvent>(OnFakeMindShieldToggle);
        SubscribeLocalEvent<FakeMindShieldImplantComponent, ImplantImplantedEvent>(ImplantCheck);
        SubscribeLocalEvent<FakeMindShieldImplantComponent, EntGotRemovedFromContainerMessage>(ImplantDraw);
        SubscribeLocalEvent<FakeMindShieldComponent, ImplantRemovedEvent>(ImplantDraw); // Corvax-Wega-Surgery
    }

    /// <summary>
    /// Raise the Action of a Implanted user toggling their implant to the FakeMindshieldComponent on their entity
    /// </summary>
    private void OnFakeMindShieldToggle(Entity<SubdermalImplantComponent> entity, ref FakeMindShieldToggleEvent ev)
    {
        ev.Handled = true;
        if (entity.Comp.ImplantedEntity is not { } ent)
            return;

        if (!TryComp<FakeMindShieldComponent>(ent, out var comp))
            return;
        // TODO: is there a reason this cant set ev.Toggle = true;
        _actionsSystem.SetToggled((ev.Action, ev.Action), !comp.IsEnabled); // Set it to what the Mindshield component WILL be after this
        RaiseLocalEvent(ent, ev); //this reraises the action event to support an eventual future Changeling Antag which will also be using this component for it's "mindshield" ability
    }
    private void ImplantCheck(EntityUid uid, FakeMindShieldImplantComponent component, ref ImplantImplantedEvent ev)
    {
        if (ev.Implanted != null)
            EnsureComp<FakeMindShieldComponent>(ev.Implanted.Value);
    }

    private void ImplantDraw(Entity<FakeMindShieldImplantComponent> ent, ref EntGotRemovedFromContainerMessage ev)
    {
        RemComp<FakeMindShieldComponent>(ev.Container.Owner);
    }

    // Corvax-Wega-Surgery-start
    private void ImplantDraw(Entity<FakeMindShieldComponent> ent, ref ImplantRemovedEvent ev)
    {
        RemComp<FakeMindShieldComponent>(ent);
    }
    // Corvax-Wega-Surgery-end
}
