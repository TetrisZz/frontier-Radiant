using System.IO;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Dossiers;

/// <summary>Requests the staff entries for one of the requesting player's character slots.</summary>
public sealed class MsgDossierRequest : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public int Slot;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        => Slot = buffer.ReadVariableInt32();

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        => buffer.WriteVariableInt32(Slot);
}

/// <summary>Only staff-entered fields are sent. Personal fields already belong to the profile.</summary>
public sealed class MsgDossierResponse : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public int Slot;
    public DossierRecord Staff = new();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Slot = buffer.ReadVariableInt32();
        var length = buffer.ReadVariableInt32();
        using var stream = new MemoryStream(length);
        buffer.ReadAlignedMemory(stream, length);
        serializer.DeserializeDirect(stream, out Staff);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(Slot);
        using var stream = new MemoryStream();
        serializer.SerializeDirect(stream, Staff);
        buffer.WriteVariableInt32((int) stream.Length);
        stream.TryGetBuffer(out var segment);
        buffer.Write(segment);
    }
}
