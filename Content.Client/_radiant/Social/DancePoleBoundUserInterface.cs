using Content.Client.UserInterface.Controls;
using Content.Shared._radiant.Social;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._radiant.Social;

public sealed class DancePoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DancePoleWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<DancePoleWindow>();
        _window.Command += message => SendMessage(message);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is DancePoleUiState data)
            _window?.UpdateState(data, data.Dancer != null && data.Dancer == EntMan.GetNetEntity(PlayerManager.LocalEntity));
    }
}

public sealed class DancePoleWindow : FancyWindow
{
    public event Action<DancePoleMessage>? Command;
    private readonly Label _status = new() { ClipText = true };
    private readonly Button[] _moves = new Button[3];
    private readonly Button _stop = new() { Text = Loc.GetString("dance-pole-stop") };
    private readonly CheckBox _light = new() { Text = Loc.GetString("dance-pole-light") };
    private readonly OptionButton _finish = new() { HorizontalExpand = true };
    private readonly LightSwatch[] _colors = new LightSwatch[DancePolePalette.Lights.Length];

    public DancePoleWindow()
    {
        Title = Loc.GetString("dance-pole-title");
        SetSize = new(370, 340);
        MinWidth = 340;
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, Margin = new Thickness(10) };
        ContentsContainer.AddChild(column);
        column.AddChild(_status);
        var moves = new BoxContainer { SeparationOverride = 4 };
        column.AddChild(moves);
        for (var i = 0; i < _moves.Length; i++)
        {
            var index = i;
            var button = new Button { Text = Loc.GetString("dance-pole-move-" + i), HorizontalExpand = true, SetHeight = 34 };
            button.OnPressed += _ => Command?.Invoke(new DancePoleMessage(DancePoleAction.Dance, index));
            moves.AddChild(button);
            _moves[i] = button;
        }
        _stop.OnPressed += _ => Command?.Invoke(new DancePoleMessage(DancePoleAction.Stop));
        column.AddChild(_stop);
        column.AddChild(new Label { Text = Loc.GetString("dance-pole-finish") });
        for (var i = 0; i < DancePolePalette.Finishes.Length; i++)
            _finish.AddItem(Loc.GetString("dance-pole-finish-" + i), i);
        _finish.OnItemSelected += args => Command?.Invoke(new DancePoleMessage(DancePoleAction.Finish, args.Id));
        column.AddChild(_finish);
        column.AddChild(_light);
        _light.OnPressed += _ => Command?.Invoke(new DancePoleMessage(DancePoleAction.LightToggle));
        var colors = new BoxContainer { SeparationOverride = 8 };
        column.AddChild(colors);
        for (var i = 0; i < _colors.Length; i++)
        {
            var index = i;
            var swatch = new LightSwatch(DancePolePalette.Lights[i])
            {
                SetSize = new(36, 30),
                ToolTip = Loc.GetString("dance-pole-color-" + i),
            };
            swatch.OnPressed += _ => Command?.Invoke(new DancePoleMessage(DancePoleAction.LightColor, index));
            _colors[i] = swatch;
            colors.AddChild(swatch);
        }
    }

    public void UpdateState(DancePoleUiState state, bool ownDance)
    {
        _status.Text = Loc.GetString(state.Dancer == null ? "dance-pole-free" : ownDance ? "dance-pole-dancing" : "dance-pole-occupied");
        _stop.Disabled = !ownDance;
        for (var i = 0; i < _moves.Length; i++)
        {
            _moves[i].Disabled = state.Dancer != null && !ownDance;
            _moves[i].Pressed = ownDance && (int) state.Move == i;
        }
        _finish.SelectId(state.Finish);
        _light.Pressed = state.Light;
        for (var i = 0; i < _colors.Length; i++)
            _colors[i].Selected = i == state.LightColor;
    }

    private sealed class LightSwatch(Color color) : Button
    {
        private bool _selected;
        public bool Selected
        {
            set { _selected = value; DrawModeChanged(); }
        }

        protected override void DrawModeChanged()
        {
            base.DrawModeChanged();
            ModulateSelfOverride = Color.White;
            StyleBoxOverride = new Robust.Client.Graphics.StyleBoxFlat
            {
                BackgroundColor = DrawMode == DrawModeEnum.Pressed
                    ? new Color(color.R * 0.7f, color.G * 0.7f, color.B * 0.7f, color.A) : color,
                BorderColor = _selected ? Color.White : Color.FromHex("#30363B"),
                BorderThickness = new Thickness(2),
            };
        }
    }
}
