using System.Linq;
using Content.Shared._radiant.Medical.Genetics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._radiant.Medical.Genetics;

public sealed class GeneticBodyBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private GeneticBodyWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<GeneticBodyWindow>();
        _window.Command += (command, product, text) => SendMessage(new GeneticBodyMessage(command, product, text));
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is GeneticBodyState data)
            _window?.Update(data);
    }
}

public sealed class GeneticResearchBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private GeneticResearchWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<GeneticResearchWindow>();
        _window.Command += (command, product, text) => SendMessage(new GeneticBodyMessage(command, product, text));
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is GeneticBodyState data)
            _window?.Update(data);
    }
}

public sealed class GeneticResearchWindow : GeneticBodyWindow
{
    public GeneticResearchWindow() : base(true) { }
}

public class GeneticBodyWindow : DefaultWindow
{
    private readonly bool _console;
    private readonly RichTextLabel _nextStep = new();
    private readonly RichTextLabel _sample = new();
    private readonly RichTextLabel _profile = new();
    private readonly RichTextLabel _description = new();
    private readonly RichTextLabel _connection = new();
    private readonly RichTextLabel _productionStatus = new();
    private readonly RichTextLabel _archive = new();
    private readonly RichTextLabel _feedback = new();
    private readonly RichTextLabel _composition = new();
    private readonly RichTextLabel _geneHelp = new();
    private readonly RichTextLabel _warning = new();
    private readonly Label _pattern = new();
    private readonly Label _materials = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1, HorizontalExpand = true };
    private readonly OptionButton _gene = new() { HorizontalExpand = true };
    private readonly OptionButton _product = new() { HorizontalExpand = true };
    private readonly OptionButton[] _bases = new OptionButton[6];
    private readonly List<string> _geneIds = new();
    private readonly Button _analyze = MakeButton("genetics-ui-analyze");
    private readonly Button _research = MakeButton("genetics-ui-research");
    private readonly Button _spectrum = MakeButton("genetics-spectrum-button");
    private readonly Button _check = MakeButton("genetics-sequence-check");
    private readonly Button _fill = MakeButton("genetics-sequence-fill");
    private readonly Button _grow = MakeButton("genetics-ui-grow");
    private readonly Button _release = MakeButton("genetics-ui-release");
    private readonly Button _eject = MakeButton("genetics-eject");
    private readonly Button _ejectDisk = MakeButton("genetics-disk-eject");
    private string _known = "??????";
    private string _geneOptions = "";
    private string _productOptions = "";
    private string _lastGene = "";
    private NetEntity? _lastSample;
    private GeneticProduct _selected;
    public event Action<GeneticCommand, GeneticProduct, string>? Command;

    public GeneticBodyWindow() : this(false) { }

    protected GeneticBodyWindow(bool console)
    {
        _console = console;
        Title = Loc.GetString(console ? "genetics-console-title" : "genetics-ui-title");
        MinWidth = 500;
        SetSize = new System.Numerics.Vector2(console ? 820 : 650, console ? 740 : 620);
        var root = Column();
        root.AddChild(_nextStep);
        var production = Column();
        production.AddChild(_connection);
        production.AddChild(_productionStatus);
        production.AddChild(_product);
        production.AddChild(_description);
        production.AddChild(_materials);
        production.AddChild(_grow);
        if (console)
        {
            var tabs = new TabContainer { HorizontalExpand = true, VerticalExpand = true };
            root.AddChild(tabs);
            var sample = Column();
            sample.AddChild(_sample);
            sample.AddChild(_profile);
            sample.AddChild(_analyze);
            sample.AddChild(_eject);
            AddTab(tabs, sample, "genetics-tab-sample");
            var research = Column();
            research.AddChild(_gene);
            research.AddChild(_geneHelp);
            research.AddChild(_composition);
            research.AddChild(_spectrum);
            research.AddChild(_research);
            research.AddChild(_pattern);
            var sequence = new BoxContainer { SeparationOverride = 8 };
            for (var i = 0; i < _bases.Length; i++)
            {
                var cell = new OptionButton { MinWidth = 52, HorizontalExpand = true };
                foreach (var letter in "ATGC")
                    cell.AddItem(letter.ToString());
                cell.SelectId(0);
                cell.OnItemSelected += args => cell.SelectId(args.Id);
                _bases[i] = cell;
                sequence.AddChild(cell);
            }
            research.AddChild(sequence);
            research.AddChild(_fill);
            research.AddChild(_check);
            research.AddChild(_feedback);
            AddTab(tabs, research, "genetics-tab-research");
            var archive = Column();
            archive.AddChild(_archive);
            archive.AddChild(_ejectDisk);
            AddTab(tabs, archive, "genetics-tab-archive");
            AddTab(tabs, production, "genetics-tab-production");
            _gene.OnItemSelected += args => Send(GeneticCommand.SelectGene, _geneIds[args.Id]);
            _fill.OnPressed += _ => FillKnown();
            _check.OnPressed += _ => Send(GeneticCommand.CheckSequence,
                new string(_bases.Select(cell => "ATGC"[cell.SelectedId]).ToArray()));
        }
        else
        {
            production.AddChild(_sample);
            production.AddChild(_analyze);
            production.AddChild(_release);
            production.AddChild(_eject);
            production.AddChild(_ejectDisk); // Recover disks left in legacy capsules.
            root.AddChild(Scroll(production));
        }
        root.AddChild(_status);
        root.AddChild(_progress);
        root.AddChild(_warning);
        Contents.AddChild(root);
        _product.OnItemSelected += args => Command?.Invoke(GeneticCommand.Select, (GeneticProduct) args.Id, "");
        _analyze.OnPressed += _ => Send(GeneticCommand.Analyze);
        _research.OnPressed += _ => Send(GeneticCommand.Research);
        _spectrum.OnPressed += _ => Send(GeneticCommand.Spectrum);
        _grow.OnPressed += _ => Send(GeneticCommand.Grow);
        _release.OnPressed += _ => Send(GeneticCommand.Release);
        _eject.OnPressed += _ => Send(GeneticCommand.Eject);
        _ejectDisk.OnPressed += _ => Send(GeneticCommand.EjectDisk);
    }

    private static Button MakeButton(string key) => new() { Text = Loc.GetString(key), HorizontalExpand = true };
    private static BoxContainer Column() => new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10, HorizontalExpand = true };
    private static ScrollContainer Scroll(Control child)
    {
        var scroll = new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true };
        scroll.AddChild(child);
        return scroll;
    }
    private static void AddTab(TabContainer tabs, Control content, string title)
    {
        tabs.AddChild(Scroll(content));
        tabs.SetTabTitle(tabs.ChildCount - 1, Loc.GetString(title));
    }
    private void Send(GeneticCommand command, string text = "") => Command?.Invoke(command, _selected, text);
    private void FillKnown()
    {
        for (var i = 0; i < _bases.Length && i < _known.Length; i++)
        {
            var index = "ATGC".IndexOf(_known[i]);
            if (index >= 0)
                _bases[i].SelectId(index);
        }
    }

    public void Update(GeneticBodyState state)
    {
        _selected = state.Selected;
        _grow.Text = Loc.GetString(state.Selected >= GeneticProduct.Hematopoiesis
            ? "genetics-ui-synthesize" : "genetics-ui-grow");
        _sample.SetMessage(state.Sample);
        _profile.SetMessage(state.Profile);
        _nextStep.SetMessage(_console ? state.NextStep :
            state.CanAnalyze ? Loc.GetString("genetics-pod-analyze-help") : Loc.GetString("genetics-pod-help"));
        _description.SetMessage(state.ProductDescription);
        _connection.SetMessage(state.Connection);
        _productionStatus.SetMessage(state.ProductionStatus);
        _archive.SetMessage(state.Archive);
        _analyze.Disabled = !state.CanAnalyze;
        _research.Disabled = !state.CanResearch;
        _spectrum.Disabled = !state.CanSpectrum;
        _composition.SetMessage(state.Composition);
        _eject.Disabled = !state.CanEject;
        _ejectDisk.Disabled = !state.CanEjectDisk;
        _ejectDisk.Visible = _console || state.HasDisk;
        _grow.Disabled = !state.CanGrow;
        _release.Disabled = !state.CanRelease;
        _warning.SetMessage(state.Warning);
        _warning.Visible = state.Warning.Length > 0;
        _status.Text = state.Status + " · " + Loc.GetString("genetics-ui-time", ("seconds", state.Seconds));
        _progress.Value = state.Progress;
        _materials.Text = Loc.GetString("genetics-ui-materials", ("stored", state.Biomass), ("cost", state.Cost));
        var productOptions = string.Join(",", state.Products);
        if (_productOptions != productOptions)
        {
            _productOptions = productOptions;
            _product.Clear();
            foreach (var product in state.Products)
                _product.AddItem(Loc.GetString("genetics-product-" + product.ToString().ToLowerInvariant()), (int) product);
        }
        if (state.Products.Contains(state.Selected))
            _product.SelectId((int) state.Selected);
        _product.Disabled = !state.CanSelect;
        if (!_console)
            return;
        _known = state.SequencePattern;
        _geneHelp.SetMessage(state.GeneDescription);
        _pattern.Text = Loc.GetString("genetics-sequence-pattern", ("pattern", _known));
        _feedback.SetMessage(state.SequenceFeedback);
        _check.Disabled = !state.CanCheckSequence;
        _fill.Disabled = !state.CanCheckSequence;
        foreach (var cell in _bases)
            cell.Disabled = !state.CanCheckSequence;
        var geneOptions = string.Join("\n", state.Genes.Select(pair => pair.Key + ":" + pair.Value));
        if (_geneOptions != geneOptions)
        {
            _geneOptions = geneOptions;
            _gene.Clear();
            _geneIds.Clear();
            foreach (var (id, name) in state.Genes)
            {
                _gene.AddItem(name, _geneIds.Count);
                _geneIds.Add(id);
            }
        }
        var selected = _geneIds.IndexOf(state.SelectedGene);
        if (selected >= 0)
            _gene.SelectId(selected);
        if (_lastGene != state.SelectedGene || _lastSample != state.SampleEntity)
        {
            _lastSample = state.SampleEntity;
            _lastGene = state.SelectedGene;
            foreach (var cell in _bases)
                cell.SelectId(0);
            FillKnown();
        }
        _gene.Disabled = !state.CanSelect;
    }
}
