using Content.Client._CorvaxGoob.Photo;
using Content.Shared._CorvaxGoob.Photo;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests.Client;

[TestFixture]
public sealed class PhotoImageEncoderTest
{
    [TestCase(false, 731)]
    [TestCase(true, 731)]
    [TestCase(true, 1)]
    [TestCase(true, 97)]
    [TestCase(true, 2147483647)]
    public void QuietAndBusyFramesFitTheNetworkLimitWithoutChangingDimensions(bool noisy, int seed)
    {
        using var image = new Image<Rgba32>(250, 250);
        var random = new System.Random(seed);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            image[x, y] = noisy
                ? new Rgba32((byte) random.Next(256), (byte) random.Next(256), (byte) random.Next(256))
                : new Rgba32(60, 65, 124);

        var originalFirst = image[0, 0];
        var originalLast = image[249, 249];
        var data = PhotoImageEncoder.Encode(image);
        Assert.That(data.Length, Is.LessThanOrEqualTo(PhotoCameraTakeImageMessage.MaxImageBytes));
        using var decoded = Image.Load<Rgba32>(data);
        Assert.That(decoded.Width, Is.EqualTo(250));
        Assert.That(decoded.Height, Is.EqualTo(250));
        Assert.That(image[0, 0], Is.EqualTo(originalFirst), "Encoding must not dispose or alter the source frame");
        Assert.That(image[249, 249], Is.EqualTo(originalLast));
        Assert.That(data[0], Is.EqualTo(noisy ? 0xff : 0x89), "Busy frames use JPEG; simple frames retain lossless PNG");
        if (!noisy)
            Assert.That(decoded[0, 0], Is.EqualTo(image[0, 0]), "Simple frames should remain lossless");
    }
}
