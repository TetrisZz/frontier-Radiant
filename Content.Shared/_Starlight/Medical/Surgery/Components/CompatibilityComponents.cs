namespace Content.Shared._Starlight.Medical.Surgery.Components;

/// <summary>
/// Compatibility markers for Starlight surgery prototypes whose owning systems
/// are not present in Frontier. Keeping them local lets the prototypes load
/// without importing unrelated species and antag systems.
/// </summary>
[RegisterComponent]
public sealed partial class OrganShadekinCoreComponent : Component;

[RegisterComponent]
public sealed partial class UncryoableComponent : Component;

[RegisterComponent]
public sealed partial class HasEggHolderComponent : Component;

[RegisterComponent]
public sealed partial class EggHolderComponent : Component;
