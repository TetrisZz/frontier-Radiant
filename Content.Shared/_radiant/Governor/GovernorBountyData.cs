using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;
using Content.Shared._radiant.Governor.Prototypes;

namespace Content.Shared._radiant.Governor;

/// <summary>
/// A data structure for storing currently available bounties.
/// </summary>
[DataDefinition, NetSerializable, Serializable]
public readonly partial record struct GovernorBountyData
{
    /// <summary>
    /// A unique id used to identify the bounty
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The prototype containing information about the bounty.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField(required: true)]
    public ProtoId<GovernorBountyPrototype> Bounty { get; init; } = string.Empty;

    /// <summary>
    /// Whether or not this bounty has been accepted. Accepted bounties cannot be skipped.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public bool Accepted { get; init; } = false;

    public GovernorBountyData(GovernorBountyPrototype bounty, int uniqueIdentifier, bool accepted)
    {
        Bounty = bounty.ID;
        Id = $"{bounty.IdPrefix}{uniqueIdentifier:D3}";
        Accepted = accepted;
    }

    public GovernorBountyData(GovernorBountyPrototype bounty, string id, bool accepted)
    {
        Bounty = bounty.ID;
        Id = id;
        Accepted = accepted;
    }
}
