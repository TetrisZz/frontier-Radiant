using Content.Shared.Chat.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Interaction
{
    [Serializable, NetSerializable]
    public sealed class InteractionPressedEvent : EntityEventArgs
    {
        public NetEntity User { get; }
        public string InteractionId { get; }
        public NetEntity? Target { get; }
        public InteractionPrototype? Prototype { get; }

        /// <summary>
        /// If true, only the user and target should see the interaction messages/sound.
        /// </summary>
        public bool HideFromOthers { get; }

        /// <summary>
        /// Client-side arousal from the local prototype manager (full data). Used when server-resolved <see cref="InteractionPrototype.EffectiveArousal"/> is 0 (e.g. incomplete network copy).
        /// </summary>
        public int ArousalHint { get; }

        public InteractionPressedEvent(NetEntity user, string interactionId, NetEntity? target, InteractionPrototype? prototype, bool hideFromOthers, int arousalHint = 0)
        {
            User = user;
            InteractionId = interactionId;
            Target = target;
            Prototype = prototype;
            HideFromOthers = hideFromOthers;
            ArousalHint = arousalHint;
        }
    }
}