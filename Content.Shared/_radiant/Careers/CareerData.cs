using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Careers;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class CareerData
{
    [DataField] public Guid CharacterId;
    [DataField] public string Profession = "Adventurer";
    [DataField] public int Level;
    [DataField] public string? JobTitle;
    [DataField] public Dictionary<string, double> Progress = new();
    [DataField] public List<int> PendingRewards = new();
    [DataField] public bool StarterBoxGranted;
    [DataField] public int BoxTier;
    [DataField] public Guid? BoxId;

    public CareerData Clone() => new()
    {
        CharacterId = CharacterId,
        Profession = Profession,
        Level = Level,
        JobTitle = JobTitle,
        Progress = new(Progress),
        PendingRewards = new(PendingRewards),
        StarterBoxGranted = StarterBoxGranted,
        BoxTier = BoxTier,
        BoxId = BoxId,
    };
}
