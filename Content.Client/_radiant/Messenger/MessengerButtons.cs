using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._radiant.Messenger;

public sealed class MessengerButton : Button
{
    private bool _clickDown;

    public MessengerButton()
    {
        RectClipContent = true;
        Label.FontColorOverride = Color.FromHex("#F0F5F5");
        OnButtonDown += _ => { _clickDown = true; DrawModeChanged(); };
        OnButtonUp += _ => { _clickDown = false; DrawModeChanged(); };
        DrawModeChanged();
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        // The background already contains its final color; do not tint it again through the PDA theme.
        ModulateSelfOverride = Color.White;
        StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(Disabled ? "#2A3338" :
                _clickDown ? "#253740" :
                Pressed ? "#35695E" :
                IsHovered ? "#506977" : "#3D505D"),
            BorderColor = Color.FromHex(Pressed ? "#83D9BA" : "#607582"),
            BorderThickness = new Thickness(1),
            ContentMarginLeftOverride = 6,
            ContentMarginRightOverride = 6,
            ContentMarginTopOverride = 4,
            ContentMarginBottomOverride = 4,
        };
    }

    protected override void ControlFocusExited()
    {
        _clickDown = false;
        base.ControlFocusExited();
        DrawModeChanged();
    }
}

public sealed class MessengerContactButton : ContainerButton
{
    public MessengerContactButton()
    {
        RectClipContent = true;
        DrawModeChanged();
    }
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; DrawModeChanged(); }
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        ModulateSelfOverride = Color.White;
        StyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex(DrawMode switch
            {
                DrawModeEnum.Pressed => "#263E38",
                DrawModeEnum.Hover => "#455D56",
                _ => Selected ? "#345F52" : "#35454A",
            }),
        };
    }
}

public sealed class MessengerSendButton : TextureButton
{
    public MessengerSendButton()
    {
        ModulateSelfOverride = Color.FromHex("#89D7BA");
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        ModulateSelfOverride = Color.FromHex(DrawMode switch
        {
            DrawModeEnum.Pressed => "#467964",
            DrawModeEnum.Hover => "#A5E6CE",
            DrawModeEnum.Disabled => "#535F5D",
            _ => "#89D7BA",
        });
    }
}
