namespace Content.Server._radiant.Mech.Components;

/// <summary>
/// Marks a mech cockpit that protects its pilot from pressure damage without supplying breathable air.
/// </summary>
[RegisterComponent]
public sealed partial class ClarkePressureProtectionComponent : Component
{
}

/// <summary>
/// Tracks pressure immunity temporarily supplied to a pilot by a Clarke cockpit.
/// </summary>
[RegisterComponent]
public sealed partial class ClarkePilotPressureProtectionComponent : Component
{
    public bool AddedPressureImmunity;
}
