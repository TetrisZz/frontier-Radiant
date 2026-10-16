using System.Numerics;
using Content.Client._radiant.Humanoid;
using Content.Shared._radiant.Humanoid;
using Content.Shared.Humanoid;
using NUnit.Framework;
using Robust.Shared.Maths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Tests.Client;

[TestFixture]
public sealed class HairGradientTest
{
    [Test]
    public void BoundsExcludeTransparentPaddingAndReachPixelCenters()
    {
        using var image = new Image<Rgba32>(32, 32);
        image[10, 2] = new Rgba32(255, 255, 255, 255);
        image[20, 15] = new Rgba32(255, 255, 255, 255);
        var bounds = HairGradientSystem.CalculateBounds(image, new Vector2i(32, 32), new[] { Vector2i.Zero });
        Assert.That(bounds, Is.EqualTo(new Vector4(10.5f / 32, 2.5f / 32, 20.5f / 32, 15.5f / 32)));
    }

    [Test]
    public void BoundsIncludeAllDirectionsAndAnimationFrames()
    {
        using var image = new Image<Rgba32>(64, 32);
        image[10, 5] = new Rgba32(255, 255, 255, 255);
        image[50, 20] = new Rgba32(255, 255, 255, 255);
        var bounds = HairGradientSystem.CalculateBounds(image, new Vector2i(32, 32),
            new[] { Vector2i.Zero, new Vector2i(32, 0), Vector2i.Zero });
        Assert.That(bounds, Is.EqualTo(new Vector4(10.5f / 32, 5.5f / 32, 18.5f / 32, 20.5f / 32)));
    }

    [Test]
    public void EmptyHairHasFiniteFallbackBounds()
    {
        using var image = new Image<Rgba32>(32, 32);
        Assert.That(HairGradientSystem.CalculateBounds(image, new Vector2i(32, 32), new[] { Vector2i.Zero }),
            Is.EqualTo(new Vector4(0, 0, 1, 1)));
    }

    [Test]
    public void AppearanceWithersAndComponentCopiesPreserveBothGradients()
    {
        var appearance = new HumanoidCharacterAppearance
        {
            HairColoringMode = HairColoringMode.Gradient,
            HairGradientColor = Color.Cyan,
            HairGradientDirection = HairGradientDirection.BottomToTop,
            FacialHairColoringMode = HairColoringMode.Gradient,
            FacialHairGradientColor = Color.Red,
            FacialHairGradientDirection = HairGradientDirection.RightToLeft,
        };
        var changed = appearance.WithHairColor(Color.White).WithSkinColor(Color.Green);
        var source = new HumanoidAppearanceComponent();
        var target = new HumanoidAppearanceComponent();
        HairColoringHelper.CopyGradientSettingsToComponent(changed, source);
        HairColoringHelper.CopyGradientSettingsToComponent(source, target);
        Assert.Multiple(() =>
        {
            Assert.That(target.HairColoringMode, Is.EqualTo(appearance.HairColoringMode));
            Assert.That(target.HairGradientColor, Is.EqualTo(appearance.HairGradientColor));
            Assert.That(target.HairGradientDirection, Is.EqualTo(appearance.HairGradientDirection));
            Assert.That(target.FacialHairColoringMode, Is.EqualTo(appearance.FacialHairColoringMode));
            Assert.That(target.FacialHairGradientColor, Is.EqualTo(appearance.FacialHairGradientColor));
            Assert.That(target.FacialHairGradientDirection, Is.EqualTo(appearance.FacialHairGradientDirection));
        });
    }
}
