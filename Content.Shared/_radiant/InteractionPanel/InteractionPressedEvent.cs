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

        public InteractionPressedEvent(NetEntity user, string interactionId, NetEntity? target, InteractionPrototype? prototype)
        {
            User = user;
            InteractionId = interactionId;
            Target = target;
            Prototype = prototype;
        }
    }
}