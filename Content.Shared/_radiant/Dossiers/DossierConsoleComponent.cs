namespace Content.Shared._radiant.Dossiers;

[RegisterComponent]
public sealed partial class DossierConsoleComponent : Component
{
    [DataField] public DossierKind Kind;
    public EntityUid? Selected;
}
