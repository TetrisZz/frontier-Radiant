using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Lathe;

/// <summary>
/// Makes a portable cyber printer eject products onto the map instead of parenting
/// them to the character that currently contains the printer.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class CyberPrinterComponent : Component;
