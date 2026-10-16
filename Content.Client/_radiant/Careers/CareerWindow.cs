using System.Linq;
using System.Numerics;
using Content.Client.Administration.UI.CustomControls;
using Content.Shared._radiant.Careers;
using Content.Shared._radiant.Skills;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._radiant.Careers;

public sealed class CareerWindow : DefaultWindow
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IEntityManager _entities = default!;

    public event Action<string, bool>? SelectRequested;

    private readonly ItemList _professions = new() { Name = "ProfessionList", SetWidth = 180, VerticalExpand = true, ItemSeparation = 6 };
    private readonly OptionButton _stages = new() { HorizontalExpand = true };
    private readonly ScrollContainer _scroll = new() { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false };
    private readonly RichTextLabel _status = new();
    private readonly BoxContainer _details = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8, HorizontalExpand = true };
    private readonly Dictionary<string, (RichTextLabel Counter, ProgressBar Bar)> _progress = new();
    private int _stage = -1;
    private readonly Button _select = new() { Name = "SelectProfession", Text = Loc.GetString("careers-client-select") };
    private readonly BoxContainer _confirmation = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, Visible = false };
    private readonly RichTextLabel _confirmationText = new();
    private readonly List<CareerProfessionPrototype> _choices = new();
    private CareerData? _career;
    private CareerProfessionPrototype? _selected;
    private bool _canChange;
    private bool _waiting;
    private string? _message;

    public CareerWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("careers-client-title");
        MinSize = new Vector2(620, 420);
        SetSize = new Vector2(780, 620);

        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
        Contents.AddChild(body);
        body.AddChild(_status);
        var columns = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 16, VerticalExpand = true };
        body.AddChild(columns);
        var list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        list.AddChild(new Label { Text = Loc.GetString("careers-client-list"), StyleClasses = { "LabelHeading" } });
        list.AddChild(_professions);
        columns.AddChild(list);
        _scroll.AddChild(_details);
        _scroll.OnResized += () => _details.MaxWidth = Math.Max(1, _scroll.Size.X - 16);
        columns.AddChild(_scroll);
        body.AddChild(_select);
        _confirmation.AddChild(_confirmationText);
        var actions = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 6 };
        var confirm = new Button { Text = Loc.GetString("careers-client-confirm-reset"), HorizontalExpand = true };
        var cancel = new Button { Text = Loc.GetString("careers-client-cancel") };
        actions.AddChild(confirm);
        actions.AddChild(cancel);
        _confirmation.AddChild(actions);
        body.AddChild(_confirmation);

        _professions.OnItemSelected += args =>
        {
            _selected = _choices[args.ItemIndex];
            _stage = DefaultStage();
            _confirmation.Visible = false;
            _scroll.VScrollTarget = 0;
            Render();
        };
        _stages.OnItemSelected += args =>
        {
            _stage = args.Id;
            _scroll.VScrollTarget = 0;
            Render();
        };
        _select.OnPressed += _ =>
        {
            if (!CanSelect())
                return;

            _confirmationText.SetMessage(Loc.GetString("careers-client-reset-warning",
                ("profession", Loc.GetString(_selected!.Name))));
            _confirmation.Visible = true;
            _select.Disabled = true;
        };
        cancel.OnPressed += _ =>
        {
            _confirmation.Visible = false;
            Render();
        };
        confirm.OnPressed += _ =>
        {
            if (!CanSelect() || !_confirmation.Visible)
                return;

            var profession = _selected!.ID;
            _waiting = true;
            _confirmation.Visible = false;
            Render();
            SelectRequested?.Invoke(profession, true);
        };
        SetLoading();
    }

    public void SetLoading()
    {
        _career = null;
        _canChange = false;
        _waiting = true;
        _message = null;
        _confirmation.Visible = false;
        _stages.Orphan();
        _details.DisposeAllChildren();
        _professions.Clear();
        _progress.Clear();
        _select.Disabled = true;
        _status.SetMessage(Loc.GetString("careers-client-loading"));
    }

    public void UpdateState(CareerStateEvent state)
    {
        var previous = _career;
        var unchanged = previous != null && previous.CharacterId == state.Career.CharacterId &&
            previous.Profession == state.Career.Profession && previous.Level == state.Career.Level &&
            previous.BoxTier == state.Career.BoxTier && previous.PendingRewards.SequenceEqual(state.Career.PendingRewards) &&
            _canChange == state.CanChange && _message == state.Message && !_waiting;
        _career = state.Career.Clone();
        _canChange = state.CanChange;
        _waiting = false;
        _message = state.Message;
        if (unchanged)
        {
            UpdateProgress();
            return;
        }
        _confirmation.Visible = false;

        var selectedId = previous != null && previous.CharacterId == _career.CharacterId && previous.Profession == _career.Profession
            ? _selected?.ID
            : _career.Profession;
        _choices.Clear();
        _choices.AddRange(_prototypes.EnumeratePrototypes<CareerProfessionPrototype>()
            .OrderBy(p => Loc.GetString(p.Name)));
        _professions.Clear();
        for (var i = 0; i < _choices.Count; i++)
        {
            var choice = _choices[i];
            var icon = choice.Icon is { } entity ? _entities.System<SpriteSystem>().GetPrototypeIcon(entity).Default : null;
            _professions.AddItem(Loc.GetString(choice.Name), icon, metadata: choice.ID);
        }

        var index = _choices.FindIndex(p => p.ID == selectedId);
        if (index < 0)
            index = _choices.FindIndex(p => p.ID == _career.Profession);
        if (index < 0 && _choices.Count > 0)
            index = 0;
        _selected = index >= 0 ? _choices[index] : null;
        if (index >= 0)
            _professions[index].Selected = true;
        _stage = DefaultStage();
        Render();
    }

    private int DefaultStage() => _selected == null || _selected.Levels.Count == 0 ? -1
        : Math.Clamp(_selected.ID == _career?.Profession ? _career.Level : 0, 0, _selected.Levels.Count - 1);

    private bool CanSelect() => !_waiting && _canChange && _career != null && _selected != null
        && _selected.ID != _career.Profession;

    private void Render()
    {
        _select.Disabled = !CanSelect() || _confirmation.Visible;
        _select.Visible = _canChange;
        foreach (var choice in _professions)
            choice.Disabled = _waiting;
        _stages.Orphan();
        _details.DisposeAllChildren();
        _progress.Clear();
        if (_career == null)
            return;

        var currentName = _prototypes.TryIndex<CareerProfessionPrototype>(_career.Profession, out var current)
            ? Loc.GetString(current.Name)
            : Loc.GetString("careers-client-unknown-profession");
        _status.SetMessage(Loc.GetString("careers-client-current",
            ("profession", currentName), ("level", _career.Level), ("tier", _career.BoxTier)));
        if (_message != null)
            AddText(Loc.TryGetString(_message, out var message)
                ? message
                : Loc.GetString("careers-client-server-message", ("message", _message)));
        if (_waiting)
            AddText(Loc.GetString("careers-client-loading"));
        if (!_canChange)
            AddText(Loc.GetString("careers-client-read-only"));

        if (_selected != null)
        {
            AddHeading(Loc.GetString(_selected.Name));
            if (_selected.Description is { } description)
                AddText(Loc.GetString(description));
            AddText(_selected.RequiredSkill is { } skill
                ? Loc.GetString("careers-client-required-skill", ("skill", Loc.GetString(ProfessionalSkillRules.NameKey(skill))),
                    ("level", _selected.RequiredSkillLevel))
                : Loc.GetString("careers-client-no-required-skill"));

            var isCurrent = _selected.ID == _career.Profession;
            _stages.Clear();
            for (var i = 0; i < _selected.Levels.Count; i++)
            {
                var key = isCurrent && i < _career.Level ? "careers-client-stage-completed"
                    : isCurrent && i == _career.Level ? "careers-client-stage-current" : "careers-client-stage-preview";
                _stages.AddItem(Loc.GetString(key, ("level", i + 1)), i);
            }
            var stage = _stage;
            if (stage >= 0)
            {
                _stages.SelectId(stage);
                _details.AddChild(_stages);
            }
            if (isCurrent && _career.Level >= _selected.Levels.Count && _selected.Levels.Count > 0)
                AddText(Loc.GetString("careers-client-complete"));
            if (stage >= 0 && stage < _selected.Levels.Count
                && _prototypes.TryIndex(_selected.Levels[stage], out var level))
            {
                AddHeading(Loc.GetString("careers-client-objectives"));
                foreach (var objective in level.Objectives)
                {
                    AddText(Loc.GetString(objective.Title));
                    var counter = new RichTextLabel { HorizontalExpand = true };
                    _details.AddChild(counter);
                    var bar = new ProgressBar
                    {
                        MinValue = 0,
                        MaxValue = 1,
                        MinHeight = 10,
                        HorizontalExpand = true,
                    };
                    _details.AddChild(bar);
                    _progress[objective.Id] = (counter, bar);
                }
                UpdateProgress();
                AddHeading(Loc.GetString("careers-client-rewards"));
                AddText(Loc.GetString("careers-client-reward-money", ("amount", level.RewardMoney)));
                AddText(Loc.GetString("careers-client-reward-tier", ("tier", level.BoxTier)));
                foreach (var item in level.RewardItems)
                {
                    var name = _prototypes.TryIndex(item, out var prototype)
                        ? prototype.Name
                        : Loc.GetString("careers-client-unknown-item");
                    var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
                    if (prototype != null)
                        row.AddChild(new TextureRect
                        {
                            Texture = _entities.System<SpriteSystem>().GetPrototypeIcon(item).Default,
                            SetSize = new Vector2(32, 32),
                            Stretch = TextureRect.StretchMode.KeepAspectCentered,
                            ToolTip = prototype.Description,
                        });
                    var text = new RichTextLabel { HorizontalExpand = true, VerticalAlignment = Control.VAlignment.Center };
                    text.SetMessage(name);
                    row.AddChild(text);
                    _details.AddChild(row);
                }
            }
            else
            {
                AddText(Loc.GetString(stage >= _selected.Levels.Count && _selected.Levels.Count > 0
                    ? "careers-client-complete"
                    : "careers-client-no-objectives"));
            }
        }

        AddHeading(Loc.GetString("careers-client-pending"));
        if (_career.PendingRewards.Count == 0)
            AddText(Loc.GetString("careers-client-no-pending"));
        foreach (var reward in _career.PendingRewards)
            AddText(Loc.GetString("careers-client-pending-level", ("level", reward)));
        AddText(Loc.GetString("careers-client-claim-at-safe"));
    }

    private void AddText(string text)
    {
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(text);
        _details.AddChild(label);
    }

    private void AddHeading(string text)
    {
        _details.AddChild(new HSeparator { Margin = new Thickness(0, 6, 0, 2) });
        _details.AddChild(new Label { Text = text, StyleClasses = { "LabelHeading" } });
    }

    private void UpdateProgress()
    {
        if (_career == null || _selected == null || _stage < 0 || _stage >= _selected.Levels.Count ||
            !_prototypes.TryIndex(_selected.Levels[_stage], out var level))
            return;
        foreach (var objective in level.Objectives)
        {
            if (!_progress.TryGetValue(objective.Id, out var controls))
                continue;
            var current = _selected.ID == _career.Profession;
            var value = current && _stage < _career.Level ? objective.Target
                : current && _stage == _career.Level ? _career.Progress.GetValueOrDefault(objective.Id) : 0;
            value = Math.Clamp(value, 0, Math.Max(0, objective.Target));
            controls.Counter.SetMessage(Loc.GetString("careers-client-counter",
                ("progress", Math.Floor(value)), ("target", Math.Ceiling(objective.Target))));
            controls.Bar.Value = objective.Target > 0 ? (float) Math.Clamp(value / objective.Target, 0, 1) : 0;
        }
    }
}
