using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._radiant.ResearchBlueprints;

internal static class ResearchBlueprintUi
{
    public static readonly Color Background = Color.FromHex("#191D20");
    public static readonly Color Border = Color.FromHex("#394348");
    public static readonly Color Accent = Color.FromHex("#79CDBB");
    public static readonly Color Success = Color.FromHex("#83D4A2");
    public static readonly Color Muted = Color.FromHex("#A4AFB5");
    public static readonly Color Warning = Color.FromHex("#E6B36B");

    public static Color Rarity(LocId rarity) => rarity.Id switch
    {
        "research-technology-blueprint-uncommon" => Color.FromHex("#33FF44"),
        "research-technology-blueprint-rare" => Color.FromHex("#33CCFF"),
        "research-technology-blueprint-epic" => Color.FromHex("#CC66FF"),
        "research-technology-blueprint-unique" => Color.FromHex("#FFCC66"),
        _ => Muted,
    };

    public static RichTextLabel Text(string text, Color? color = null)
    {
        var label = new RichTextLabel
        {
            HorizontalExpand = true, VerticalAlignment = Control.VAlignment.Top,
            MouseFilter = Control.MouseFilterMode.Ignore, ModulateSelfOverride = color ?? Color.White,
        };
        label.SetMessage(FormattedMessage.FromUnformatted(text));
        return label;
    }

    public static EntityPrototypeView Icon(EntProtoId prototype, int size)
    {
        var icon = new EntityPrototypeView
        {
            SetSize = new Vector2(size), Stretch = SpriteView.StretchMode.Fill,
            MouseFilter = Control.MouseFilterMode.Ignore,
        };
        icon.SetPrototype(prototype);
        return icon;
    }

    public static PanelContainer Heading(Control title, EntProtoId icon, Color accent)
    {
        var row = new BoxContainer { SeparationOverride = 8, Margin = new Thickness(2, 4, 2, 6) };
        row.AddChild(Icon(icon, 20));
        row.AddChild(title);
        return new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BorderColor = accent.WithAlpha(0.55f), BorderThickness = new Thickness(0, 0, 0, 1) },
            Children = { row },
        };
    }
}

internal sealed class ResearchBlueprintRowButton : ContainerButton
{
    private readonly Color _accent;

    public ResearchBlueprintRowButton(Color accent)
    {
        _accent = accent;
        UpdateStyle();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        UpdateStyle();
    }

    private void UpdateStyle()
    {
        var selected = Pressed || DrawMode == DrawModeEnum.Pressed;
        var background = selected ? Color.FromHex("#304044")
            : DrawMode == DrawModeEnum.Hover ? Color.FromHex("#2C3338") : Color.FromHex("#23282C");
        var style = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = selected ? _accent : ResearchBlueprintUi.Border,
            BorderThickness = new Thickness(3, 1, 1, 1),
        };
        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, 8);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, 6);
        StyleBoxOverride = style;
    }
}
