using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._radiant.Medical;

/// <summary>Shared spacing and readable information cards for laboratory equipment.</summary>
internal static class MedicalLabUi
{
    public static BoxContainer Column(int gap = 10) => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = gap, HorizontalExpand = true,
    };

    public static PanelContainer Card(string title, params Control[] content)
    {
        var column = Column(8);
        column.AddChild(new Label { Text = Loc.GetString(title), FontColorOverride = Color.FromHex("#8CD8CA") });
        foreach (var child in content) column.AddChild(child);
        var style = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#1B262D"), BorderColor = Color.FromHex("#344850"),
            BorderThickness = new Thickness(1),
        };
        style.SetContentMarginOverride(StyleBox.Margin.All, 12);
        return new PanelContainer { PanelOverride = style, HorizontalExpand = true, Children = { column } };
    }

    public static ScrollContainer Scroll(Control child) => new()
    {
        HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true, Children = { child },
    };

    public static Button Button(string key) => new LabButton { Text = Loc.GetString(key), HorizontalExpand = true, MinHeight = 32 };

    public static void FitSelector(Control control)
    {
        foreach (var child in control.Children)
        {
            if (child is Label label) label.ClipText = true;
            FitSelector(child);
        }
    }
}

internal sealed class LabButton : Button
{
    private bool _clickDown;

    public LabButton()
    {
        OnButtonDown += _ => { _clickDown = true; DrawModeChanged(); };
        OnButtonUp += _ => { _clickDown = false; DrawModeChanged(); };
        DrawModeChanged();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        // The palette is final: the global button theme must not multiply it by another dark tint.
        ModulateSelfOverride = Color.White;
        if (Label != null)
            Label.FontColorOverride = Color.FromHex(Disabled ? "#8C969F" : "#FFFFFF");
        var style = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(Disabled ? "#24282E" : _clickDown || DrawMode == DrawModeEnum.Pressed ? "#1B3544"
                : DrawMode == DrawModeEnum.Hover ? "#6595A5" : "#4B798B"),
            BorderColor = Color.FromHex(Disabled ? "#454D55" : "#9ED6E3"), BorderThickness = new Thickness(1),
        };
        style.SetContentMarginOverride(StyleBox.Margin.Horizontal, 10);
        style.SetContentMarginOverride(StyleBox.Margin.Vertical, 6);
        StyleBoxOverride = style;
    }

    protected override void ControlFocusExited()
    {
        _clickDown = false;
        base.ControlFocusExited();
        DrawModeChanged();
    }
}
