﻿using Content.Shared.DisplacementMap;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Inventory;

[RegisterComponent, NetworkedComponent]
[Access(typeof(InventorySystem))]
[AutoGenerateComponentState(true)]
public sealed partial class InventoryComponent : Component
{
    [DataField("templateId", customTypeSerializer: typeof(PrototypeIdSerializer<InventoryTemplatePrototype>))]
    [AutoNetworkedField]
    public string TemplateId { get; set; } = "human";

    [DataField("speciesId"), AutoNetworkedField] public string? SpeciesId { get; set; } // Corvax-Wega-Edit

    public SlotDefinition[] Slots = Array.Empty<SlotDefinition>();
    public ContainerSlot[] Containers = Array.Empty<ContainerSlot>();

    [DataField, AutoNetworkedField] // Corvax-Wega-Edit
    public Dictionary<string, DisplacementData> Displacements = new();

    /// <summary>
    /// Alternate displacement maps, which if available, will be selected for the player of the appropriate gender.
    /// </summary>
    [DataField, AutoNetworkedField] // Corvax-Wega-Edit
    public Dictionary<string, DisplacementData> FemaleDisplacements = new();

    /// <summary>
    /// Alternate displacement maps, which if available, will be selected for the player of the appropriate gender.
    /// </summary>
    [DataField, AutoNetworkedField] // Corvax-Wega-Edit
    public Dictionary<string, DisplacementData> MaleDisplacements = new();

    // Corvax-Wega-Genetics-start
    public void Clone(InventoryComponent targetInventory)
    {
        this.SpeciesId = targetInventory.SpeciesId;
        this.Displacements = targetInventory.Displacements;
        this.FemaleDisplacements = targetInventory.FemaleDisplacements;
        this.MaleDisplacements = targetInventory.MaleDisplacements;
    }
    // Corvax-Wega-Genetics-end
}

/// <summary>
/// Raised if the <see cref="InventoryComponent.TemplateId"/> of an inventory changed.
/// </summary>
[ByRefEvent]
public struct InventoryTemplateUpdated;
