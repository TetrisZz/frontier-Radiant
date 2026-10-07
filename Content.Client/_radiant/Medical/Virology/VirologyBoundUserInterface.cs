using System.Linq;
using Content.Shared._radiant.Medical.Virology;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Medical.Virology;

public sealed class VirologyBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private VirologyWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<VirologyWindow>();
        _window.Command += (command, id) => SendMessage(new VirologyMessage(command, id));
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is VirologyState data) _window?.Update(data);
    }
}

public sealed class VirologyWindow : DefaultWindow
{
    private readonly RichTextLabel _report = new() { VerticalAlignment = Control.VAlignment.Top };
    private readonly RichTextLabel _archive = new() { VerticalAlignment = Control.VAlignment.Top };
    private readonly Label _status = new();
    private readonly OptionButton _disease = new() { HorizontalExpand = true };
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1 };
    private readonly Button _analyze = new() { Text = Loc.GetString("virology-analyze") };
    private readonly Button _treat = new() { Text = Loc.GetString("virology-treat") };
    private readonly Button _vaccinate = new() { Text = Loc.GetString("virology-vaccinate") };
    private readonly Button _eject = new() { Text = Loc.GetString("virology-eject") };
    private string[] _ids = [];
    public event Action<VirologyCommand, string>? Command;
    public VirologyWindow()
    {
        Title = Loc.GetString("virology-title");
        SetSize = new System.Numerics.Vector2(650, 430);
        MinWidth = 400;
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        var tabs = new TabContainer { HorizontalExpand = true, VerticalExpand = true };
        var reportScroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        reportScroll.AddChild(_report);
        tabs.AddChild(reportScroll);
        tabs.SetTabTitle(0, Loc.GetString("virology-tab-report"));
        var archiveScroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        archiveScroll.AddChild(_archive);
        tabs.AddChild(archiveScroll);
        tabs.SetTabTitle(1, Loc.GetString("virology-tab-archive"));
        root.AddChild(tabs);
        root.AddChild(_status);
        root.AddChild(_progress);
        root.AddChild(_disease);
        foreach (var button in new[] { _analyze, _treat, _vaccinate, _eject }) root.AddChild(button);
        Contents.AddChild(root);
        _analyze.OnPressed += _ => Command?.Invoke(VirologyCommand.Analyze, "");
        _treat.OnPressed += _ => Command?.Invoke(VirologyCommand.Treat, "");
        _vaccinate.OnPressed += _ => Command?.Invoke(VirologyCommand.Vaccinate, "");
        _eject.OnPressed += _ => Command?.Invoke(VirologyCommand.Eject, "");
        _disease.OnItemSelected += args =>
        {
            if (args.Id >= 0 && args.Id < _ids.Length) Command?.Invoke(VirologyCommand.Select, _ids[args.Id]);
        };
    }
    public void Update(VirologyState state)
    {
        _report.SetMessage(FormattedMessage.EscapeText(state.Report));
        _archive.SetMessage(FormattedMessage.EscapeText(state.Archive));
        _status.Text = state.Status;
        _progress.Value = state.Progress;
        _progress.Visible = state.Working;
        _analyze.Visible = !state.Producer;
        _treat.Visible = _vaccinate.Visible = _disease.Visible = state.Producer && state.Diseases.Count > 0;
        _analyze.Disabled = !state.CanAnalyze;
        _treat.Disabled = _vaccinate.Disabled = !state.CanProduce;
        _eject.Disabled = !state.CanEject;
        _disease.Disabled = !state.CanEject;
        _ids = state.Diseases.Keys.ToArray();
        _disease.Clear();
        for (var i = 0; i < _ids.Length; i++)
        {
            _disease.AddItem(state.Diseases[_ids[i]], i);
            if (_ids[i] == state.Selected) _disease.SelectId(i);
        }
    }
}
