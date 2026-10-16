using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._radiant.GhostSkins;

[Prototype("ghostSkin")]
public sealed partial class GhostSkinPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public string Name = default!;
    [DataField(required: true)] public SpriteSpecifier Sprite = default!;
    [DataField] public Vector2 Scale = Vector2.One;
    [DataField] public Color Color = Color.White;
    [DataField] public bool SupporterOnly;
    [DataField] public int Order;
}

public static class GhostSkinRules
{
    public const string Default = "GhostClassic";

    public static bool CanSelect(GhostSkinPrototype skin, bool hasSupporterAccess)
        => !skin.SupporterOnly || hasSupporterAccess;

    public static string Resolve(IPrototypeManager prototypes, string? selected, bool hasSupporterAccess)
        => selected != null && prototypes.TryIndex<GhostSkinPrototype>(selected, out var skin)
            && CanSelect(skin, hasSupporterAccess) ? selected : Default;
}
