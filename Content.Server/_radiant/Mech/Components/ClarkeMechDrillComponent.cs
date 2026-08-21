namespace Content.Server._radiant.Mech.Components;

/// <summary>
/// Marks equipment that belongs to the Clarke's dedicated mining loadout.
/// </summary>
[RegisterComponent]
public sealed partial class ClarkeMechEquipmentComponent : Component;

/// <summary>
/// Marks a Clarke drill that can make a direct, mech-originating attack with right-click.
/// </summary>
[RegisterComponent]
public sealed partial class ClarkeMechDrillComponent : Component;
