using System.Linq;
using Content.Client.UserInterface.Controls;
using Content.Shared._radiant.Social;
using Content.Shared.Humanoid.Markings;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._radiant.Social;

public sealed class HairAccessoryBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private HairAccessoryWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<HairAccessoryWindow>();
        _window.SelectStyle += style => SendMessage(new HairAccessorySelectMessage(style));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HairAccessoryUiState data)
            _window?.UpdateState(data);
    }
}

public sealed class HairAccessoryWindow : FancyWindow
{
    public event Action<string>? SelectStyle;
    private readonly LineEdit _search = new() { PlaceHolder = Loc.GetString("markings-search"), HorizontalExpand = true };
    private readonly ItemList _styles = new() { VerticalExpand = true, HorizontalExpand = true };
    private HairAccessoryUiState? _state;
    private bool _updating;

    public HairAccessoryWindow()
    {
        Title = Loc.GetString("hair-accessory-title");
        SetSize = new(360, 460);
        MinSize = new(300, 300);
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 6, Margin = new Thickness(8) };
        ContentsContainer.AddChild(column);
        column.AddChild(_search);
        column.AddChild(_styles);
        _search.OnTextChanged += _ => Populate();
        _styles.OnItemSelected += args =>
        {
            if (!_updating && _styles[args.ItemIndex].Metadata is string style)
                SelectStyle?.Invoke(style);
        };
    }

    public void UpdateState(HairAccessoryUiState state)
    {
        _state = state;
        Populate();
    }

    private void Populate()
    {
        _updating = true;
        _styles.Clear();
        if (_state != null)
        {
            var markings = IoCManager.Resolve<MarkingManager>();
            var sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();
            foreach (var id in _state.Styles.OrderBy(id => Loc.GetString("marking-" + id)))
            {
                var name = Loc.GetString("marking-" + id);
                if (!name.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase) ||
                    !markings.Markings.TryGetValue(id, out var marking) || marking.Sprites.Count == 0)
                    continue;
                var item = _styles.AddItem(name, sprites.Frame0(marking.Sprites[0]));
                item.Metadata = id;
                item.Selected = id == _state.Selected;
            }
        }
        _updating = false;
    }
}
