using Content.Client.Administration.UI.CustomControls;
using Content.Client.UserInterface.Controls;
using Content.Shared._radiant.Fishing.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Fishing;

public sealed class FishScannerBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FishScannerWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<FishScannerWindow>();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is FishScannerUiState data)
            _window?.UpdateState(data);
    }
}

public sealed class FishScannerWindow : FancyWindow
{
    private readonly BoxContainer _results = new()
    {
        Name = "FishScannerResults",
        Orientation = BoxContainer.LayoutOrientation.Vertical,
        SeparationOverride = 6,
        HorizontalExpand = true,
        VerticalAlignment = VAlignment.Top,
    };

    public FishScannerWindow()
    {
        Title = Loc.GetString("fish-scanner-title");
        SetSize = new(420, 380);
        MinSize = new(340, 320);
        var scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(10),
        };
        scroll.AddChild(_results);
        ContentsContainer.AddChild(scroll);
    }

    public void UpdateState(FishScannerUiState state)
    {
        _results.DisposeAllChildren();
        if (string.IsNullOrEmpty(state.Target))
        {
            AddHeading(Loc.GetString("fish-scanner-no-data"));
            return;
        }
        AddHeading(state.Target);
        _results.AddChild(new HSeparator());
        if (state.Aquarium)
        {
            AddField("water-volume", Loc.GetString("fish-scanner-water-units", ("water", state.Water)));
            AddField("food", state.Food.ToString());
            AddField("occupants", $"{state.Fish.Count} / {state.Capacity}");
        }
        foreach (var fish in state.Fish)
        {
            if (state.Aquarium)
            {
                _results.AddChild(new HSeparator { Margin = new Thickness(0, 6, 0, 2) });
                AddHeading(fish.Name);
            }
            AddField("health", Loc.GetString(fish.Health), fish.Health == "fish-scanner-alive"
                ? Color.FromHex("#9DCEBC") : Color.FromHex("#E8AB86"));
            AddField("damage", fish.Damage.ToString("0.#"));
            AddField("environment", Loc.GetString(fish.InWater ? "fish-scanner-water" : "fish-scanner-air"));
            if (!fish.Breedable)
            {
                AddField("breeding", Loc.GetString("fish-scanner-breeding-unavailable"));
                continue;
            }
            AddField("sex", Loc.GetString(fish.Sex));
            AddField("age", Loc.GetString(fish.Juvenile ? "fish-scanner-juvenile" : "fish-scanner-adult"));
            if (fish.Juvenile)
                AddField("growth", $"{fish.Growth}%");
            AddField("tint", fish.Tint.ToHex(), fish.Tint);
        }
        if (state.Fish.Count == 0)
            AddHeading(Loc.GetString("fish-scanner-no-fish"));
    }

    private void AddHeading(string text)
        => _results.AddChild(Text(text, Color.FromHex("#9DCEBC")));

    private void AddField(string key, string value, Color? color = null)
    {
        var row = new BoxContainer { Name = "FishScannerField-" + key, SeparationOverride = 12, HorizontalExpand = true };
        var label = Text(Loc.GetString("fish-scanner-field-" + key), Color.FromHex("#A8B4BC"));
        label.SetWidth = 130;
        label.HorizontalExpand = false;
        row.AddChild(label);
        row.AddChild(Text(value, color ?? Color.FromHex("#E1E7E9")));
        _results.AddChild(row);
    }

    private static RichTextLabel Text(string text, Color color)
    {
        var message = new FormattedMessage();
        message.PushColor(color);
        message.AddText(text);
        message.Pop();
        var label = new RichTextLabel { HorizontalExpand = true, VerticalAlignment = VAlignment.Top };
        label.SetMessage(message);
        return label;
    }
}
