using Robust.Shared.Serialization;

namespace Content.Shared._Triad.Shipyard.Save;

[Serializable, NetSerializable]
public sealed class MigrateShipFileMessage(string targetPath, string contents) : EntityEventArgs
{
    public string TargetPath { get; } = targetPath;
    public string Contents { get; } = contents;
}
