using System.Numerics;
using Content.Client.Humanoid;
using Content.Shared._radiant.Passports;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Passports;

public sealed class ServiceCredentialBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private ServiceCredentialWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = new ServiceCredentialWindow();
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ServiceCredentialUiState credential)
            _window?.Update(credential);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;
        _window?.Dispose();
        _window = null;
    }
}

public sealed class ServiceCredentialWindow : BaseWindow
{
    private static readonly Color Paper = Color.FromHex("#F1E8D7");
    private static readonly Color Ink = Color.FromHex("#302B28");
    private static readonly Color Gold = Color.FromHex("#BA9859");
    private readonly IEntityManager _entities = IoCManager.Resolve<IEntityManager>();
    private readonly IPrototypeManager _prototypes = IoCManager.Resolve<IPrototypeManager>();
    private readonly BoxContainer _content = new() { HorizontalExpand = true, VerticalExpand = true };
    private EntityUid? _portraitDummy;

    public ServiceCredentialWindow()
    {
        MouseFilter = MouseFilterMode.Stop;
        Resizable = false;
        MinSize = new Vector2(780, 460);
        SetSize = new Vector2(880, 480);
        AddChild(_content);
    }

    protected override DragMode GetDragModeFor(Vector2 relativeMousePos)
    {
        // Drag the cover margin without adding a separate window header.
        const float coverMargin = 13;
        return relativeMousePos.X < coverMargin || relativeMousePos.X > Size.X - coverMargin ||
               relativeMousePos.Y < coverMargin || relativeMousePos.Y > Size.Y - coverMargin
            ? DragMode.Move
            : DragMode.None;
    }

    public void Update(ServiceCredentialUiState state)
    {
        ClearPortrait();
        _content.RemoveAllChildren();
        var fleet = state.Service == CredentialService.Fleet;
        var accent = Color.FromHex(fleet ? "#244C74" : "#873B3B");
        var cover = Panel(Color.FromHex(fleet ? "#14283F" : "#442020"), Gold, 3);
        cover.Name = "CredentialCover";
        cover.HorizontalExpand = true;
        cover.VerticalExpand = true;
        _content.AddChild(cover);
        var spread = new BoxContainer
        {
            HorizontalExpand = true, VerticalExpand = true, SeparationOverride = 10, Margin = new Thickness(10)
        };
        cover.AddChild(spread);

        var left = Page(spread);
        Text(left, "service-credential-ministry", accent, true);
        Text(left, fleet ? "service-credential-fleet" : "service-credential-dvb", accent, true);
        left.AddChild(Rule(accent));
        var photo = Panel(Color.FromHex("#BAC4C4"), Gold, 2);
        photo.SetSize = new Vector2(214, 154);
        photo.HorizontalAlignment = HAlignment.Center;
        photo.RectClipContent = true;
        photo.Name = "CredentialPhoto";
        photo.AddChild(CreatePortrait(state));
        left.AddChild(photo);
        Field(left, "service-credential-holder", state.OwnerName, accent);
        left.AddChild(Rule(accent));
        Field(left, "service-credential-number", state.Number, accent);
        InlineField(left, "service-credential-personal-number", state.PersonalNumber);

        var right = Page(spread);
        Text(right, "service-credential-confederation", accent, true);
        Text(right, "service-credential-title", accent, true);
        InlineField(right, "service-credential-number", state.Number);
        right.AddChild(Rule(accent));
        Field(right, "service-credential-job", state.JobName.Length == 0
            ? Loc.GetString("passport-ui-unfilled") : Loc.GetString(state.JobName), accent);
        Field(right, "service-credential-sector", Loc.GetString("service-credential-radiant"), accent);
        Field(right, "service-credential-issued-by", Loc.GetString(fleet
            ? "service-credential-fleet-authority" : "service-credential-dvb-authority"), accent);
        var sealRow = new BoxContainer { HorizontalExpand = true, SeparationOverride = 8 };
        InlineField(right, "service-credential-registration-number", state.RegistrationNumber);
        var seal = new ServiceCredentialSeal(state.Service)
        {
            Name = "CredentialSeal", SetSize = new Vector2(128, 128), MouseFilter = MouseFilterMode.Ignore,
            Visible = !string.IsNullOrWhiteSpace(state.OwnerName) && !string.IsNullOrWhiteSpace(state.Number)
        };
        sealRow.AddChild(seal);
        var signature = Column();
        Text(signature, "service-credential-signature", accent);
        Text(signature, fleet ? "service-credential-fleet-authority" : "service-credential-dvb-authority", Ink);
        signature.AddChild(Rule(accent));
        sealRow.AddChild(signature);
        right.AddChild(sealRow);
        right.AddChild(new Control { VerticalExpand = true });
        Text(right, "service-credential-purpose", Ink);
    }

    private Control CreatePortrait(ServiceCredentialUiState state)
    {
        if (state.Portrait == null || !_prototypes.TryIndex<SpeciesPrototype>(state.Species, out var species))
            return new Control { Name = "EmptyCredentialPhoto", SetSize = new Vector2(210, 150), MouseFilter = MouseFilterMode.Ignore };
        var sex = Enum.TryParse<Sex>(state.Sex, out var parsed) ? parsed : Sex.Unsexed;
        var profile = HumanoidCharacterProfile.DefaultWithSpecies(state.Species).WithSex(sex).WithAge(state.Age)
            .WithCharacterAppearance(new HumanoidCharacterAppearance(state.Portrait));
        _portraitDummy = _entities.SpawnEntity(species.DollPrototype, MapCoordinates.Nullspace);
        _entities.System<HumanoidAppearanceSystem>().LoadProfile(_portraitDummy.Value, profile);
        if (state.Uniform.Length > 0 && _prototypes.HasIndex<EntityPrototype>(state.Uniform))
        {
            var uniform = _entities.SpawnEntity(state.Uniform, MapCoordinates.Nullspace);
            if (!_entities.System<InventorySystem>().TryEquip(_portraitDummy.Value, uniform, "jumpsuit", true, true))
                _entities.DeleteEntity(uniform);
        }
        var view = new SpriteView
        {
            Scale = new Vector2(8, 8), Stretch = SpriteView.StretchMode.None,
            SetSize = new Vector2(320, 320), OverrideDirection = Direction.South
        };
        view.SetEntity(_portraitDummy.Value);
        var crop = new LayoutContainer
        {
            SetSize = new Vector2(210, 150), RectClipContent = true, InheritChildMeasure = false
        };
        LayoutContainer.SetMarginLeft(view, 8);
        LayoutContainer.SetMarginTop(view, 65);
        crop.AddChild(view);
        return crop;
    }

    private static BoxContainer Page(BoxContainer spread)
    {
        var panel = Panel(Paper, Gold, 1);
        panel.HorizontalExpand = true;
        panel.VerticalExpand = true;
        spread.AddChild(panel);
        var scroll = new ScrollContainer { HorizontalExpand = true, VerticalExpand = true, HScrollEnabled = false };
        panel.AddChild(scroll);
        var column = Column();
        column.Margin = new Thickness(18);
        scroll.AddChild(column);
        return column;
    }

    private static BoxContainer Column() => new()
    {
        Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 8
    };

    private static void Text(BoxContainer parent, string key, Color color, bool heading = false)
    {
        var text = new RichTextLabel { HorizontalExpand = true, MaxWidth = 370 };
        if (heading)
            text.AddStyleClass("LabelHeading");
        text.SetMessage(Loc.GetString(key), defaultColor: color);
        parent.AddChild(text);
    }

    private static void Field(BoxContainer parent, string key, string value, Color accent)
    {
        Text(parent, key, accent);
        var text = new RichTextLabel { HorizontalExpand = true, MaxWidth = 370 };
        text.SetMessage(FormattedMessage.FromUnformatted(string.IsNullOrWhiteSpace(value)
            ? Loc.GetString("passport-ui-unfilled") : value), defaultColor: Ink);
        parent.AddChild(text);
    }

    private static void InlineField(BoxContainer parent, string key, string value)
    {
        var text = new RichTextLabel { HorizontalExpand = true, MaxWidth = 370 };
        var shown = string.IsNullOrWhiteSpace(value) ? Loc.GetString("passport-ui-unfilled") : value;
        text.SetMessage(FormattedMessage.FromUnformatted($"{Loc.GetString(key)}: {shown}"), defaultColor: Ink);
        parent.AddChild(text);
    }

    private static PanelContainer Panel(Color background, Color border, int thickness) => new()
    {
        PanelOverride = new StyleBoxFlat { BackgroundColor = background, BorderColor = border, BorderThickness = new Thickness(thickness) }
    };
    private static PanelContainer Rule(Color color)
    {
        var rule = Panel(color, color, 0);
        rule.MinHeight = 1;
        return rule;
    }
    private void ClearPortrait()
    {
        if (_portraitDummy is { } dummy && _entities.EntityExists(dummy))
            _entities.DeleteEntity(dummy);
        _portraitDummy = null;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ClearPortrait();
        base.Dispose(disposing);
    }
}
