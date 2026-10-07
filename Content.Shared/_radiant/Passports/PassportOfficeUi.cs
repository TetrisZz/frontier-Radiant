using Content.Shared.UserInterface;
using Content.Shared.Humanoid;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Passports;

[RegisterComponent]
public sealed partial class PassportOfficeComponent : Component;

[Serializable, NetSerializable]
public enum PassportOfficeUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class PassportOfficeApplicant(string name, string species, string sex, int age, int height,
    string residence, string emergencyContact, string familyStatus, Sex sexValue)
{
    public readonly string Name = name;
    public readonly string Species = species;
    public readonly string Sex = sex;
    public readonly int Age = age;
    public readonly int Height = height;
    public readonly string Residence = residence;
    public readonly string EmergencyContact = emergencyContact;
    public readonly string FamilyStatus = familyStatus;
    public readonly Sex SexValue = sexValue;
}

[Serializable, NetSerializable]
public sealed class PassportOfficeUiState(Dictionary<NetEntity, PassportOfficeApplicant> candidates) : BoundUserInterfaceState
{
    public readonly Dictionary<NetEntity, PassportOfficeApplicant> Candidates = candidates;
}

[Serializable, NetSerializable]
public sealed class PassportOfficeRefreshMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PassportOfficeIssueMessage(NetEntity target, string name, int age, int height,
    string residence, string emergencyContact, string familyStatus) : BoundUserInterfaceMessage
{
    public readonly NetEntity Target = target;
    public readonly string Name = name;
    public readonly int Age = age;
    public readonly int Height = height;
    public readonly string Residence = residence;
    public readonly string EmergencyContact = emergencyContact;
    public readonly string FamilyStatus = familyStatus;
}
