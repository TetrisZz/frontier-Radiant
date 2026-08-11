using Content.Shared.DoAfter;
using Content.Shared.DeviceLinking;
using Content.Shared.Inventory;
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
