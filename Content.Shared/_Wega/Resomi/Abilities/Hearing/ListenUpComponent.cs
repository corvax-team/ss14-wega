using Robust.Shared.Utility;

namespace Content.Shared._Wega.Resomi.Abilities.Hearing;

[RegisterComponent]
public sealed partial class ListenUpComponent : Component
{
    [DataField]
    public float radius = 8f;

    [DataField]
    public SpriteSpecifier Sprite = new SpriteSpecifier.Rsi(new("/Textures/_Wega/Mobs/Species/Resomi/Abilities/noise_effect.rsi"), "noise");
}
