namespace Content.Server._Starlight.Emp;

// Radiant sector: Starlight EMP fist component.
[RegisterComponent]
public sealed partial class EmpOnMeleeHitComponent : Component
{
    [DataField] public float Range = 1f;
    [DataField] public float EnergyConsumption;
    [DataField] public TimeSpan DisableDuration = TimeSpan.FromSeconds(60);
    [DataField] public bool DisableOnHit = true;
}
