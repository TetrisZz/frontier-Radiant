using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._radiant.Paperwork;

public sealed class PaperFormButton : Button
{
    public PaperFormButton(float lineHeight)
    {
        Text = Loc.GetString("paper-form-fill-button");
        Label.FontColorOverride = Color.FromHex("#264758");
        MinWidth = 44;
        MinHeight = Math.Max(22, lineHeight + 2);
        MaxHeight = MinHeight;
        Margin = new Thickness(2, 0);
        TextAlign = Label.AlignMode.Center;
        DrawModeChanged();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        ModulateSelfOverride = Color.White;
        StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(DrawMode switch
            {
                DrawModeEnum.Pressed => "#A7BEC9",
                DrawModeEnum.Hover => "#CCDDE5",
                _ => "#E2EBEF",
            }),
            BorderColor = Color.FromHex("#6C8795"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            ContentMarginLeftOverride = 5,
            ContentMarginRightOverride = 5,
            ContentMarginTopOverride = 0,
            ContentMarginBottomOverride = 1,
        };
    }
}
