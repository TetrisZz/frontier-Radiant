using System.IO;
using Content.Shared._CorvaxGoob.Photo;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._CorvaxGoob.Photo;

public static class PhotoImageEncoder
{
    public static byte[] Encode(Image<Rgba32> image)
    {
        using var opaque = image.CloneAs<Rgb24>(Configuration.Default);
        using var stream = new MemoryStream();
        opaque.SaveAsPng(stream);
        if (stream.Length <= PhotoCameraTakeImageMessage.MaxImageBytes)
            return stream.ToArray();

        // Encoder settings are not sandbox APIs. Default JPEG fits busy 250x250 captures;
        // the photo viewer detects the format from the image bytes.
        stream.SetLength(0);
        stream.Position = 0;
        opaque.SaveAsJpeg(stream);
        return stream.ToArray();
    }
}
