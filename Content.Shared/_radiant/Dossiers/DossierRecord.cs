using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Dossiers;

/// <summary>Persistent fields belong to a character slot, never to a round entity.</summary>
[Serializable, NetSerializable]
public sealed class DossierRecord
{
    public string Residence { get; set; } = "";
    public string FamilyStatus { get; set; } = "";
    public string Children { get; set; } = "";
    public string EmergencyContact { get; set; } = "";
    public string DistinguishingFeatures { get; set; } = "";
    public string Birthplace { get; set; } = "";
    public string Occupation { get; set; } = "";
    public string Education { get; set; } = "";
    public string Allergies { get; set; } = "";
    public string MedicalHistory { get; set; } = "";
    public string BloodGroup { get; set; } = "";
    public string MedicalInstructions { get; set; } = "";
    public string MedicalRestrictions { get; set; } = "";
    public string MedicalPhysiology { get; set; } = "";
    public string MedicalPsychology { get; set; } = "";
    public string MedicalNotes { get; set; } = "";
    public string SecurityPermissions { get; set; } = "";
    public string SecurityArrests { get; set; } = "";
    public string SecurityConvictions { get; set; } = "";
    public string SecurityNotes { get; set; } = "";
    public string LastMedicalEditor { get; set; } = "";
    public string LastSecurityEditor { get; set; } = "";
}

[Serializable, NetSerializable]
public enum DossierField : byte
{
    Residence, FamilyStatus, Children, EmergencyContact, DistinguishingFeatures,
    MedicalInstructions, MedicalRestrictions, MedicalPhysiology, MedicalPsychology, MedicalNotes,
    SecurityPermissions, SecurityArrests, SecurityConvictions, SecurityNotes,
}

[Serializable, NetSerializable]
public enum DossierKind : byte
{
    Personal, Medical, Security,
}
