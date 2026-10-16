using Robust.Shared.Network;

namespace Content.Server._Triad.Shipyard;

[RegisterComponent, UnsavedComponent]
public sealed partial class DeployedShipSaveComponent : Component
{
    public string Hash = string.Empty;
    public string SourceFilePath = string.Empty;
    public NetUserId Loader;
}
