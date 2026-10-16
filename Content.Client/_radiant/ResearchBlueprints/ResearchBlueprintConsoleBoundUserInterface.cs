using System.Linq;
using System.Numerics;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._radiant.ResearchBlueprints;

public sealed class ResearchBlueprintConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private ResearchBlueprintConsoleWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredRight<ResearchBlueprintConsoleWindow>();
        _window.Roll.OnPressed += _ => SendMessage(new ResearchBlueprintRollMessage());
        _window.Servers.OnPressed += _ => SendMessage(new ConsoleServerSelectionMessage());
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ResearchBlueprintConsoleState data) _window?.Update(data);
    }
}

public sealed partial class ResearchBlueprintConsoleWindow : DefaultWindow
{
    public readonly Button Roll = new();
    public readonly Button Servers = new() { Text = Loc.GetString("research-console-menu-server-selection-button") };
    private readonly RichTextLabel _points = ResearchBlueprintUi.Text(string.Empty);
    private readonly RichTextLabel _connection = ResearchBlueprintUi.Text(string.Empty);
    private readonly Label _historyHeading = new() { StyleClasses = { "LabelHeading" } };
    private readonly ProgressBar _funding = new()
    {
        Name = "BlueprintResearchFunding", MinValue = 0, MaxValue = 1, SetHeight = 6,
        BackgroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = ResearchBlueprintUi.Background },
    };
    private readonly BoxContainer _history = new() { Name = "BlueprintHistory", Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 2, HorizontalExpand = true };
    private readonly IPrototypeManager _prototypes;
    private readonly SharedLatheSystem _lathe;
    private string? _historyKey;

    public ResearchBlueprintConsoleWindow()
    {
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _lathe = IoCManager.Resolve<IEntityManager>().System<SharedLatheSystem>();
        Title = Loc.GetString("research-console-menu-title");
        MinSize = new Vector2(520, 400);
        SetSize = new Vector2(700, 560);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, Margin = new Thickness(8), SeparationOverride = 8 };
        var status = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
        status.AddChild(_points);
        status.AddChild(_connection);
        var toolbar = new BoxContainer { SeparationOverride = 10, Children = { Servers, ResearchBlueprintUi.Icon("ResearchAndDevelopmentServer", 40), status } };
        Servers.VerticalAlignment = VAlignment.Center;
        root.AddChild(toolbar);
        root.AddChild(_funding);
        Roll.MinHeight = 42;
        Roll.AddStyleClass("ButtonColorGreen");
        Roll.Disabled = true;
        root.AddChild(Roll);
        root.AddChild(ResearchBlueprintUi.Heading(_historyHeading, "ResearchRandomBlueprint", ResearchBlueprintUi.Accent));
        root.AddChild(new ScrollContainer { VerticalExpand = true, HorizontalExpand = true, HScrollEnabled = false, Children = { _history } });
        Contents.AddChild(root);
    }

    public void Update(ResearchBlueprintConsoleState state)
    {
        _points.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("research-blueprint-points", ("points", state.Points))));
        _connection.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(state.Connected ? "research-blueprint-connected" : "research-blueprint-disconnected")));
        _connection.ModulateSelfOverride = state.Connected ? ResearchBlueprintUi.Success : ResearchBlueprintUi.Warning;
        Roll.Text = Loc.GetString("research-blueprint-roll", ("cost", state.Cost));
        Roll.Disabled = !state.Connected || state.Cost <= 0 || state.Points < state.Cost;
        _funding.Value = state.Connected ? Math.Clamp(state.Points / (float) Math.Max(1, state.Cost), 0, 1) : 0;
        _funding.ToolTip = Roll.Text;
        _funding.ForegroundStyleBoxOverride = new StyleBoxFlat { BackgroundColor = Roll.Disabled ? ResearchBlueprintUi.Warning : ResearchBlueprintUi.Accent };
        _historyHeading.Text = Loc.GetString("research-blueprint-history-count", ("count", state.History.Count));
        var key = string.Join('|', state.History.Select(p => p.InstanceId));
        if (_historyKey == key) return;
        _historyKey = key;
        _history.RemoveAllChildren();
        if (state.History.Count == 0)
            _history.AddChild(new BoxContainer
            {
                SeparationOverride = 12, Margin = new Thickness(12),
                Children = { ResearchBlueprintUi.Icon("ResearchRandomBlueprint", 48), ResearchBlueprintUi.Text(Loc.GetString("research-blueprint-history-empty"), ResearchBlueprintUi.Muted) },
            });
        var index = 0;
        foreach (var result in state.History)
        {
            if (!_prototypes.TryIndex(result.Technology, out var technology)) continue;
            var recipes = result.Recipes.Where(r => _prototypes.HasIndex(r)).Select(r => _prototypes.Index(r)).ToArray();
            if (recipes.Length == 0) continue;
            var discipline = _prototypes.Index(technology.Discipline);
            var row = new BoxContainer { Margin = new Thickness(10), SeparationOverride = 10 };
            row.AddChild(ResearchBlueprintUi.Icon(recipes[0].Result ?? technology.EntityIcon, 44));
            var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 4 };
            if (index == 0) text.AddChild(new Label { Text = Loc.GetString("research-blueprint-latest"), Modulate = ResearchBlueprintUi.Success, StyleClasses = { "LabelSubText" } });
            text.AddChild(ResearchBlueprintUi.Text(Loc.GetString("research-blueprint-paper-name",
                ("item", string.Join(", ", recipes.Select(recipe => _lathe.GetRecipeName(recipe)))))));
            text.AddChild(ResearchBlueprintUi.Text(Loc.GetString(result.RareLoc), ResearchBlueprintUi.Rarity(result.RareLoc)));
            text.AddChild(ResearchBlueprintUi.Text(Loc.GetString(discipline.Name), discipline.Color));
            row.AddChild(text);
            _history.AddChild(new PanelContainer
            {
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Color.InterpolateBetween(ResearchBlueprintUi.Background, discipline.Color, index == 0 ? 0.12f : 0.05f),
                    BorderColor = discipline.Color, BorderThickness = new Thickness(3, 0, 0, 0),
                },
                Children = { row },
            });
            index++;
        }
    }
}
