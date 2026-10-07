using Content.Shared.Humanoid;
using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Passports;

[Serializable, NetSerializable]
public enum PassportUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum PassportPhotoKind : byte
{
    Portrait,
    Stranger,
    Sketch,
}

[Serializable, NetSerializable]
public sealed class PassportUiState(
    string ownerName,
    string species,
    string sex,
    int age,
    int heightCm,
    string number,
    string residence,
    string emergencyContact,
    string familyStatus,
    RadiantCitizenship citizenship,
    HumanoidCharacterAppearance? portrait,
    PassportPhotoKind photoKind,
    RadiantCitizenship? sealCitizenship,
    bool starSeal,
    bool isTemporary) : BoundUserInterfaceState
{
    public readonly string OwnerName = ownerName;
    public readonly string Species = species;
    public readonly string Sex = sex;
    public readonly int Age = age;
    public readonly int HeightCm = heightCm;
    public readonly string Number = number;
    public readonly string Residence = residence;
    public readonly string EmergencyContact = emergencyContact;
    public readonly string FamilyStatus = familyStatus;
    public readonly RadiantCitizenship Citizenship = citizenship;
    public readonly HumanoidCharacterAppearance? Portrait = portrait;
    public readonly PassportPhotoKind PhotoKind = photoKind;
    public readonly RadiantCitizenship? SealCitizenship = sealCitizenship;
    public readonly bool StarSeal = starSeal;
    public readonly bool IsTemporary = isTemporary;
}
