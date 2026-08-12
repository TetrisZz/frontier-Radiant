using Content.Shared.DoAfter;
using Content.Shared.DeviceLinking;
using Content.Shared.Inventory;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.ERP.Components
{
    [RegisterComponent]
    [ComponentProtoName("SexToy")]
    public sealed partial class SexToyComponent : Component
    {
        [DataField]
        public List<string> Prototype = new();
    }

    [RegisterComponent]
    [ComponentProtoName("Vibrator")]
    public sealed partial class VibratorComponent : Component
    {
        [DataField]
        public VibratorMode Mode = VibratorMode.Medium;

        [DataField]
        public bool Muted;

        [DataField]
        public SoundSpecifier ActiveSound = new SoundPathSpecifier("/Audio/_radiant/Lewd/vibrate_loop.ogg");

        [DataField]
        public float PassiveArousalAmount = 2f;

        [DataField]
        public TimeSpan MediumArousalInterval = TimeSpan.FromSeconds(30);

        [DataField]
        public TimeSpan HardArousalInterval = TimeSpan.FromSeconds(15);

        public TimeSpan NextPassiveArousal = TimeSpan.Zero;

        [DataField]
        public TimeSpan MediumMoanInterval = TimeSpan.FromSeconds(60);

        [DataField]
        public TimeSpan HardMoanInterval = TimeSpan.FromSeconds(30);

        public TimeSpan NextPassiveMoan = TimeSpan.Zero;

        /// <summary>
        /// Chance to involuntarily moan after speaking while this vibrator is active in the plug slot.
        /// </summary>
        [DataField]
        public float MoanChance = 0.1f;

        [DataField]
        public ProtoId<SinkPortPrototype> TogglePort = "Toggle";

        [DataField]
        public ProtoId<SinkPortPrototype> OnPort = "On";

        [DataField]
        public ProtoId<SinkPortPrototype> OffPort = "Off";
    }

    [RegisterComponent]
    [ComponentProtoName("Strapon")]
    public sealed partial class StraponComponent : Component
    {
    }
}

[Serializable, NetSerializable]
public sealed partial class InteractionDoAfterEvent : SimpleDoAfterEvent
{
}

[Serializable, NetSerializable]
public sealed partial class SexToyDoAfterEvent : SimpleDoAfterEvent
{
}

[Serializable, NetSerializable]
public sealed partial class VibratorDoAfterEvent : SimpleDoAfterEvent
{
}

public enum VibratorMode : byte
{
    Off,
    Low,
    Medium,
    Hard,
}
