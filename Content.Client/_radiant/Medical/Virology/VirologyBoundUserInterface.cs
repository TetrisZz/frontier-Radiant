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
    private readonly Button _analyze = MedicalLabUi.Button("virology-analyze");
    private readonly Button _treat = MedicalLabUi.Button("virology-treat");
    private readonly Button _vaccinate = MedicalLabUi.Button("virology-vaccinate");
    private readonly Button _eject = MedicalLabUi.Button("virology-eject");
    private readonly Button _study = MedicalLabUi.Button("virology-study");
    private readonly Button _culture = MedicalLabUi.Button("virology-culture");
    private readonly RichTextLabel _design = new(), _studyHelp = new();
    private readonly BoxContainer _symptoms = MedicalLabUi.Column();
    private readonly Dictionary<string, CheckBox> _checks = new();
    private string _symptomOptions = "", _diseaseOptions = "";
    private string[] _ids = [];
    public event Action<VirologyCommand, string>? Command;
    public VirologyWindow()
    {
        Title = Loc.GetString("virology-title");
        MedicalLabUi.FitSelector(_disease);
        _status.ClipText = true;
        SetSize = new System.Numerics.Vector2(720, 650);
        MinWidth = 520;
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        var tabs = new TabContainer { HorizontalExpand = true, VerticalExpand = true };
        var reportScroll = MedicalLabUi.Scroll(MedicalLabUi.Card("virology-tab-report", _report));
        tabs.AddChild(reportScroll);
        tabs.SetTabTitle(0, Loc.GetString("virology-tab-report"));
        var archiveScroll = MedicalLabUi.Scroll(MedicalLabUi.Card("virology-tab-archive", _archive));
        tabs.AddChild(archiveScroll);
        tabs.SetTabTitle(1, Loc.GetString("virology-tab-archive"));
        var research = MedicalLabUi.Column();
        research.AddChild(MedicalLabUi.Card("virology-lab-study", _studyHelp, _study));
        research.AddChild(MedicalLabUi.Card("virology-lab-design", _design, _symptoms, _culture));
        tabs.AddChild(MedicalLabUi.Scroll(research));
        tabs.SetTabTitle(2, Loc.GetString("virology-lab-design"));
        root.AddChild(tabs);
        root.AddChild(_status);
        root.AddChild(_progress);
        root.AddChild(_disease);
        root.AddChild(MedicalLabUi.Card("virology-lab-treatment", _analyze, _treat, _vaccinate));
        root.AddChild(_eject);
        Contents.AddChild(root);
        _analyze.OnPressed += _ => Command?.Invoke(VirologyCommand.Analyze, "");
        _treat.OnPressed += _ => Command?.Invoke(VirologyCommand.Treat, "");
        _vaccinate.OnPressed += _ => Command?.Invoke(VirologyCommand.Vaccinate, "");
        _eject.OnPressed += _ => Command?.Invoke(VirologyCommand.Eject, "");
        _study.OnPressed += _ => Command?.Invoke(VirologyCommand.Study, "");
        _culture.OnPressed += _ => Command?.Invoke(VirologyCommand.Culture, "");
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
        _status.ToolTip = state.Status;
        _progress.Value = state.Progress;
        _progress.Visible = state.Working;
        _design.SetMessage(FormattedMessage.EscapeText(state.Design));
        _studyHelp.SetMessage(FormattedMessage.EscapeText(state.Study));
        _study.Visible = !state.Producer;
        _study.Disabled = !state.CanStudy;
        _culture.Visible = state.Producer;
        _culture.Disabled = !state.CanCulture;
        var symptomOptions = string.Join("\n", state.Symptoms.Select(p => p.Key + p.Value));
        if (_symptomOptions != symptomOptions)
        {
            _symptomOptions = symptomOptions;
            _symptoms.DisposeAllChildren();
            _checks.Clear();
            foreach (var (id, description) in state.Symptoms)
            {
                var check = new CheckBox { Text = description.Split('\n')[0], HorizontalExpand = true };
                check.OnToggled += _ => Command?.Invoke(VirologyCommand.ToggleSymptom, id);
                _checks[id] = check;
                _symptoms.AddChild(check);
                var detail = new RichTextLabel();
                detail.SetMessage(FormattedMessage.EscapeText(description.Contains('\n') ? description.Split('\n', 2)[1] : ""));
                _symptoms.AddChild(detail);
            }
        }
        foreach (var (id, check) in _checks)
        {
            check.Pressed = state.Draft.Contains(id);
            check.Disabled = !state.CanDesign;
        }
        _analyze.Visible = !state.Producer;
        _treat.Visible = _vaccinate.Visible = _disease.Visible = state.Producer && state.Diseases.Count > 0;
        _analyze.Disabled = !state.CanAnalyze;
        _treat.Disabled = _vaccinate.Disabled = !state.CanProduce;
        _eject.Disabled = !state.CanEject;
        _disease.Disabled = !state.CanEject;
        var diseaseOptions = string.Join("\n", state.Diseases.Select(p => p.Key + p.Value));
        if (_diseaseOptions != diseaseOptions)
        {
            _diseaseOptions = diseaseOptions;
            _ids = state.Diseases.Keys.ToArray();
            _disease.Clear();
            for (var i = 0; i < _ids.Length; i++) _disease.AddItem(state.Diseases[_ids[i]], i);
        }
        var selected = Array.IndexOf(_ids, state.Selected);
        if (selected >= 0) _disease.SelectId(selected);
    }
}
