using Content.Client.Storage.Visualizers;
using Content.Shared._radiant.SmartFridge;
using Content.Shared.Power;
using Robust.Client.GameObjects;

namespace Content.Client._radiant.SmartFridge;

public sealed class SmartFridgeFillVisualizerSystem : VisualizerSystem<SmartFridgeFillVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, SmartFridgeFillVisualsComponent component,
        ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        AppearanceSystem.TryGetData<int>(uid, SmartFridgeFillVisuals.Level, out var level, args.Component);
        AppearanceSystem.TryGetData<bool>(uid, PowerDeviceVisuals.Powered, out var powered, args.Component);
        var state = !powered ? "off" : level <= 0 ? "normal" : $"fill{level}";
        SpriteSystem.LayerSetRsiState((uid, args.Sprite), StorageVisualLayers.Base, state);
    }
}
