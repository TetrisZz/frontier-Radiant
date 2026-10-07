using System.Numerics;
using Content.Client.Resources;
using Content.Shared._radiant.Passports;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.Client._radiant.Passports;

/// <summary>A departmental ink stamp, independent of the passport citizenship seals.</summary>
public sealed class ServiceCredentialSeal(CredentialService service) : Control
{
    private Font? _font;

    protected override void Draw(IRenderHandle handle)
    {
        var screen = handle.DrawingHandleScreen;
        var center = PixelSize / 2;
        var scale = MathF.Min(PixelSize.X, PixelSize.Y) / 128f;
        var ink = Color.FromHex("#A32838").WithAlpha(0.72f);
        var font = _font ??= IoCManager.Resolve<IResourceCache>().GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 7);
        Vector2 Point(float x, float y) => center + new Vector2(x, y) * scale;
        void Line(float x1, float y1, float x2, float y2)
            => screen.DrawLine(Point(x1, y1), Point(x2, y2), ink);

        // Uneven double outer rim and inner rim mimic an actual impression on paper.
        foreach (var radius in new[] { 60f, 58f, 45f, 43f })
        {
            for (var i = 0; i < 160; i++)
            {
                if (i % 53 is 6 or 7)
                    continue;
                var a = i * MathF.Tau / 160;
                var b = (i + 1) * MathF.Tau / 160;
                Line(MathF.Cos(a) * radius, MathF.Sin(a) * radius,
                    MathF.Cos(b) * radius, MathF.Sin(b) * radius);
            }
        }

        var ring = Loc.GetString("service-credential-seal-ring");
        var original = screen.GetTransform();
        try
        {
            for (var i = 0; i < ring.Length; i++)
            {
                var angle = -MathF.PI / 2 + (i - (ring.Length - 1) / 2f) * MathF.Tau / (ring.Length + 5);
                var position = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 51 * scale;
                screen.SetTransform(Matrix3x2.CreateRotation(angle + MathF.PI / 2)
                    * Matrix3x2.CreateTranslation(position) * original);
                screen.DrawString(font, new Vector2(-2 * scale, -4 * scale), ring[i].ToString(), scale, ink);
            }
        }
        finally
        {
            screen.SetTransform(original);
        }

        // Distinct departmental emblems: shield for internal security, shuttle for the space fleet.
        if (service == CredentialService.Dvb)
        {
            Line(-18, -25, 18, -25); Line(-18, -25, -16, -3);
            Line(18, -25, 16, -3); Line(-16, -3, 0, 9); Line(16, -3, 0, 9);
            for (var i = 0; i < 5; i++)
            {
                var a = -MathF.PI / 2 + i * MathF.Tau / 5;
                var b = -MathF.PI / 2 + ((i + 2) % 5) * MathF.Tau / 5;
                Line(MathF.Cos(a) * 10, MathF.Sin(a) * 10 - 10,
                    MathF.Cos(b) * 10, MathF.Sin(b) * 10 - 10);
            }
        }
        else
        {
            Line(0, -37, 0, -29); Line(-4, -33, 4, -33);
            Line(0, -24, -5, -9); Line(-5, -9, -20, 4);
            Line(-20, 4, -20, 8); Line(-20, 8, -5, 2);
            Line(-5, 2, -5, 9); Line(-5, 9, 5, 9);
            Line(5, 9, 5, 2); Line(5, 2, 20, 8);
            Line(20, 8, 20, 4); Line(20, 4, 5, -9); Line(5, -9, 0, -24);
            Line(-4, 11, -4, 15); Line(4, 11, 4, 15);
        }
        var department = Loc.GetString(service == CredentialService.Dvb
            ? "service-credential-seal-dvb" : "service-credential-seal-fleet");
        var dimensions = screen.GetDimensions(font, department, scale * 1.8f);
        screen.DrawString(font, Point(0, 17) - new Vector2(dimensions.X / 2, 0), department, scale * 1.8f, ink);
    }
}
