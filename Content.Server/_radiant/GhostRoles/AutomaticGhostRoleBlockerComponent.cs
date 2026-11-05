namespace Content.Server._radiant.GhostRoles;

/// <summary>Prevents events and reagents from turning ordinary station fixtures into ghost roles.</summary>
[RegisterComponent]
public sealed partial class AutomaticGhostRoleBlockerComponent : Component
{
    [DataField]
    public bool Enabled = true;
}
