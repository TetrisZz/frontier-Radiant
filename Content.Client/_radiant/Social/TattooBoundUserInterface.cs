using Content.Client.UserInterface.Controls;
using Content.Shared._radiant.Social;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Social;

public sealed class TattooEditorBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private TattooEditorWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<TattooEditorWindow>();
        _window.Propose += message => SendMessage(message);
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is TattooUiState tattoo)
            _window?.Update(tattoo);
    }
}

public sealed class TattooConsentBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private TattooConsentWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<TattooConsentWindow>();
        _window.Consent += accepted => SendMessage(new TattooConsentMessage(accepted));
        Update();
    }
    public override void Update()
    {
        base.Update();
        // The queued opening state can predate the request already stored on the UI component.
        if (UiSystem.TryGetUiState<TattooUiState>(Owner, UiKey, out var tattoo))
            _window?.Update(tattoo);
    }
}

public sealed class TattooEditorWindow : FancyWindow
{
    public event Action<TattooProposeMessage>? Propose;
    private readonly Label _status = new() { ClipText = true };
    private readonly LineEdit _text = new() { HorizontalExpand = true, PlaceHolder = Loc.GetString("tattoo-text-placeholder") };
    private readonly OptionButton _zone = new() { HorizontalExpand = true };
    private int _color;
    private TattooUiState? _state;
    private readonly Button _propose = new() { Text = Loc.GetString("tattoo-propose") };

    public TattooEditorWindow()
    {
        Title = Loc.GetString("tattoo-title");
        MinWidth = 420;
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(10) };
        ContentsContainer.AddChild(column);
        column.AddChild(_status);
        column.AddChild(_zone);
        var colors = new BoxContainer { SeparationOverride = 8 };
        var group = new ButtonGroup();
        for (var i = 0; i < TextTattooRules.Colors.Length; i++)
        {
            var index = i;
            var button = new Button
            {
                ToggleMode = true,
                Group = group,
                SetSize = new System.Numerics.Vector2(32, 32),
                ToolTip = Loc.GetString("tattoo-color-" + i),
            };
            void UpdateSwatch() => button.StyleBoxOverride = new StyleBoxFlat
            {
                BackgroundColor = TextTattooRules.Colors[index],
                BorderColor = button.Pressed ? Color.White : Color.FromHex("#25292D"),
                BorderThickness = new Thickness(2),
            };
            button.OnToggled += e => { if (e.Pressed) _color = index; };
            button.OnToggled += _ =>
            {
                foreach (var sibling in colors.Children)
                {
                    sibling.ModulateSelfOverride = sibling == button ? Color.White : Color.FromHex("#999999");
                    if (sibling is Button { StyleBoxOverride: StyleBoxFlat box })
                        box.BorderColor = sibling == button ? Color.White : Color.FromHex("#25292D");
                }
                UpdateSwatch();
            };
            button.Pressed = i == 0;
            UpdateSwatch();
            colors.AddChild(button);
        }
        column.AddChild(colors);
        column.AddChild(_text);
        column.AddChild(_propose);
        _text.IsValid = s => s.Length <= TextTattooRules.MaximumLength;
        for (var i = 0; i < 6; i++)
        {
            _zone.AddItem(Loc.GetString("tattoo-zone-" + i), i);
        }
        _zone.OnItemSelected += e => _zone.SelectId(e.Id);
        _text.OnTextChanged += _ => RefreshActions();
        _propose.OnPressed += _ => Propose?.Invoke(new TattooProposeMessage(_text.Text, _zone.SelectedId, _color));
        RefreshActions();
    }
    public void Update(TattooUiState state)
    {
        _state = state;
        _status.Text = Loc.GetString(state.Accepted ? "tattoo-status-applying" :
            state.Tattoo != null ? "tattoo-status-waiting" : "tattoo-status",
            ("patient", state.Patient), ("ink", state.Ink));
        RefreshActions();
    }
    private void RefreshActions() => _propose.Disabled = _state is not { Ink: > 0, Tattoo: null } ||
        string.IsNullOrWhiteSpace(_text.Text);
}

public sealed class TattooConsentWindow : FancyWindow
{
    public event Action<bool>? Consent;
    private readonly RichTextLabel _request = new() { MaxWidth = 420 };
    private readonly Button _accept = new() { Text = Loc.GetString("tattoo-accept"), Disabled = true };
    private readonly Button _reject = new() { Text = Loc.GetString("tattoo-reject") };
    public TattooConsentWindow()
    {
        Title = Loc.GetString("tattoo-consent-title");
        MinWidth = 360;
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(10) };
        ContentsContainer.AddChild(column);
        column.AddChild(_request);
        column.AddChild(_accept);
        column.AddChild(_reject);
        _accept.OnPressed += _ => Consent?.Invoke(true);
        _reject.OnPressed += _ => Consent?.Invoke(false);
        OnClose += () => Consent?.Invoke(false);
    }
    public void Update(TattooUiState state)
    {
        _request.SetMessage(FormattedMessage.FromUnformatted(state.Tattoo is { } tattoo
            ? Loc.GetString("tattoo-consent-request", ("zone", Loc.GetString("tattoo-zone-" + tattoo.Zone)),
                ("color", Loc.GetString("tattoo-color-" + tattoo.Color)), ("text", tattoo.Text)) : ""));
        _accept.Disabled = state.Accepted || state.Tattoo == null;
    }
}
