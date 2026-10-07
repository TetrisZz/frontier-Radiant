using System.IO;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Preferences
{
    /// <summary>
    /// The client sends this to update a character profile.
    /// </summary>
    public sealed class MsgUpdateCharacter : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.Command;

        public int Slot;
        public bool ReplaceCharacter;
        public ICharacterProfile Profile = default!;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            Slot = buffer.ReadInt32();
            var length = buffer.ReadVariableInt32();
            using var stream = new MemoryStream(length);
            buffer.ReadAlignedMemory(stream, length);
            Profile = serializer.Deserialize<ICharacterProfile>(stream);
            // Older clients sent only the slot and profile. Keep that layout intact so
            // their character edits are still accepted while the client is updating.
            ReplaceCharacter = buffer.Position < buffer.LengthBits && buffer.ReadBoolean();
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.Write(Slot);
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, Profile);
                buffer.WriteVariableInt32((int) stream.Length);
                stream.TryGetBuffer(out var segment);
                buffer.Write(segment);
            }
            // Appended rather than inserted before the profile: older servers can still
            // deserialize the character even though they ignore replacement semantics.
            buffer.Write(ReplaceCharacter);
        }
    }
}
