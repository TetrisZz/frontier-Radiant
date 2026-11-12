using Robust.Shared.GameStates;
namespace Content.Shared._Triad.Shipyard.Save;

/// <summary>
/// On a player, blocks saving and loading through the shipyard console.
/// On a grid or an entity aboard it, blocks saving that ship, including cargo inside containers.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ShipSavingBlacklistComponent : Component;
