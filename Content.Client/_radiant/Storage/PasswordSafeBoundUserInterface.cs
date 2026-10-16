using Content.Shared._radiant.Storage;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._radiant.Storage;

public sealed class PasswordSafeBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private PasswordSafeWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<PasswordSafeWindow>();
        _window.Submit += message => SendMessage(message);
        _window.Lock += () => SendMessage(new PasswordSafeCloseMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is PasswordSafeUiState safe)
            _window?.Update(safe);
    }
}

public sealed class PasswordSafeWindow : FancyWindow
{
    private readonly Label _status = new();
    private readonly Label _confirmationLabel = new() { Text = Loc.GetString("radiant-safe-confirm-code") };
    private readonly LineEdit _code = new();
    private readonly LineEdit _confirmation = new();
    private readonly GridContainer _keypad = new() { Columns = 3 };
    private readonly Button _submit = new() { HorizontalExpand = true };
    private readonly Button _lock = new() { Text = Loc.GetString("radiant-safe-lock") };
    private LineEdit _active;
    private bool _configure;
    private bool _open;
    private bool _broken;
    private bool _initialized;

    public event Action<PasswordSafeSubmitMessage>? Submit;
    public event Action? Lock;

    public PasswordSafeWindow()
    {
        Title = Loc.GetString("radiant-safe-title");
        MinWidth = 280;
        _active = _code;
        var contents = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 8,
            Margin = new Thickness(8),
        };
        ContentsContainer.AddChild(contents);
        contents.AddChild(_status);
        contents.AddChild(_code);
        contents.AddChild(_confirmationLabel);
        contents.AddChild(_confirmation);
        contents.AddChild(_keypad);
        contents.AddChild(_submit);
        contents.AddChild(_lock);
        foreach (var edit in new[] { _code, _confirmation })
        {
            edit.IsValid = text => text.Length <= PasswordSafeCode.MaxLength && PasswordSafeCode.IsNumeric(text);
            edit.OnFocusEnter += _ => _active = edit;
            edit.OnTextChanged += _ => RefreshSubmit();
            edit.OnTextEntered += _ => SendCode();
        }
        for (var digit = 1; digit <= 9; digit++)
        {
            var text = digit.ToString();
            AddKey(text, () => _active.SetText(_active.Text + text, true));
        }
        AddKey("C", () => _active.SetText("", true), Loc.GetString("radiant-safe-clear"));
        AddKey("0", () => _active.SetText(_active.Text + "0", true));
        AddKey("<", () =>
        {
            if (_active.Text.Length > 0)
                _active.SetText(_active.Text[..^1], true);
        }, Loc.GetString("radiant-safe-delete"));
        _submit.OnPressed += _ => SendCode();
        _lock.OnPressed += _ => Lock?.Invoke();
    }

    private void AddKey(string text, Action pressed, string? tooltip = null)
    {
        var button = new Button { Text = text, MinWidth = 80, MinHeight = 36, ToolTip = tooltip };
        button.OnPressed += _ => pressed();
        _keypad.AddChild(button);
    }

    public void Update(PasswordSafeUiState state)
    {
        var changed = !_initialized || _configure != !state.Configured ||
            _open != state.Open || _broken != state.Broken;
        _initialized = true;
        _configure = !state.Configured;
        _open = state.Open;
        _broken = state.Broken;
        _status.Text = Loc.GetString(state.Broken ? "radiant-safe-broken" :
            state.Open ? "radiant-safe-open" : _configure ? "radiant-safe-create-code" : "radiant-safe-enter-code");
        _code.Visible = _keypad.Visible = _submit.Visible = !state.Open && !state.Broken;
        _confirmation.Visible = _confirmationLabel.Visible = _configure && !state.Open && !state.Broken;
        _lock.Visible = state.Open && !state.Broken;
        _submit.Text = Loc.GetString(_configure ? "radiant-safe-set-code" : "radiant-safe-unlock");
        if (changed)
        {
            _code.Text = _confirmation.Text = "";
            _active = _code;
        }
        RefreshSubmit();
    }

    private void RefreshSubmit()
        => _submit.Disabled = _open || !PasswordSafeCode.IsValid(_code.Text) ||
            _configure && _code.Text != _confirmation.Text;

    private void SendCode()
    {
        RefreshSubmit();
        if (_submit.Disabled)
            return;
        Submit?.Invoke(new PasswordSafeSubmitMessage(_code.Text, _confirmation.Text, _configure));
        _code.Text = _confirmation.Text = "";
        _active = _code;
        RefreshSubmit();
    }
}
