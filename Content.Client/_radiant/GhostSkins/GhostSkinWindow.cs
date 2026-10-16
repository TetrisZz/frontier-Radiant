using System.Linq;
using System.Numerics;
using Content.Shared._radiant.GhostSkins;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._radiant.GhostSkins;

public sealed class GhostSkinWindow : DefaultWindow
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    private readonly Label _status = new() { HorizontalExpand = true, ClipText = true };
    private readonly BoxContainer _choices = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
    private readonly Dictionary<string, Button> _buttons = new();
    public event Action<string>? SelectRequested;

    public GhostSkinWindow()
    {
        IoCManager.InjectDependencies(this);
        Title = Loc.GetString("ghost-skins-title");
        MinSize = new Vector2(540, 400);
        SetSize = new Vector2(580, 440);
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        Contents.AddChild(body);
        body.AddChild(_status);
        var scroll = new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, HorizontalExpand = true };
        scroll.AddChild(_choices);
        body.AddChild(scroll);
        SetLoading();
    }

    public void SetLoading()
    {
        _status.Text = Loc.GetString("ghost-skins-loading");
        foreach (var button in _buttons.Values)
            button.Disabled = true;
    }

    public void UpdateState(GhostSkinStateEvent state)
    {
        _choices.DisposeAllChildren();
        _buttons.Clear();
        _status.Text = state.Message != null ? Loc.GetString(state.Message)
            : state.Saving ? Loc.GetString("ghost-skins-saving")
            : _prototypes.TryIndex<GhostSkinPrototype>(state.Skin, out var selected)
                ? Loc.GetString("ghost-skins-current", ("name", Loc.GetString(selected.Name))) : "";
        _status.ToolTip = _status.Text;
        var skins = _prototypes.EnumeratePrototypes<GhostSkinPrototype>().OrderBy(skin => skin.Order).ThenBy(skin => skin.ID).ToArray();
        foreach (var supporterOnly in new[] { false, true })
        {
            _choices.AddChild(new Label
            {
                Text = Loc.GetString(supporterOnly ? "ghost-skins-supporter" : "ghost-skins-free"),
                StyleClasses = { "LabelHeading" },
            });
            var grid = new GridContainer { Columns = 4, HorizontalExpand = true };
            _choices.AddChild(grid);
            foreach (var skin in skins.Where(skin => skin.SupporterOnly == supporterOnly))
            {
                var allowed = GhostSkinRules.CanSelect(skin, state.HasSupporterAccess);
                var button = new Button
                {
                    Name = skin.ID,
                    ToggleMode = true,
                    Pressed = skin.ID == state.Skin,
                    Disabled = state.Saving || !allowed,
                    MinSize = new Vector2(124, 124),
                    ToolTip = allowed ? Loc.GetString(skin.Name) : Loc.GetString("ghost-skins-supporter-required"),
                };
                var contents = new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    SeparationOverride = 4,
                    MouseFilter = MouseFilterMode.Ignore,
                };
                contents.AddChild(new TextureRect
                {
                    Texture = _entities.System<SpriteSystem>().Frame0(skin.Sprite),
                    SetSize = new Vector2(72, 72),
                    HorizontalAlignment = HAlignment.Center,
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    Modulate = skin.Color,
                    MouseFilter = MouseFilterMode.Ignore,
                });
                contents.AddChild(new Label
                {
                    Text = Loc.GetString(skin.Name),
                    HorizontalAlignment = HAlignment.Center,
                    MaxWidth = 116,
                    ClipText = true,
                    MouseFilter = MouseFilterMode.Ignore,
                });
                if (!allowed)
                    contents.AddChild(new Label
                    {
                        Text = Loc.GetString("ghost-skins-locked"),
                        HorizontalAlignment = HAlignment.Center,
                        MouseFilter = MouseFilterMode.Ignore,
                    });
                button.AddChild(contents);
                grid.AddChild(button);
                _buttons.Add(skin.ID, button);
                button.OnPressed += _ =>
                {
                    SetLoading();
                    _status.Text = Loc.GetString("ghost-skins-saving");
                    SelectRequested?.Invoke(skin.ID);
                };
            }
        }
    }
}
