using System.IO;
using System.Linq;
using System.Numerics;
using Content.Shared._radiant.Media;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Media;

internal static class CivicUi
{
    public static BoxContainer Column() => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, HorizontalExpand = true };
    public static RichTextLabel Text(string text, bool paper = false)
    {
        var label = new RichTextLabel { HorizontalExpand = true, Margin = new Thickness(2) };
        var message = new FormattedMessage();
        if (paper) message.PushColor(Color.FromHex("#273034"));
        message.AddText(text);
        label.SetMessage(message);
        return label;
    }
    public static PanelContainer Card(string heading, Control content, bool paper = false)
    {
        var panel = new PanelContainer
        {
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex(paper ? "#ECECDE" : "#1B2C33"),
                BorderColor = Color.FromHex(paper ? "#B0AD96" : "#3B6570"),
                BorderThickness = new Thickness(1),
            },
        };
        var column = Column(); column.Margin = new Thickness(12);
        if (heading != "")
        {
            var label = new RichTextLabel { HorizontalExpand = true };
            var color = paper ? "#273034" : "#87D8CF";
            label.SetMessage(FormattedMessage.FromMarkupOrThrow($"[color={color}][bold]{FormattedMessage.EscapeText(heading)}[/bold][/color]"));
            column.AddChild(label);
        }
        column.AddChild(content); panel.AddChild(column); return panel;
    }
    public static void Clear(Control parent)
    {
        foreach (var child in parent.Children.ToArray()) child.Dispose();
    }
    public static Control Article(NewsEditionArticle article, bool paper)
    {
        var column = Column();
        column.AddChild(Text($"{article.Author} | {article.Time} | {article.Channel}", paper));
        if (NewsPhotoRules.Valid(article.Photo)) column.AddChild(new NewsPhotoView(article.Photo!));
        column.AddChild(Text(article.Content, paper));
        return Card(article.Title, column, paper);
    }
}

internal sealed class NewsPhotoView : TextureRect
{
    private OwnedTexture? _owned;
    public NewsPhotoView(byte[] image)
    {
        MinSize = new Vector2(160, 160);
        MaxHeight = 280;
        HorizontalExpand = true;
        Stretch = StretchMode.KeepAspectCentered;
        try
        {
            using var stream = new MemoryStream(image);
            var texture = Texture.LoadFromPNGStream(stream);
            _owned = texture as OwnedTexture;
            Texture = texture;
        }
        catch { Visible = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Texture = null; _owned?.Dispose(); _owned = null; }
        base.Dispose(disposing);
    }
}
