using Robust.Shared.Network;

namespace Content.Server._radiant.Careers;

[RegisterComponent]
public sealed partial class CareerFleetComponent : Component
{
    [DataField] public string Profession = "Mining";
    [ViewVariables] public EntityUid Grid;
    [ViewVariables] public NetUserId Buyer;
    [ViewVariables] public Dictionary<NetUserId, EntityUid> Participants = new();
    [ViewVariables] public TimeSpan? OfflineSince;
    [ViewVariables] public TimeSpan? UnpoweredSince;
    [ViewVariables] public bool SpawnEnabled = true;
}

// Kept on the grid as well so saved ships cannot lose their career classification.
[RegisterComponent]
public sealed partial class CareerFleetGridComponent : Component
{
    [DataField] public string Vessel = string.Empty;
}

[RegisterComponent]
public sealed partial class CareerBaseStationComponent : Component
{
    [DataField] public HashSet<string> Professions = new();

    public bool Allows(string profession) => Professions.Count == 0 || Professions.Contains(profession);
}
