using System.Linq;
using Content.Server.Disease;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Content.Shared.Surgery.Components;
using Content.Shared.Tools.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Surgery;

public sealed partial class SurgerySystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly DiseaseSystem _disease = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        GraphsInitialize();
        InternalDamageInitialize();
        UiInitialize();

        SubscribeLocalEvent<OperatedComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<OperatedComponent, DidEquipEvent>(OnDidEquip);
        SubscribeLocalEvent<OperatedComponent, BodyPartRemovedEvent>(OnBodyPartsChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<OperatedComponent>();
        while (query.MoveNext(out var uid, out var operated))
        {
            if (operated.OperatedPart)
                continue;

            if (operated.NextUpdateTick <= 0)
            {
                if (operated.InternalDamages.Count != 0 && !_mobState.IsDead(uid))
                {
                    ProcessInternalDamages(uid, operated);
                }

                if (operated.IsOperating)
                {
                    UpdateOperationSterility(uid, operated);
                }

                operated.NextUpdateTick = 5f;
            }
            operated.NextUpdateTick -= frameTime;
        }

        var sterileQuery = EntityQueryEnumerator<SterileComponent>();
        while (sterileQuery.MoveNext(out var uid, out var sterile))
        {
            if (sterile.NextUpdateTick <= 0)
            {
                sterile.NextUpdateTick = 5f;
                sterile.Amount -= sterile.DecayRate;
                if (sterile.Amount <= 0)
                {
                    RemComp<SterileComponent>(uid);
                }
            }
            sterile.NextUpdateTick -= frameTime;
        }
    }

    private void OnRejuvenate(Entity<OperatedComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.InternalDamages.Clear();
        RestoreMissingLimbs(ent);
    }

    private void RestoreMissingLimbs(Entity<OperatedComponent> entity)
    {
        if (!TryComp<BodyComponent>(entity, out var body) || body.Prototype == null)
            return;

        var rootPart = _body.GetRootPartOrNull(entity, body);
        if (rootPart == null)
            return;

        var prototype = _proto.Index<BodyPrototype>(body.Prototype.Value);
        foreach (var (slotId, slot) in prototype.Slots)
        {
            if (slotId == prototype.Root)
                continue;

            bool partExists = false;
            foreach (var part in _body.GetBodyChildren(entity, body))
            {
                var parentAndSlot = _body.GetParentPartAndSlotOrNull(part.Id);
                if (parentAndSlot != null && parentAndSlot.Value.Slot == slotId)
                {
                    partExists = true;
                    break;
                }
            }

            if (!partExists && slot.Part != null)
            {
                var newPart = Spawn(slot.Part, Transform(entity).Coordinates);
                var newPartComp = Comp<BodyPartComponent>(newPart);

                var parentSlot = prototype.Slots.FirstOrDefault(s => s.Value.Connections.Contains(slotId));
                if (parentSlot.Value != null)
                {
                    foreach (var parentPart in _body.GetBodyChildren(entity, body))
                    {
                        if (_body.CanAttachPart(parentPart.Id, slotId, newPart, parentPart.Component, newPartComp))
                        {
                            _body.AttachPart(parentPart.Id, slotId, newPart, parentPart.Component, newPartComp);
                            break;
                        }
                    }
                }

                if (slot.Organs != null)
                {
                    foreach (var (organSlot, organPrototype) in slot.Organs)
                    {
                        var newOrgan = Spawn(organPrototype, Transform(entity).Coordinates);
                        _body.InsertOrgan(newOrgan, newPart, organSlot);
                    }
                }
            }
        }
    }

    private void OnDidEquip(Entity<OperatedComponent> ent, ref DidEquipEvent args)
        => Timer.Spawn(1, () => { CheckAndRemoveInvalidClothing(ent); });

    private void OnBodyPartsChanged(Entity<OperatedComponent> ent, ref BodyPartRemovedEvent args)
        => Timer.Spawn(1, () => { CheckAndRemoveInvalidClothing(ent); });

    public void CheckAndRemoveInvalidClothing(Entity<OperatedComponent> ent)
    {
        if (!HasRequiredLimbs(ent, BodyPartType.Leg) || !HasRequiredLimbs(ent, BodyPartType.Foot))
            _inventory.TryUnequip(ent, "shoes", force: true);

        if (!HasRequiredLimbs(ent, BodyPartType.Arm) || !HasRequiredLimbs(ent, BodyPartType.Hand))
            _inventory.TryUnequip(ent, "gloves", force: true);

        if (!_body.GetBodyChildrenOfType(ent, BodyPartType.Head).Any())
        {
            string[] headSlots = { "head", "mask", "eyes", "ears" };
            foreach (var slot in headSlots)
            {
                _inventory.TryUnequip(ent, slot, force: true);
            }
        }
    }

    private bool HasRequiredLimbs(EntityUid uid, BodyPartType partType)
    {
        var parts = _body.GetBodyChildrenOfType(uid, partType).ToList();

        bool hasLeft = parts.Any(p => p.Component.Symmetry == BodyPartSymmetry.Left);
        bool hasRight = parts.Any(p => p.Component.Symmetry == BodyPartSymmetry.Right);

        if (partType == BodyPartType.Head)
            return parts.Count > 0;

        return hasLeft && hasRight;
    }

    private bool TryGetOperatingTable(EntityUid patient, out float tableModifier)
    {
        tableModifier = 1f;
        if (!TryComp<BuckleComponent>(patient, out var buckle) || buckle.BuckledTo == null)
            return false;

        return TryComp<OperatingTableComponent>(buckle.BuckledTo.Value, out var operating) &&
            (tableModifier = operating.Modifier) > 0;
    }
}
