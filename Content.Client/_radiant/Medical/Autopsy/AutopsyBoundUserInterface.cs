using System.Numerics;
using Content.Client._radiant.Media;
using Content.Client.UserInterface.Controls;
using Content.Shared._radiant.Medical.Autopsy;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._radiant.Medical.Autopsy;

public sealed class AutopsyBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private AutopsyWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = new AutopsyWindow(); _window.OnClose += Close;
        _window.Action += action => SendMessage(new AutopsyActionMessage(action));
        Update<AutopsyUiState>(); _window.OpenCentered();
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    { base.UpdateState(state); if (state is AutopsyUiState data) _window?.UpdateState(data); }
    protected override void Dispose(bool disposing)
    { base.Dispose(disposing); if (disposing) { _window?.Dispose(); _window = null; } }
}

public sealed class AutopsyWindow : FancyWindow
{
    public event Action<AutopsyAction>? Action;
    private readonly RichTextLabel _report = new() { HorizontalExpand = true };
    private readonly Label _status = new();
    private readonly Button _deep;
    private readonly Button _print;
    private readonly Button _record;
    private string _text = "";
    public AutopsyWindow()
    {
        Title = Loc.GetString("autopsy-ui-title"); MinSize = new Vector2(540, 480); SetSize = new Vector2(620, 680);
        var column = CivicUi.Column(); column.Margin = new Thickness(10);
        column.AddChild(CivicUi.Card(Loc.GetString("autopsy-ui-procedure"), CivicUi.Text(Loc.GetString("autopsy-ui-help"))));
        column.AddChild(_status);
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
        scroll.AddChild(CivicUi.Card(Loc.GetString("autopsy-ui-results"), _report)); column.AddChild(scroll);
        var buttons = new BoxContainer { HorizontalExpand = true, SeparationOverride = 6 };
        _deep = Add(AutopsyAction.Detailed, "autopsy-ui-detailed");
        _print = Add(AutopsyAction.Print, "autopsy-ui-print");
        _record = Add(AutopsyAction.Record, "autopsy-ui-record");
        column.AddChild(buttons); ContentsContainer.AddChild(column);
        Button Add(AutopsyAction action, string label)
        {
            var button = new Button { Text = Loc.GetString(label), HorizontalExpand = true };
            button.OnPressed += _ => Action?.Invoke(action); buttons.AddChild(button); return button;
        }
    }
    public void UpdateState(AutopsyUiState state)
    {
        var text = state.Report == "" ? Loc.GetString("autopsy-ui-empty") : state.Report;
        if (text != _text) { _text = text; _report.SetMessage(Robust.Shared.Utility.FormattedMessage.FromUnformatted(text)); }
        _status.Text = Loc.GetString(state.Busy ? "autopsy-ui-busy" : "autopsy-ui-ready", ("paper", state.Paper));
        _deep.Disabled = state.Busy || !state.Detailed;
        _print.Disabled = state.Busy || state.Report == "" || state.Paper <= 0;
        _record.Disabled = state.Busy || state.Report == "" || !state.Record;
    }
}
