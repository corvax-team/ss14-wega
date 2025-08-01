using Robust.Shared.Timing;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Robust.Shared.Physics.Events;
using Content.Shared.Climbing.Systems;
using Content.Shared.Damage.Systems;
using Content.Shared.Actions;

namespace Content.Shared._Wega.Resomi.Abilities;

public abstract class SharedAgillitySkillSystem : EntitySystem
{
    [Dependency] protected readonly IGameTiming Timing = default!;
    [Dependency] protected readonly SharedPopupSystem _popup = default!;
    [Dependency] protected readonly ClimbSystem _climb = default!;
    [Dependency] protected readonly SharedStaminaSystem _stamina = default!;
    [Dependency] protected readonly SharedActionsSystem _actions = default!;

    protected const int BaseCollisionGroup = (int)(CollisionGroup.MobMask);

    public override void Initialize()
    {
        SubscribeLocalEvent<AgillitySkillComponent, StartCollideEvent>(DoJump);
        SubscribeLocalEvent<AgillitySkillComponent, SwitchAgillity>(OnHandleStateChange);
    }

    private void DoJump(Entity<AgillitySkillComponent> ent, ref StartCollideEvent args)
    {
        if (!ent.Comp.Active || !ent.Comp.JumpEnabled
            || args.OurFixture.CollisionMask != BaseCollisionGroup
            || args.OtherFixture.CollisionMask != (int)CollisionGroup.TableMask)
            return;

        _stamina.TryTakeStamina(ent.Owner, ent.Comp.StaminaDamageOnJump);
        _climb.ForciblySetClimbing(ent.Owner, args.OtherEntity);
    }

    private void OnHandleStateChange(Entity<AgillitySkillComponent> ent, ref SwitchAgillity args)
    {
        _actions.SetToggled(args.action.Owner, args.toggled);
    }
}
