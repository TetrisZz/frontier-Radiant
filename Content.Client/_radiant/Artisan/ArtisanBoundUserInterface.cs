using System.Numerics;
using System.Linq;
using Content.Shared._radiant.Artisan;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.Graphics;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Artisan;

public sealed class ArtisanBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private ArtisanWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = new ArtisanWindow();
        _window.OnClose += Close;
        _window.Action += SendMessage;
        Update<ArtisanUiState>();
        _window.OpenCentered();
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ArtisanUiState workshop) _window?.SetState(workshop);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && _window != null) { _window.Action -= SendMessage; _window.Dispose(); _window = null; }
        base.Dispose(disposing);
    }
}

public sealed class ArtisanWindow : DefaultWindow
{
    [Dependency] private readonly IEntityManager _entities = default!;
    public event Action<ArtisanActionMessage>? Action;
    private readonly RichTextLabel _status = new();
    private readonly Label _temperature = new();
    private readonly ProgressBar _progress = new() { MinValue = 0, MaxValue = 1, MinHeight = 12 };
    private readonly RichTextLabel _liquids = new();
    private readonly RichTextLabel _hint = new();
    private readonly Label _recipeHeading = new() { StyleClasses = { "LabelHeading" } };
    private readonly BoxContainer _recipes = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
    private readonly BoxContainer _items = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6 };
    private readonly BoxContainer _settings = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };
    private readonly LineEdit _batch = new() { Name = "BatchName" };
    private readonly SpinBox _amount = new() { MinWidth = 160, HorizontalExpand = true, IsValid = value => value is >= 1 and <= 100 };
    private readonly BoxContainer _doseRow = new() { SeparationOverride = 8 };
    private readonly RichTextLabel _settingsHint = new();
    private readonly RichTextLabel _settingsStatus = new();
    private readonly Button _apply = new() { HorizontalExpand = true };
    private readonly Button _stop = new();
    private readonly Button _bottle = new();
    private readonly Button _tap = new() { Name = "ArtisanTapMode" };
    private string _recipeSignature = "";
    private string _itemSignature = "";
    private bool _draftInitialized;
    private bool _settingsDirty;
    private bool _updatingSettings;

    public ArtisanWindow()
    {
        IoCManager.InjectDependencies(this);
        MinSize = new Vector2(480, 600);
        SetSize = new Vector2(620, 740);
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10, Margin = new Thickness(8) };
        Contents.AddChild(body);
        var header = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#20313A") } };
        var summary = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 5, Margin = new Thickness(10) };
        summary.AddChild(_status); summary.AddChild(_temperature); summary.AddChild(_progress); summary.AddChild(_liquids);
        header.AddChild(summary); body.AddChild(header);
        _tap.OnPressed += _ => Action?.Invoke(new(ArtisanAction.ToggleTap));
        body.AddChild(_tap);
        SetText(_hint, Loc.GetString("artisan-ui-hint"));
        body.AddChild(_hint);
        var scroll = new ScrollContainer { VerticalExpand = true, HorizontalExpand = true };
        var content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10, MaxWidth = 520 };
        scroll.OnResized += () => content.MaxWidth = Math.Max(1, scroll.Size.X - 16);
        _recipeHeading.Text = Loc.GetString("artisan-ui-recipes");
        content.AddChild(_recipeHeading);
        content.AddChild(_recipes);
        content.AddChild(new Label { Text = Loc.GetString("artisan-ui-inputs"), StyleClasses = { "LabelHeading" } });
        content.AddChild(_items); scroll.AddChild(content); body.AddChild(scroll);
        _settings.AddChild(new Label { Text = Loc.GetString("artisan-ui-batch"), StyleClasses = { "LabelHeading" } });
        _settings.AddChild(_settingsHint);
        _batch.PlaceHolder = Loc.GetString("artisan-ui-batch-placeholder");
        _batch.OnTextChanged += _ => OnSettingsEdited();
        _batch.OnTextEntered += _ => SaveSettings();
        _settings.AddChild(_batch);
        _amount.LineEditControl.Name = "BottleDose";
        _amount.InitDefaultButtons();
        _amount.OverrideValue(30);
        _amount.ValueChanged += _ => OnSettingsEdited();
        _doseRow.AddChild(new Label { Text = Loc.GetString("artisan-ui-dose") });
        _doseRow.AddChild(_amount);
        _doseRow.AddChild(new Label { Text = Loc.GetString("artisan-ui-units") });
        _settings.AddChild(_doseRow);
        _settingsStatus.Modulate = Color.FromHex("#E7BB79");
        _settings.AddChild(_settingsStatus);
        _apply.Text = Loc.GetString("artisan-ui-apply");
        _apply.OnPressed += _ => SaveSettings(); _settings.AddChild(_apply); body.AddChild(_settings);
        var controls = new BoxContainer { SeparationOverride = 8 };
        _bottle.Text = Loc.GetString("artisan-ui-bottle"); _bottle.HorizontalExpand = true;
        _bottle.OnPressed += _ => Action?.Invoke(SettingsMessage(ArtisanAction.Bottle));
        _stop.Text = Loc.GetString("artisan-ui-stop"); _stop.HorizontalExpand = true;
        _stop.OnPressed += _ => Action?.Invoke(new(ArtisanAction.Stop));
        controls.AddChild(_bottle); controls.AddChild(_stop); body.AddChild(controls);
    }

    private void SaveSettings()
    {
        Action?.Invoke(SettingsMessage(ArtisanAction.Settings));
    }

    private ArtisanActionMessage SettingsMessage(ArtisanAction action)
    {
        var name = string.Concat(_batch.Text.Where(c => !char.IsControl(c))).Trim();
        if (name.Length > 48) name = name[..48];
        _batch.Text = name;
        _settingsDirty = true;
        return new(action, batchName: name, amount: _amount.Value, applySettings: true);
    }

    private void OnSettingsEdited()
    {
        if (_updatingSettings) return;
        _settingsDirty = true;
        SetText(_settingsStatus, Loc.GetString("artisan-ui-settings-draft"));
    }

    public void SetState(ArtisanUiState state)
    {
        Title = state.Title;
        SetText(_hint, Loc.GetString("artisan-hint-" + state.Kind.ToString().ToLowerInvariant()));
        SetText(_status, state.Status);
        _progress.Value = state.Progress;
        _recipeHeading.Visible = _recipes.Visible = state.Recipes.Count > 0;
        _temperature.Visible = state.Kind is ArtisanStationKind.Kiln or ArtisanStationKind.Furnace;
        _temperature.Text = Loc.GetString("artisan-ui-temperature", ("temperature", (int)state.Temperature));
        SetText(_liquids, state.Liquids); _liquids.Visible = state.Liquids != "";
        _settings.Visible = state.Kind is ArtisanStationKind.Fermenter or ArtisanStationKind.Bottler;
        _doseRow.Visible = state.Kind == ArtisanStationKind.Bottler;
        SetText(_settingsHint, Loc.GetString(state.Kind == ArtisanStationKind.Bottler ? "artisan-ui-label-help" : "artisan-ui-barrel-label-help"));
        _batch.Editable = !state.Busy;
        _amount.LineEditControl.Editable = !state.Busy;
        _amount.SetButtonDisabled(state.Busy);
        _apply.Disabled = state.Busy;
        _bottle.Visible = state.Kind == ArtisanStationKind.Bottler; _bottle.Disabled = state.Busy;
        _stop.Disabled = !state.Busy;
        _tap.Visible = state.Kind == ArtisanStationKind.Fermenter;
        _tap.Disabled = state.Busy;
        _tap.Text = Loc.GetString(state.DrainMode ? "artisan-tap-draining" : "artisan-tap-filling");
        // Preserve keyboard editing and scroll positions while progress updates arrive.
        if (!_draftInitialized || !_settingsDirty)
        {
            _updatingSettings = true;
            _batch.Text = state.BatchName;
            _amount.OverrideValue(state.BottleAmount);
            _updatingSettings = false;
            _draftInitialized = true;
        }
        if (_batch.Text == state.BatchName && _amount.Value == state.BottleAmount) _settingsDirty = false;
        SetText(_settingsStatus, Loc.GetString(_settingsDirty ? "artisan-ui-settings-draft" : "artisan-ui-settings-saved"));
        var recipeSignature = string.Join("|", state.Recipes.Select(recipe => recipe.Id + recipe.Enabled + recipe.Reason + recipe.Result + RequirementSignature(recipe.Requirements)));
        if (_recipeSignature != recipeSignature)
        {
            _recipeSignature = recipeSignature; _recipes.RemoveAllChildren();
            foreach (var recipe in state.Recipes)
            {
                var panel = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#19262D") } };
                var row = new BoxContainer { Margin = new Thickness(8), SeparationOverride = 12 };
                if (recipe.Result != "") row.AddChild(Preview(recipe.Result, 48));
                var details = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 5 };
                var button = new Button { Text = recipe.Name, ClipText = true, Disabled = !recipe.Enabled,
                    ToolTip = recipe.Name + (recipe.Reason == "" ? "" : "\n" + recipe.Reason), MinHeight = 32, HorizontalExpand = true };
                button.OnPressed += _ => Action?.Invoke(new(ArtisanAction.Recipe, recipe.Id)); details.AddChild(button);
                AddRequirements(details, recipe.Requirements);
                if (recipe.Reason != "")
                {
                    var reason = new RichTextLabel { Modulate = Color.FromHex("#E7BB79") };
                    SetText(reason, recipe.Reason); details.AddChild(reason);
                }
                row.AddChild(details); panel.AddChild(row); _recipes.AddChild(panel);
            }
        }
        var itemSignature = string.Join("|", state.Items.Select(item => item.Entity + item.Name + item.Step + item.Process + item.Reheat + item.Prototype + item.Result + RequirementSignature(item.Requirements))) + state.Busy;
        if (_itemSignature == itemSignature) return;
        _itemSignature = itemSignature; _items.RemoveAllChildren();
        foreach (var item in state.Items)
        {
            var panel = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = Color.FromHex("#19262D") } };
            var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8), SeparationOverride = 4 };
            var header = new BoxContainer { SeparationOverride = 10 };
            if (_entities.TryGetEntity(item.Entity, out var actual))
            {
                var view = new SpriteView { SetSize = new Vector2(40, 40), Stretch = SpriteView.StretchMode.Fit,
                    MouseFilter = Control.MouseFilterMode.Ignore, VerticalAlignment = VAlignment.Top };
                view.SetEntity(actual.Value);
                header.AddChild(view);
            }
            else if (item.Prototype != "") header.AddChild(Preview(item.Prototype, 40));
            var description = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
            var name = new RichTextLabel(); SetText(name, item.Name); description.AddChild(name);
            if (item.Step != "") { var step = new RichTextLabel(); SetText(step, item.Step); description.AddChild(step); }
            header.AddChild(description);
            if (item.Result != "")
            {
                var outcome = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 3 };
                outcome.AddChild(Preview(item.Result, 40));
                outcome.AddChild(new Label { Text = Loc.GetString("artisan-ui-result"), StyleClasses = { "LabelSubText" } });
                header.AddChild(outcome);
            }
            row.AddChild(header);
            AddRequirements(row, item.Requirements);
            var buttons = new BoxContainer { SeparationOverride = 6 };
            if (item.Process) Add(ArtisanAction.Process, "artisan-ui-process");
            if (item.Reheat) Add(ArtisanAction.Reheat, "artisan-ui-reheat");
            if (state.Kind == ArtisanStationKind.Bottler) Add(ArtisanAction.Source, "artisan-source");
            Add(ArtisanAction.Eject, item.Hot ? "artisan-ui-tongs" : "artisan-ui-eject");
            row.AddChild(buttons); panel.AddChild(row); _items.AddChild(panel);
            void Add(ArtisanAction action, string text)
            {
                var button = new Button { Text = Loc.GetString(text), Disabled = state.Busy, HorizontalExpand = true };
                button.OnPressed += _ => Action?.Invoke(new(action, item: item.Entity)); buttons.AddChild(button);
            }
        }
    }

    private static void SetText(RichTextLabel label, string text)
    {
        var message = new FormattedMessage(); message.AddText(text); label.SetMessage(message);
    }

    private static string RequirementSignature(List<ArtisanRequirementEntry> requirements)
        => string.Join(";", requirements.Select(requirement => requirement.Text + requirement.Icon + requirement.Satisfied));

    private static EntityPrototypeView Preview(string prototype, int size)
    {
        var view = new EntityPrototypeView { SetSize = new Vector2(size, size), Stretch = SpriteView.StretchMode.Fit,
            MouseFilter = Control.MouseFilterMode.Ignore, VerticalAlignment = VAlignment.Top };
        view.SetPrototype(prototype);
        return view;
    }

    private static void AddRequirements(BoxContainer target, List<ArtisanRequirementEntry> requirements)
    {
        foreach (var requirement in requirements)
        {
            var row = new BoxContainer { SeparationOverride = 7 };
            if (requirement.Icon is { } icon) row.AddChild(Preview(icon, 24));
            var text = new RichTextLabel { HorizontalExpand = true, VerticalAlignment = VAlignment.Center,
                Modulate = Color.FromHex(requirement.Satisfied switch { true => "#91CFA7", false => "#E7BB79", _ => "#9BC8DF" }) };
            SetText(text, requirement.Text);
            row.AddChild(text);
            target.AddChild(row);
        }
    }
}
