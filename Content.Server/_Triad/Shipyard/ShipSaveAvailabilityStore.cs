using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Server._Triad.Shipyard;

/// <summary>Deployment leases expire on a process restart; revoked snapshots never become available again.</summary>
internal sealed class ShipSaveAvailabilityStore(IWritableDirProvider files, Guid startupId)
{
    private static ResPath PathFor(string hash) => new($"/Shipyard/available/{hash}.bin");

    public bool IsKnown(string hash) => files.Exists(PathFor(hash));

    public bool IsAvailable(string hash)
    {
        var path = PathFor(hash);
        if (!files.Exists(path))
            return false;
        using var stream = files.OpenRead(path);
        var state = stream.ReadByte();
        if (state == 1) // Existing available files remain compatible.
            return true;
        if (state != 2)
            return false;
        var bytes = new byte[16];
        return stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length
            && new Guid(bytes) != startupId;
    }

    public void MakeAvailable(string hash) => Write(hash, false);
    public void Deploy(string hash) => Write(hash, true);
    public void Revoke(string hash)
    {
        var path = PathFor(hash);
        files.CreateDir(path.Directory);
        using var stream = files.OpenWrite(path);
        stream.WriteByte(0);
    }

    private void Write(string hash, bool deployed)
    {
        var path = PathFor(hash);
        files.CreateDir(path.Directory);
        using var stream = files.OpenWrite(path);
        stream.WriteByte(deployed ? (byte) 2 : (byte) 1);
        if (deployed)
            stream.Write(startupId.ToByteArray());
    }
}
