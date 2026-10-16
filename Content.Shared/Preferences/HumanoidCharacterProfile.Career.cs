using Content.Shared._radiant.Careers;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    [DataField]
    public CareerData Career { get; set; } = new();
}
