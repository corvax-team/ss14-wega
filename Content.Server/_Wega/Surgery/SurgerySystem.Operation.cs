using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chat.Prototypes;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Surgery;
using Content.Shared.Surgery.Components;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;

    private void PerformSurgeryEffect(SurgeryActionType action, string? requiredPart, float successChance, string failureEffect, EntityUid patient, EntityUid? item)
    {
        if (!TryComp<OperatedComponent>(patient, out var comp))
            return;

        switch (action)
        {
            case SurgeryActionType.Cut:
                PerformCut((patient, comp), successChance, failureEffect);
                break;

            case SurgeryActionType.Retract:
                PerformRetract((patient, comp), successChance, failureEffect);
                break;

            case SurgeryActionType.ClampBleeding:
                PerformClamp((patient, comp), successChance, failureEffect);
                break;

            case SurgeryActionType.HealInternalDamage:
                PerformHealInternalDamage((patient, comp), requiredPart, successChance, failureEffect);
                break;

            case SurgeryActionType.RemoveOrgan:
                PerformRemoveOrgan((patient, comp), requiredPart, successChance, failureEffect);
                break;

            case SurgeryActionType.InsertOrgan:
                PerformInsertOrgan((patient, comp), item, requiredPart, successChance, failureEffect);
                break;

            case SurgeryActionType.RemovePart:
                PerformRemovePart((patient, comp), requiredPart, successChance, failureEffect);
                break;

            case SurgeryActionType.AttachPart:
                PerformAttachPart((patient, comp), item, requiredPart, successChance, failureEffect);
                break;

            case SurgeryActionType.Implanting:
                PerformImplant((patient, comp), item, requiredPart, successChance, failureEffect);
                break;

            default: break;
        }

        // Any action without anesthesia will cause pain.
        if (!HasComp<SleepingComponent>(patient) && !comp.OperatedPart && !_mobState.IsDead(patient))
            _chat.TryEmoteWithoutChat(patient, _proto.Index<EmotePrototype>("Scream"), true);
    }

    private void PerformCut(Entity<OperatedComponent> patient, float successChance, string failureEffect)
    {
        if (!RollSuccess(patient, successChance))
        {
            HandleFailure(patient, failureEffect);
            return;
        }

        if (!TryComp<BloodstreamComponent>(patient, out _))
            return;

        _bloodstream.TryModifyBleedAmount(patient, 2f);
    }

    private void PerformRetract(Entity<OperatedComponent> patient, float successChance, string failureEffect)
    {
        if (!RollSuccess(patient, successChance))
        {
            HandleFailure(patient, failureEffect);
            return;
        }
    }

    private void PerformClamp(Entity<OperatedComponent> patient, float successChance, string failureEffect)
    {
        if (!RollSuccess(patient, successChance))
        {
            HandleFailure(patient, failureEffect);
            return;
        }

        if (!TryComp<BloodstreamComponent>(patient, out _))
            return;

        _bloodstream.TryModifyBleedAmount(patient, -10f);
    }

    private void PerformHealInternalDamage(Entity<OperatedComponent> patient, string? damageType, float successChance, string failureEffect)
    {
        if (patient.Comp.Surgeon == null || string.IsNullOrEmpty(damageType))
            return;

        if (!RollSuccess(patient, successChance))
        {
            HandleFailure(patient, failureEffect);
            return;
        }

        var parts = damageType.Split('_');
        var bodyPart = parts.Length > 1 ? damageType : null;
        var actualDamageType = parts.Length > 1 ? parts[1] : damageType;

        if (!patient.Comp.InternalDamages.TryGetValue(actualDamageType, out var damagedParts))
            return;

        if (bodyPart != null)
        {
            if (damagedParts.Remove(bodyPart))
            {
                _popup.PopupEntity(Loc.GetString("surgery-internal-damage-healed",
                    ("damage", actualDamageType),
                    ("bodypart", bodyPart)), patient);
            }

            if (damagedParts.Count == 0)
            {
                patient.Comp.InternalDamages.Remove(actualDamageType);
            }
        }
    }

    private void PerformRemoveOrgan(Entity<OperatedComponent> patient, string? requiredOrgan, float successChance, string failureEffect)
    {
        if (patient.Comp.Surgeon == null || string.IsNullOrEmpty(requiredOrgan))
            return;

        if (!RollSuccess(patient, successChance))
        {
            HandleFailure(patient, failureEffect);
            return;
        }

        var organs = _body.GetBodyOrgans(patient)
            .Where(o => o.Component.OrganType == requiredOrgan)
            .ToList();

        if (organs.Count == 0)
            return;

        foreach (var (organId, _) in organs)
        {
            _body.RemoveOrgan(organId);
            _popup.PopupEntity(Loc.GetString("surgery-organ-removed"), patient);
            _hands.TryPickupAnyHand(patient.Comp.Surgeon.Value, organId);
        }

        if (HasComp<BloodstreamComponent>(patient))
            _bloodstream.TryModifyBleedAmount(patient, 2f);
    }

    private void PerformInsertOrgan(Entity<OperatedComponent> patient, EntityUid? item, string? requiredOrgan, float successChance, string failureEffect)
    {
        if (item == null || string.IsNullOrEmpty(requiredOrgan))
            return;

        if (!RollSuccess(patient, successChance))
        {
            HandleFailure(patient, failureEffect);
            return;
        }

        var targetSlot = FindOrganSlot(patient, requiredOrgan);
        if (targetSlot == null)
            return;

        if (_body.InsertOrgan(targetSlot.Value.PartUid, item.Value, targetSlot.Value.SlotId))
            _popup.PopupEntity(Loc.GetString("surgery-organ-inserted"), patient);
    }

    private void PerformRemovePart(Entity<OperatedComponent> patient, string? requiredPart, float successChance, string failureEffect)
    {
        if (patient.Comp.Surgeon == null || string.IsNullOrEmpty(requiredPart))
            return;

        if (!RollSuccess(patient, successChance))
        {
            HandleFailure(patient, failureEffect);
            return;
        }

        var bodyParts = new List<(EntityUid Id, BodyPartComponent Component)>();

        if (requiredPart.Contains('_'))
        {
            var parts = requiredPart.Split('_');

            if (parts.Length == 2)
            {
                var symmetry = parts[0].ToLower() switch
                {
                    "left" => BodyPartSymmetry.Left,
                    "right" => BodyPartSymmetry.Right,
                    _ => BodyPartSymmetry.None
                };

                var partType = parts[1].ToLower() switch
                {
                    "arm" => BodyPartType.Arm,
                    "hand" => BodyPartType.Hand,
                    "leg" => BodyPartType.Leg,
                    "foot" => BodyPartType.Foot,
                    _ => BodyPartType.Other
                };

                bodyParts = _body.GetBodyChildren(patient)
                    .Where(p => p.Component.PartType == partType && p.Component.Symmetry == symmetry)
                    .ToList();
            }
        }
        else
        {
            var partType = requiredPart.ToLower() switch
            {
                "torso" => BodyPartType.Torso,
                "head" => BodyPartType.Head,
                "tail" => BodyPartType.Tail,
                _ => BodyPartType.Other
            };

            bodyParts = _body.GetBodyChildren(patient)
                .Where(p => p.Component.PartType == partType)
                .ToList();
        }

        if (bodyParts.Count == 0)
            return;

        foreach (var (partId, part) in bodyParts)
        {
            var parentSlot = _body.GetParentPartAndSlotOrNull(partId);
            if (parentSlot == null)
                continue;

            var (parentId, slotId) = parentSlot.Value;
            if (TryComp<BodyPartComponent>(parentId, out var parentPart))
            {
                var containerId = SharedBodySystem.GetPartSlotContainerId(slotId);
                if (_container.TryGetContainer(parentId, containerId, out var container))
                {
                    _container.Remove(partId, container);
                    _popup.PopupEntity(Loc.GetString("surgery-part-removed"), patient);
                    _hands.TryPickupAnyHand(patient.Comp.Surgeon.Value, partId);
                }
            }
        }

        if (HasComp<BloodstreamComponent>(patient))
            _bloodstream.TryModifyBleedAmount(patient, 2f);
    }

    private void PerformAttachPart(Entity<OperatedComponent> patient, EntityUid? item, string? requiredPart, float successChance, string failureEffect)
    {
        if (item == null || string.IsNullOrEmpty(requiredPart) || !TryComp<BodyPartComponent>(item, out _))
            return;

        if (!RollSuccess(patient, successChance))
        {
            HandleFailure(patient, failureEffect);
            return;
        }

        var slotId = ParseSlotId(requiredPart.ToLower(), "body_part_slot_");
        if (string.IsNullOrEmpty(slotId))
            return;

        var parentPart = _body.GetBodyPartsWithSlot(patient, slotId).FirstOrDefault();
        if (parentPart == EntityUid.Invalid)
            return;

        if (!_body.AttachPart(parentPart, slotId, item.Value))
            return;

        _popup.PopupEntity(Loc.GetString("surgery-part-attached"), patient);
    }

    private void PerformImplant(Entity<OperatedComponent> patient, EntityUid? item, string? requiredPart, float successChance, string failureEffect)
    {
        if (item == null || string.IsNullOrEmpty(requiredPart))
            return;

        if (!RollSuccess(patient, successChance))
        {
            HandleFailure(patient, failureEffect);
            return;
        }

        // TODO: Имплантация импланта или устройства в тело
    }

    private bool RollSuccess(Entity<OperatedComponent> ent, float baseChance)
    {
        var adjustedChance = baseChance * Math.Clamp(ent.Comp.Sterility, 0f, 1.5f);
        if (TryGetOperatingTable(ent, out var tableModifier))
            adjustedChance *= tableModifier;

        return _random.Prob(adjustedChance);
    }

    private (EntityUid PartUid, string SlotId)? FindOrganSlot(EntityUid bodyId, string organType)
    {
        foreach (var part in _body.GetBodyChildren(bodyId))
        {
            foreach (var (slotId, _) in part.Component.Organs)
            {
                if (slotId.Equals(organType, StringComparison.OrdinalIgnoreCase))
                {
                    return (part.Id, slotId);
                }
            }
        }
        return null;
    }

    private string? ParseSlotId(string? fullSlotId, string prefix)
    {
        if (string.IsNullOrEmpty(fullSlotId))
            return null;

        return fullSlotId.StartsWith(prefix)
            ? fullSlotId.Substring(prefix.Length)
            : fullSlotId;
    }

    private void HandleFailure(EntityUid patient, string failureEffect)
    {
        switch (failureEffect)
        {
            case "Bleed":
                TryAddInternalDamage(patient, "ArterialBleeding");
                break;
        }

        _popup.PopupPredicted(Loc.GetString($"surgery-handle-failed-{failureEffect.ToLower()}", ("patient", Identity.Entity(patient, EntityManager))),
            patient, null, PopupType.MediumCaution);
    }
}
