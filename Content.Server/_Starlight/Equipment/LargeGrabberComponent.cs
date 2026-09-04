using System.Numerics;
using Content.Shared.DoAfter;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Containers;

namespace Content.Server._Starlight.Equipment;

// Radiant sector: portable Starlight grabber used by the Ripley cyber arm.
[RegisterComponent]
public sealed partial class LargeGrabberComponent : Component
{
    [DataField] public float GrabEnergyCost = 30;
    [DataField] public float GrabDelay = 2.5f;
    [DataField] public Vector2 DepositOffset = new(0, -1);
    [DataField] public int MaxContents = 10;
    [DataField] public SoundSpecifier GrabSound = new SoundPathSpecifier("/Audio/Mecha/sound_mecha_hydraulic.ogg");
    [DataField] public EntityWhitelist? Blacklist = new()
    {
        Components = ["WallMount", "Anomaly", "Mech", "MobState"],
    };
    [DataField] public bool DropOnContainerChange;
    public EntityUid? AudioStream;
    public Container ItemContainer = default!;
    [DataField] public DoAfterId? DoAfter;
}
