using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared.Chat.Prototypes
{
    [Prototype("interaction")]
    [Serializable] //Never add NetSerialization here.
    public sealed partial class InteractionPrototype : IPrototype, IInheritingPrototype
    {
        [IdDataField]
        public string ID { get; private set; } = default!;

        /// <inheritdoc />
        [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<InteractionPrototype>))]
        public string[]? Parents { get; }

        /// <inheritdoc />
        [NeverPushInheritance]
        [AbstractDataField]
        public bool Abstract { get; }

        [DataField(required: true)]
        public string Name = default!;

        [DataField]
        public string Icon = "/Textures/_radiant/Interface/InteractionPanel/heart.png";

        [DataField("delay")]
        public float DoAfterDelay { get; set; } = 0f;

        [DataField("erp")]
        public bool ERP { get; set; } = false;

        [DataField("interactSound")]
        public SoundSpecifier? InteractSound;

        [DataField("points")]
        public int Points { get; set; } = 0;

        [DataField("soundPerceivedByOthers")]
        public bool SoundPerceivedByOthers = true;

        [DataField("useDelay")]
        public TimeSpan UseDelay { get; set; } = TimeSpan.FromSeconds(2);

        [DataField("userMessages")]
        public List<string> UserMessages = new();

        [DataField("targetMessages")]
        public List<string> TargetMessages = new();

        [DataField("otherMessages")]
        public List<string> OtherMessages = new();

        [DataField]
        public List<string>? AllowedGenders = new() { "all" };

        [DataField]
        public List<string>? AllowedSpecies = new() { "all" };

        [DataField]
        public List<string>? BlackListSpecies;

        [DataField]
        public List<string>? NearestAllowedGenders = new() { "all" };

        [DataField]
        public List<string>? NearestAllowedSpecies = new() { "all" };

        [DataField]
        public List<string>? OneRequiredClothingSlots;

        [DataField]
        public List<string>? RequiredClothingSlots;

        [DataField]
        public bool RequiresStrapon { get; set; } = false;

        [DataField]
        public List<string>? TargetEntityId;
    }
}