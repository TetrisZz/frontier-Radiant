using Robust.Shared.GameStates;
using Content.Shared._radiant.Passports;
using Content.Shared.Humanoid;

namespace Content.Shared._EE.Contractors.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class PassportComponent : Component
{
    [DataField, AutoNetworkedField] public bool IsClosed = true;
    [DataField, AutoNetworkedField] public string Design = "";
    [DataField, AutoNetworkedField] public RadiantCitizenship Citizenship = RadiantCitizenship.Asgard;
    [DataField, AutoNetworkedField] public string OwnerName = "";
    [DataField, AutoNetworkedField] public string Species = "";
    [DataField, AutoNetworkedField] public string Sex = "";
    [DataField, AutoNetworkedField] public int Age;
    [DataField, AutoNetworkedField] public int HeightCm;
    [DataField, AutoNetworkedField] public string Number = "";
    [DataField, AutoNetworkedField] public string FaceSignature = "";
    [DataField, AutoNetworkedField] public string Residence = "";
    [DataField, AutoNetworkedField] public string EmergencyContact = "";
    [DataField, AutoNetworkedField] public string FamilyStatus = "";
    [DataField] public HumanoidCharacterAppearance? Portrait;
    [DataField] public PassportPhotoKind PhotoKind;
    [DataField] public RadiantCitizenship? SealCitizenship;
    [DataField] public bool StarSeal;
    [DataField, AutoNetworkedField] public bool IsForged;
    [DataField, AutoNetworkedField] public bool IsTemporary;
}
