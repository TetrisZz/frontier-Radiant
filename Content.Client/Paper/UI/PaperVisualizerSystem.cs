using Robust.Client.GameObjects;
using Robust.Shared.Utility;

using static Content.Shared.Paper.PaperComponent;

namespace Content.Client.Paper.UI;

public sealed class PaperVisualizerSystem : VisualizerSystem<PaperVisualsComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, PaperVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!component.StampRsiCaptured &&
            SpriteSystem.TryGetLayer((uid, args.Sprite), PaperVisualLayers.Stamp, out var stampLayer, false))
        {
            component.OriginalStampRsi = stampLayer.ActualRsi;
            component.StampRsiCaptured = true;
        }

        if (AppearanceSystem.TryGetData<PaperStatus>(uid, PaperVisuals.Status, out var writingStatus, args.Component))
            SpriteSystem.LayerSetVisible((uid, args.Sprite), PaperVisualLayers.Writing, writingStatus == PaperStatus.Written);

        if (AppearanceSystem.TryGetData<string>(uid, PaperVisuals.Stamp, out var stampState, args.Component))
        {
            if (stampState != string.Empty)
            {
                if (AppearanceSystem.TryGetData<string>(uid, PaperVisuals.StampRsi, out var stampRsi, args.Component))
                {
                    if (string.IsNullOrEmpty(stampRsi))
                        SpriteSystem.LayerSetRsi((uid, args.Sprite), PaperVisualLayers.Stamp, component.OriginalStampRsi);
                    else
                        SpriteSystem.LayerSetRsi((uid, args.Sprite), PaperVisualLayers.Stamp, new ResPath(stampRsi));
                }
                SpriteSystem.LayerSetRsiState((uid, args.Sprite), PaperVisualLayers.Stamp, stampState);
                SpriteSystem.LayerSetVisible((uid, args.Sprite), PaperVisualLayers.Stamp, true);
            }
            else
            {
                SpriteSystem.LayerSetVisible((uid, args.Sprite), PaperVisualLayers.Stamp, false);
            }

        }
    }
}

public enum PaperVisualLayers
{
    Stamp,
    Writing
}
