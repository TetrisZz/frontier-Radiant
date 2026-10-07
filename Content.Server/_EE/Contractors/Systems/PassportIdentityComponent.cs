namespace Content.Server._EE.Contractors.Systems;

/// <summary>Round-local identity record. The passport is only a physical credential for it.</summary>
[RegisterComponent]
public sealed partial class PassportIdentityComponent : Component
{
    [DataField] public string Number = "";
    // A replacement document has its own serial; the permanent number remains the identity key.
    public string CurrentDocumentNumber = "";
    public string DossierNumber => CurrentDocumentNumber.Length > 0 ? CurrentDocumentNumber : Number;
    [DataField] public string RegisteredName = "";
    [DataField] public Content.Shared._radiant.Passports.RadiantCitizenship Citizenship;
    [DataField] public bool NeedsReview;
}
