using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Governor.Prototypes;

/// <summary>
/// A prototype for a governor bounty, a set of items that must
/// be delivered to the governor to receive a reward.
/// </summary>
[Prototype, Serializable, NetSerializable]
public sealed partial class GovernorBountyPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The monetary reward for completing the bounty
    /// </summary>
    [DataField(required: true)]
    public int Reward;

    /// <summary>
    /// A stack of tokens awarded along with the credits. Required, must be set in the prototype.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<StackPrototype> TokenReward;

    /// <summary>
    /// How many token units are awarded.
    /// </summary>
    [DataField]
    public int TokenRewardCount = 1;

    /// <summary>
    /// A description for flava purposes.  If empty, will fallback to a default option.
    /// </summary>
    [DataField]
    public LocId Description = string.Empty;

    /// <summary>
    /// The entries that must be satisfied for the cargo bounty to be complete.
    /// </summary>
    [DataField(required: true)]
    public List<GovernorBountyItemEntry> Entries = new();

    /// <summary>
    /// Whether or not to spawn a chest for this item.
    /// </summary>
    [DataField]
    public bool SpawnChest = true;

    /// <summary>
    /// A prefix appended to the beginning of a bounty's ID.
    /// </summary>
    [DataField]
    public string IdPrefix = "GOV-";
}

[DataDefinition, Serializable, NetSerializable]
public readonly partial record struct GovernorBountyItemEntry()
{
    /// <summary>
    /// An internal ID for matching, should be used in GovernorBountyItemComponent
    /// </summary>
    [IdDataField]
    public string ID { get; init; } = default!;

    /// <summary>
    /// If set, any prototype whose ID starts with this prefix matches (e.g. "any trophy").
    /// </summary>
    [DataField]
    public string Prefix { get; init; } = string.Empty;

    /// <summary>
    /// How much of the item must be present to satisfy the entry
    /// </summary>
    [DataField]
    public int Amount { get; init; } = 1;

    /// <summary>
    /// A player-facing name for the item.
    /// </summary>
    [DataField]
    public LocId Name { get; init; } = string.Empty;

    /// <summary>
    /// If set, this entry requires a liquid reagent instead of an item.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype> Reagent { get; init; } = string.Empty;

    /// <summary>
    /// How many units of the reagent are required.
    /// </summary>
    [DataField]
    public FixedPoint2 ReagentAmount { get; init; } = FixedPoint2.Zero;
}
