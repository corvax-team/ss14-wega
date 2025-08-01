using Content.Shared.Inventory;
using Robust.Shared.Audio;

namespace Content.Shared.Radio.Components;

/// <summary>
///     This component relays radio messages to the parent entity's chat when equipped.
/// </summary>
[RegisterComponent]
public sealed partial class HeadsetComponent : Component
{
    [DataField("enabled")]
    public bool Enabled = true;

    public bool IsEquipped = false;

    [DataField("requiredSlot")]
    public SlotFlags RequiredSlot = SlotFlags.EARS;

    [DataField] // Corvax-Wega-Headset
    public SoundSpecifier Sound; // Corvax-Wega-Headset

    [DataField] // Corvax-Wega-Headset
    public bool ToggledSound = true; // Corvax-Wega-Headset
}
