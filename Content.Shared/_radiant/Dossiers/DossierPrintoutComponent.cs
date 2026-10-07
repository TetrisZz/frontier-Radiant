using Robust.Shared.GameStates;
using Content.Shared.Humanoid;

namespace Content.Shared._radiant.Dossiers;

/// <summary>Marks a printed dossier as a fixed template with individually fillable paper fields.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DossierPrintoutComponent : Component
{
    [DataField, AutoNetworkedField] public DossierKind Kind;
    [AutoNetworkedField] public string SubjectName = string.Empty;
    [AutoNetworkedField] public string PassportNumber = string.Empty;
    [AutoNetworkedField] public HumanoidCharacterAppearance? Portrait;
    [AutoNetworkedField] public string Species = string.Empty;
    [AutoNetworkedField] public Sex Sex;
    [AutoNetworkedField] public int Age;
}
