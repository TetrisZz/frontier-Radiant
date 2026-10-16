using System.Linq;
using System.Numerics;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Lathe;
using Content.Shared.Materials;
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

public sealed class BlueprintLibraryBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private BlueprintLibraryWindow? _window;
    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredRight<BlueprintLibraryWindow>();
        _window.OnSelect += (recipe, blueprint) => SendMessage(new BlueprintLibrarySelectMessage(recipe, blueprint));
        _window.OnEject += blueprint => SendMessage(new BlueprintLibraryEjectMessage(blueprint));
        _window.Servers.OnPressed += _ => SendMessage(new ConsoleServerSelectionMessage());
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is BlueprintLibraryState data) _window?.Update(data);
    }
}

public sealed partial class BlueprintLibraryWindow : DefaultWindow
{
    public Action<string, NetEntity>? OnSelect;
    public Action<NetEntity>? OnEject;
    public readonly Button Servers = new() { Text = Loc.GetString("research-console-menu-server-selection-button") };
    private readonly IPrototypeManager _prototypes;
    private readonly SharedLatheSystem _lathe;
    private readonly SharedMaterialStorageSystem _materialStorage;
    private readonly BoxContainer _items = Column("BlueprintItems");
    private readonly BoxContainer _variants = Column("BlueprintVariants");
    private readonly BoxContainer _details = Column("BlueprintDetails");
    private readonly RichTextLabel _connection = Text(string.Empty);
    private readonly BoxContainer _actions = new() { Name = "BlueprintActions", HorizontalExpand = true, SeparationOverride = 8 };
    private readonly Label _itemsHeading = new();
    private readonly Label _variantsHeading = new();
    private readonly LineEdit _search = new() { PlaceHolder = Loc.GetString("research-blueprint-search") };
    private BlueprintLibraryState _state = new(new(), new(), false);
    private string? _item;
    private NetEntity? _variant;
    public static readonly Color ModifiedColor = Color.FromHex("#E6B36B");
    public static readonly Color AdditionalMaterialColor = Color.FromHex("#33CCFF");

    private static BoxContainer Column(string? name = null) => new() { Name = name, Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 6 };
    private static ScrollContainer Scroll(BoxContainer content) => new() { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true, Children = { content } };
    private static PanelContainer ListPanel(BoxContainer content) => new()
    {
        VerticalExpand = true, HorizontalExpand = true,
        PanelOverride = new StyleBoxFlat { BackgroundColor = ResearchBlueprintUi.Background, BorderColor = ResearchBlueprintUi.Border, BorderThickness = new Thickness(1) },
        Children = { Scroll(content) },
    };

    private static RichTextLabel Text(string text, Color? color = null)
    {
        return ResearchBlueprintUi.Text(text, color);
    }

    private static EntityPrototypeView Icon(EntProtoId prototype, int size)
    {
        return ResearchBlueprintUi.Icon(prototype, size);
    }

    private string Machine(BlueprintLibraryEntry entry)
    {
        var machines = entry.Modifiers.GetLathes().Distinct().Where(machine => _prototypes.HasIndex(machine))
            .Select(machine => _prototypes.Index(machine).Name).ToArray();
        return machines.Length == 0 ? Loc.GetString("research-blueprint-lathe-legacy")
            : Loc.GetString(machines.Length == 1 ? "research-blueprint-lathe" : "research-blueprint-lathes",
                ("lathe", string.Join(", ", machines)));
    }

    private string Time(ProtoId<LatheRecipePrototype> recipe, BlueprintLibraryEntry entry)
        => Loc.GetString("research-blueprint-time", ("seconds", Math.Round(
            BlueprintModifiers.Time(_prototypes.Index(recipe), entry.Modifiers, 1).TotalSeconds * SharedLatheSystem.ProductionTimeMultiplier, 1)));

    private void AddMaterials(BoxContainer container, ProtoId<LatheRecipePrototype> recipe, BlueprintLibraryEntry entry)
    {
        var prototype = _prototypes.Index(recipe);
        foreach (var (material, amount) in BlueprintModifiers.Materials(prototype, entry.Modifiers, 1).OrderBy(p => p.Key.Id))
        {
            var color = !prototype.Materials.ContainsKey(material) ? AdditionalMaterialColor
                : amount != prototype.Materials[material] ? ModifiedColor : Color.White;
            var label = Text(FormatMaterial(material, amount), color);
            label.Name = "BlueprintMaterial";
            var row = new BoxContainer { SeparationOverride = 8, MinHeight = 24 };
            if (_prototypes.Index(material).StackEntity is { } stack && _prototypes.HasIndex(stack)) row.AddChild(Icon(stack, 20));
            row.AddChild(label);
            container.AddChild(row);
        }
    }

    private Color MachineColor(BlueprintLibraryEntry entry)
        => entry.Modifiers.Technology is { } technology && _prototypes.TryIndex(technology, out var original) &&
           entry.Modifiers.Lathe != original.Lathe ? ModifiedColor : Color.White;

    private RichTextLabel TimeLabel(ProtoId<LatheRecipePrototype> recipe, BlueprintLibraryEntry entry)
        => NamedText(Time(recipe, entry), "BlueprintTime", entry.Modifiers.TimeMultiplier == 1 ? Color.White : ModifiedColor);

    private static RichTextLabel NamedText(string text, string name, Color color)
    {
        var label = Text(text, color);
        label.Name = name;
        return label;
    }

    private string FormatMaterial(ProtoId<MaterialPrototype> materialId, int rawAmount)
    {
        var material = _prototypes.Index(materialId);
        var amount = rawAmount / (double) Math.Max(1, _materialStorage.GetSheetVolume(material));
        var quantity = Loc.GetString("lathe-menu-material-amount", ("amount", amount), ("unit", Loc.GetString(material.Unit)));
        return Loc.GetString("research-blueprint-material", ("material", Loc.GetString(material.Name)), ("amount", quantity));
    }

    public BlueprintLibraryWindow()
    {
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _lathe = IoCManager.Resolve<IEntityManager>().System<SharedLatheSystem>();
        _materialStorage = IoCManager.Resolve<IEntityManager>().System<SharedMaterialStorageSystem>();
        Title = Loc.GetString("research-blueprint-library-title");
        MinSize = new Vector2(750, 480);
        SetSize = new Vector2(950, 830);
        var root = Column();
        root.Margin = new Thickness(8);
        var toolbar = new BoxContainer { SeparationOverride = 8, Children = { Servers, Icon("ResearchAndDevelopmentServer", 28), _connection } };
        Servers.VerticalAlignment = VAlignment.Center;
        root.AddChild(toolbar);
        var columns = new BlueprintLibraryColumns { Name = "BlueprintColumns", VerticalExpand = true, HorizontalExpand = true };
        var itemsPane = Column("BlueprintItemsPane");
        _itemsHeading.AddStyleClass("LabelHeading");
        itemsPane.AddChild(ResearchBlueprintUi.Heading(_itemsHeading, "ClothingBackpackHolding", ResearchBlueprintUi.Accent));
        itemsPane.AddChild(_search);
        itemsPane.AddChild(ListPanel(_items));
        columns.AddChild(itemsPane);
        var right = new BlueprintLibraryColumns(vertical: true, firstContent: _variants) { Name = "BlueprintRightPane", HorizontalExpand = true, VerticalExpand = true };
        var variantsPane = Column("BlueprintVariantsPane");
        _variantsHeading.AddStyleClass("LabelHeading");
        variantsPane.AddChild(ResearchBlueprintUi.Heading(_variantsHeading, "ResearchRandomBlueprint", Color.FromHex("#D9B875")));
        variantsPane.AddChild(ListPanel(_variants));
        right.AddChild(variantsPane);
        var craftPane = Column("BlueprintCraftPane");
        craftPane.AddChild(ResearchBlueprintUi.Heading(new Label { Text = Loc.GetString("research-blueprint-details"), StyleClasses = { "LabelHeading" } },
            "EngineeringTechFab", ResearchBlueprintUi.Accent));
        craftPane.AddChild(ListPanel(_details));
        craftPane.AddChild(_actions);
        right.AddChild(craftPane);
        columns.AddChild(right);
        root.AddChild(columns);
        Contents.AddChild(root);
        _search.OnTextChanged += _ => Render();
    }

    public void Update(BlueprintLibraryState state)
    {
        _state = state;
        _connection.ModulateSelfOverride = state.Connected ? ResearchBlueprintUi.Success : ResearchBlueprintUi.Warning;
        _connection.SetMessage(FormattedMessage.FromUnformatted(state.Connected
            ? Loc.GetString("research-blueprint-server", ("name", state.ServerName))
            : Loc.GetString("research-blueprint-disconnected")));
        Render();
    }

    private string Key(ProtoId<LatheRecipePrototype> recipe) => _prototypes.Index(recipe).Result?.Id ?? recipe.Id;
    private void Render()
    {
        _items.RemoveAllChildren();
        _variants.RemoveAllChildren();
        _details.RemoveAllChildren();
        _actions.RemoveAllChildren();
        var recipes = _state.Blueprints.SelectMany(b => b.Recipes).Distinct().Where(r => _prototypes.HasIndex(r))
            .GroupBy(Key).Where(g => _lathe.GetRecipeName(g.First()).Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(g => _lathe.GetRecipeName(g.First())).ToArray();
        if (recipes.All(g => g.Key != _item)) _item = recipes.FirstOrDefault()?.Key;
        var visibleCount = 0;
        foreach (var group in recipes)
        {
            var recipe = group.First();
            var name = _lathe.GetRecipeName(recipe);
            visibleCount++;
            var button = new ResearchBlueprintRowButton(ResearchBlueprintUi.Accent) { ToolTip = name, ToggleMode = true, Pressed = group.Key == _item, MinHeight = 52 };
            var row = new BoxContainer { SeparationOverride = 8, MouseFilter = MouseFilterMode.Ignore };
            if (_prototypes.Index(recipe).Result is { } rowResult) row.AddChild(Icon(rowResult, 32));
            row.AddChild(Text(name));
            button.AddChild(row);
            button.OnPressed += _ => { _item = group.Key; _variant = null; Render(); };
            _items.AddChild(button);
        }
        _itemsHeading.Text = Loc.GetString("research-blueprint-items-count", ("count", visibleCount));
        var variants = _state.Blueprints.Where(b => b.Recipes.Any(r => _prototypes.HasIndex(r) && Key(r) == _item)).ToArray();
        if (!variants.Any(b => b.Entity == _variant))
            _variant = variants.FirstOrDefault(b => b.Recipes.Any(r => _prototypes.HasIndex(r) && Key(r) == _item &&
                _state.Selected.GetValueOrDefault(r) == b.Modifiers.InstanceId))?.Entity ?? variants.FirstOrDefault()?.Entity;
        _variantsHeading.Text = Loc.GetString("research-blueprint-variants-count", ("count", variants.Length));
        for (var index = 0; index < variants.Length; index++)
        {
            var entry = variants[index];
            var variantName = Loc.GetString("research-blueprint-variant-title", ("number", index + 1));
            var recipe = entry.Recipes.First(r => _prototypes.HasIndex(r) && Key(r) == _item);
            var variantActive = _state.Selected.GetValueOrDefault(recipe) == entry.Modifiers.InstanceId;
            var rarityColor = ResearchBlueprintUi.Rarity(entry.Modifiers.RareLoc);
            var button = new ResearchBlueprintRowButton(variantActive ? ResearchBlueprintUi.Success : rarityColor)
            {
                ToolTip = variantName + "\n" + Time(recipe, entry) + "\n" +
                    string.Join("\n", BlueprintModifiers.Materials(_prototypes.Index(recipe), entry.Modifiers, 1).OrderBy(p => p.Key.Id)
                        .Select(p => FormatMaterial(p.Key, p.Value))) + "\n" + Machine(entry),
                ToggleMode = true, Pressed = entry.Entity == _variant,
                MinHeight = 112,
            };
            var summary = Column();
            summary.SeparationOverride = 3;
            summary.MouseFilter = MouseFilterMode.Ignore;
            summary.AddChild(Text(variantName + (variantActive ? " / " + Loc.GetString("research-blueprint-active") : string.Empty),
                variantActive ? ResearchBlueprintUi.Success : Color.White));
            summary.AddChild(NamedText(Loc.GetString(entry.Modifiers.RareLoc), "BlueprintRarity", rarityColor));
            summary.AddChild(TimeLabel(recipe, entry));
            summary.AddChild(NamedText(Loc.GetString("research-blueprint-resource-factor", ("factor", entry.Modifiers.MaterialMultiplier),
                ("count", entry.Modifiers.ExtraMaterials.Count)), "BlueprintResourceFactor", entry.Modifiers.MaterialMultiplier != 1 ? ModifiedColor
                    : entry.Modifiers.ExtraMaterials.Count > 0 ? AdditionalMaterialColor : ResearchBlueprintUi.Muted));
            button.AddChild(summary);
            button.OnPressed += _ => { _variant = entry.Entity; Render(); };
            _variants.AddChild(button);
        }
        var chosen = variants.FirstOrDefault(b => b.Entity == _variant);
        if (chosen == null)
        {
            _details.AddChild(Text(Loc.GetString("research-blueprint-library-empty")));
            return;
        }
        var selectedRecipe = chosen.Recipes.First(r => _prototypes.HasIndex(r) && Key(r) == _item);
        var prototype = _prototypes.Index(selectedRecipe);
        var header = new BoxContainer { SeparationOverride = 8 };
        if (prototype.Result is { } result)
            header.AddChild(Icon(result, 32));
        header.AddChild(Text(_lathe.GetRecipeName(prototype)));
        _details.AddChild(header);
        var machineRow = new BoxContainer { SeparationOverride = 8 };
        if (chosen.Modifiers.Lathe is { } machine && _prototypes.HasIndex(machine)) machineRow.AddChild(Icon(machine, 24));
        machineRow.AddChild(NamedText(Machine(chosen), "BlueprintMachine", MachineColor(chosen)));
        _details.AddChild(machineRow);
        _details.AddChild(TimeLabel(selectedRecipe, chosen));
        _details.AddChild(new Label { Text = Loc.GetString("research-blueprint-resources"), StyleClasses = { "LabelSubText" } });
        AddMaterials(_details, selectedRecipe, chosen);
        var destinations = chosen.Destinations.GetValueOrDefault(selectedRecipe) ?? new();
        if (destinations.Count == 0)
            _details.AddChild(Text(Loc.GetString(_state.Connected ? "research-blueprint-no-destinations" : "research-blueprint-disconnected"), Color.FromHex("#E8BE7D")));
        else
        {
            var recipients = Text(Loc.GetString("research-blueprint-connected-machines", ("count", destinations.Count)), ResearchBlueprintUi.Muted);
            recipients.ToolTip = string.Join("\n", destinations.GroupBy(d => d.Name).OrderBy(g => g.Key)
                .Select(g => Loc.GetString("research-blueprint-machine-count", ("name", g.Key), ("count", g.Count()))));
            _details.AddChild(recipients);
        }
        var active = _state.Selected.GetValueOrDefault(selectedRecipe) == chosen.Modifiers.InstanceId;
        var select = new Button { Name = "BlueprintAssign", Text = Loc.GetString(active ? "research-blueprint-active" : "research-blueprint-select"), Disabled = !_state.Connected || active, MinHeight = 36, HorizontalExpand = true };
        select.AddStyleClass("ButtonColorGreen");
        select.OnPressed += _ => OnSelect?.Invoke(selectedRecipe.Id, chosen.Entity);
        _actions.AddChild(select);
        var eject = new Button { Text = Loc.GetString("research-blueprint-eject"), MinHeight = 36, HorizontalExpand = true };
        eject.OnPressed += _ => OnEject?.Invoke(chosen.Entity);
        _actions.AddChild(eject);
    }
}

// Measure each pane at its allocated width so wrapped rows cannot push other panes off-screen.
internal sealed class BlueprintLibraryColumns(bool vertical = false, Control? firstContent = null) : Container
{
    private const float Gap = 12;
    private static readonly float[] Weights = [0.40f, 0.60f];

    private float FirstHeight(float height)
    {
        var frame = GetChild(0).GetChild(0).DesiredSize.Y + 12;
        var firstRow = firstContent is { ChildCount: > 0 } ? firstContent.GetChild(0).DesiredSize.Y : 0;
        var desired = Math.Max(160, frame + (firstContent?.DesiredSize.Y ?? 0));
        var limit = Math.Max(height * 0.45f, frame + firstRow);
        return Math.Min(Math.Max(0, height - 120), Math.Min(desired, limit));
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var width = float.IsFinite(availableSize.X) ? availableSize.X : 1064;
        var height = float.IsFinite(availableSize.Y) ? availableSize.Y : 480;
        if (vertical)
        {
            GetChild(0).Measure(new Vector2(width, height));
            var top = FirstHeight(height);
            GetChild(1).Measure(new Vector2(width, Math.Max(0, height - top - Gap)));
            return new Vector2(width, height);
        }
        var usable = Math.Max(0, width - Gap * (ChildCount - 1));
        for (var i = 0; i < ChildCount; i++)
        {
            GetChild(i).Measure(new Vector2(usable * Weights[i], height));
        }
        return new Vector2(width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        if (vertical)
        {
            var top = FirstHeight(finalSize.Y);
            GetChild(0).Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(finalSize.X, top)));
            GetChild(1).Arrange(UIBox2.FromDimensions(new Vector2(0, top + Gap), new Vector2(finalSize.X, Math.Max(0, finalSize.Y - top - Gap))));
            return finalSize;
        }
        var usable = Math.Max(0, finalSize.X - Gap * (ChildCount - 1));
        var offset = 0f;
        for (var i = 0; i < ChildCount; i++)
        {
            var size = usable * Weights[i];
            GetChild(i).Arrange(UIBox2.FromDimensions(new Vector2(offset, 0), new Vector2(size, finalSize.Y)));
            offset += size + Gap;
        }
        return finalSize;
    }
}
