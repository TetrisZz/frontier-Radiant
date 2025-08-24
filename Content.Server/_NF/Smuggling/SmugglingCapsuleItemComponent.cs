using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Utility;

namespace Content.Server._NF.Smuggling.Components
{
    [RegisterComponent]
    public sealed partial class SmugglingCapsuleItemComponent : Component
    {
        [DataField("channelNotify")]
        public string ChannelNotify = "Separatist";

        [DataField("channelNotifyOpponent")]
        public string ChannelNotifyOpponent = "Command";

        [DataField("dropGrid")]
        public ResPath DropGrid = new("/Maps/_radiant/DeadDrop/kapsyla_sep.yml");

        [DataField("minDistance")]
        public int MinimumDistance = 2500;

        [DataField("maxDistance")]
        public int MaximumDistance = 3500;

        [DataField("minSpawnDelaySeconds")]
        public int MinSpawnDelaySeconds = 60; // default 60

        [DataField("maxSpawnDelaySeconds")]
        public int MaxSpawnDelaySeconds = 120; // default 60

        [DataField("initialNotifyLoc")]
        public string InitialNotifyLoc = "smuggling-capsule-initiated";

        [DataField("arrivalLoc")]
        public string ArrivalLoc = "smuggling-capsule-arrived";

        [DataField("completeNotifyLoc")]
        public string CompleteNotifyLoc = "smuggling-capsule-complete";

        [DataField("gridColor")]
        public Color GridColor = new(121, 85, 61);
    }
}
