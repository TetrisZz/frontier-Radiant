using System.Buffers.Binary;
using Content.Shared._CorvaxGoob.Photo;

namespace Content.Shared._radiant.Media;

public static class NewsPhotoRules
{
    public static bool Valid(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 24 || bytes.Length > PhotoCameraTakeImageMessage.MaxImageBytes) return false;
        if (bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10)
            return Dimensions(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4)));

        // The existing camera falls back to JPEG for busy captures. Inspect the frame
        // dimensions before sending either format to the client's image decoder.
        if (bytes[0] != 0xff || bytes[1] != 0xd8) return false;
        var offset = 2;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 0xff) return false;
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) return false;
            var marker = bytes[offset++];
            if (marker is 0xd9 or 0xda or 0x00) return false;
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
            if (offset + 2 > bytes.Length) return false;
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            if (length < 2 || offset + length > bytes.Length) return false;
            if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc))
                return length >= 8 && Dimensions(
                    BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 5, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset + 3, 2)));
            offset += length;
        }
        return false;
    }

    private static bool Dimensions(uint width, uint height)
        => width is > 0 and <= 1024 && height is > 0 and <= 1024 && (ulong) width * height <= 512 * 512;
}
