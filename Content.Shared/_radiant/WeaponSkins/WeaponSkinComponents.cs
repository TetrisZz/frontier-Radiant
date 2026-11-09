using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.WeaponSkins;

[Prototype("weaponSkin")]
public sealed partial class WeaponSkinPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public List<EntProtoId> Weapons = new();
    [DataField(required: true)] public string Sprite;
    [DataField(required: true)] public string State = "icon";
    [DataField(required: true)] public string ItemSprite;
    [DataField] public string? ClothingSprite;
    [DataField(required: true)] public SoundSpecifier? FireSound;
}

[RegisterComponent]
public sealed partial class WeaponSkinApplicatorComponent : Component
{
    [DataField(required: true)] public ProtoId<WeaponSkinPrototype> Skin;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class WeaponSkinAppliedComponent : Component
{
    [DataField(required: true), AutoNetworkedField] public ProtoId<WeaponSkinPrototype> Skin;
}
