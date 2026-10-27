using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Paper.UI;

/// <summary>A document illustration whose height follows the available paper width.</summary>
public sealed class PaperIllustration : TextureRect
{
    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        if (Texture == null)
            return Vector2.Zero;

        var width = float.IsFinite(availableSize.X) ? availableSize.X : Texture.Width;
        return new Vector2(width, width * Texture.Height / Texture.Width);
    }
}
