namespace Content.Server.Chat;

using Content.Server.Chat.Systems;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage.Prototypes; ///radiant sector
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

/// <summary>
/// Causes an entity to automatically emote when taking damage.
/// </summary>
[RegisterComponent, Access(typeof(EmoteOnDamageSystem)), AutoGenerateComponentPause]
public sealed partial class EmoteOnDamageComponent : Component
{
    /// <summary>
    /// Chance of preforming an emote when taking damage and not on cooldown.
    /// </summary>
    [DataField("emoteChance"), ViewVariables(VVAccess.ReadWrite)]
    public float EmoteChance = 0.5f;

    /// <summary>
    /// A set of emotes that will be randomly picked from.
    /// <see cref="EmotePrototype"/>
    /// </summary>
    [DataField("emotes", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<EmotePrototype>)), ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> Emotes = new();

    /// <summary>
    /// Optional weights for specific emotes. Emotes without an explicit weight use 1.
    /// </summary>
    [DataField("emoteWeights"), ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, float> EmoteWeights = new();

    /// <summary>
    /// Optional chat message overrides for specific emotes.
    /// The emote itself still runs normally, so sounds and blockers keep using the original prototype. ///Radiant Sector
    /// </summary>
    [DataField("chatMessages"), ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, string> ChatMessages = new();

    /// <summary>
    ///radiant sector. Minimum total damage in one damage change required to emote.
    /// </summary>
    [DataField("minimumDamage"), ViewVariables(VVAccess.ReadWrite)]
    public float MinimumDamage;

    /// <summary>
    /// Optional upper bound for a random damage threshold roll.
    /// If greater than MinimumDamage, each damage event rolls a threshold between both values.
    /// </summary>
    [DataField("maximumDamage"), ViewVariables(VVAccess.ReadWrite)]
    public float MaximumDamage;

    /// <summary>
    /// If non-empty, only positive damage of these types counts toward the threshold.
    /// </summary>
    [DataField("damageTypes", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<DamageTypePrototype>)), ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> DamageTypes = new();

    /// <summary>
    /// Also send the emote in chat.
    /// <summary>
    [DataField("withChat"), ViewVariables(VVAccess.ReadWrite)]
    public bool WithChat = false;

    /// <summary>
    /// Hide the chat message from the chat window, only showing the popup.
    /// This does nothing if WithChat is false.
    /// <summary>
    [DataField("hiddenFromChatWindow")]
    public bool HiddenFromChatWindow = false;

    /// <summary>
    /// The simulation time of the last emote preformed due to taking damage.
    /// </summary>
    [DataField("lastEmoteTime", customTypeSerializer: typeof(TimeOffsetSerializer)), ViewVariables(VVAccess.ReadWrite)]
    [AutoPausedField]
    public TimeSpan LastEmoteTime = TimeSpan.Zero;

    /// <summary>
    /// The cooldown between emotes.
    /// </summary>
    [DataField("emoteCooldown"), ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan EmoteCooldown = TimeSpan.FromSeconds(2);
}
