using System.Linq;
using Content.Shared._radiant.Medical.Genetics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._radiant.Medical.Genetics;

public sealed class DnaVaultBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private DnaVaultWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<DnaVaultWindow>();
        _window.Prepare += id => SendMessage(new DnaVaultMessage(id));
        _window.Eject += () => SendMessage(new DnaVaultMessage(eject: true));
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is DnaVaultState data) _window?.Update(data);
    }
}
public sealed class DnaVaultWindow : DefaultWindow
{
    private readonly Label _humans = new(), _plants = new(), _animals = new(), _status = new(), _materials = new();
    private readonly ProgressBar[] _counts = new ProgressBar[3];
    private readonly RichTextLabel _patient = new(), _archive = new();
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1 };
    private readonly BoxContainer _rewards = MedicalLabUi.Column();
    private readonly Button _eject = MedicalLabUi.Button("genetics-eject");
    private readonly List<Button> _choices = new();
    private string _options = "";
    public event Action<string>? Prepare;
    public event Action? Eject;
    public DnaVaultWindow()
    {
        Title = Loc.GetString("dna-vault-title");
        _status.ClipText = true;
        MinWidth = 520;
        SetSize = new System.Numerics.Vector2(670, 670);
        var root = MedicalLabUi.Column();
        var help = new RichTextLabel();
        help.SetMessage(Loc.GetString("dna-vault-help"));
        root.AddChild(MedicalLabUi.Card("dna-vault-title", help));
        var collection = MedicalLabUi.Column();
        var labels = new[] { _humans, _plants, _animals };
        for (var i = 0; i < 3; i++)
        {
            collection.AddChild(labels[i]);
            _counts[i] = new ProgressBar { MinValue = 0, MaxValue = 1, HorizontalExpand = true };
            collection.AddChild(_counts[i]);
        }
        root.AddChild(MedicalLabUi.Card("dna-vault-collection", collection));
        root.AddChild(MedicalLabUi.Card("dna-vault-patient", _patient, _materials, _rewards, _eject));
        root.AddChild(MedicalLabUi.Card("genetics-tab-archive", _archive));
        var frame = MedicalLabUi.Column();
        frame.AddChild(MedicalLabUi.Scroll(root));
        frame.AddChild(_status);
        frame.AddChild(_progress);
        Contents.AddChild(frame);
        _eject.OnPressed += _ => Eject?.Invoke();
    }
    public void Update(DnaVaultState state)
    {
        _humans.Text = Loc.GetString("dna-vault-count-human", ("count", state.Humans), ("goal", state.HumanGoal));
        _plants.Text = Loc.GetString("dna-vault-count-plant", ("count", state.Plants), ("goal", state.PlantGoal));
        _animals.Text = Loc.GetString("dna-vault-count-animal", ("count", state.Animals), ("goal", state.AnimalGoal));
        var values = new[] { (state.Humans, state.HumanGoal), (state.Plants, state.PlantGoal), (state.Animals, state.AnimalGoal) };
        for (var i = 0; i < 3; i++) _counts[i].Value = Math.Clamp(values[i].Item1 / (float) Math.Max(1, values[i].Item2), 0, 1);
        _patient.SetMessage(Robust.Shared.Utility.FormattedMessage.EscapeText(state.Sample));
        _archive.SetMessage(Robust.Shared.Utility.FormattedMessage.EscapeText(state.Archive));
        _status.Text = state.Status;
        _status.ToolTip = state.Status;
        _materials.Text = Loc.GetString("genetics-ui-materials", ("stored", state.Biomass), ("cost", state.Cost));
        _eject.Disabled = !state.CanEject;
        _progress.Value = state.Progress;
        var options = string.Join("\n", state.Rewards.Select(p => p.Key + p.Value));
        if (_options != options)
        {
            _options = options;
            _rewards.DisposeAllChildren();
            _choices.Clear();
            foreach (var (id, description) in state.Rewards)
            {
                var text = new RichTextLabel();
                text.SetMessage(Robust.Shared.Utility.FormattedMessage.EscapeText(description));
                _rewards.AddChild(text);
                var button = MedicalLabUi.Button("dna-vault-prepare");
                button.OnPressed += _ => Prepare?.Invoke(id);
                _choices.Add(button);
                _rewards.AddChild(button);
            }
        }
        foreach (var choice in _choices) choice.Disabled = !state.CanPrepare;
    }
}
