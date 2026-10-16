using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Client.Interaction.Panel.Ui;

[DataDefinition]
public sealed partial class InteractionPanelPreferences
{
    [DataField] public Vector2 WindowSize = new(660, 720);
    [DataField] public float Scroll;
    [DataField] public List<string> Expanded = new();

    public void Sanitize()
    {
        Expanded ??= new List<string>();
        WindowSize = new Vector2(
            float.IsFinite(WindowSize.X) ? Math.Clamp(WindowSize.X, 580, 1600) : 660,
            float.IsFinite(WindowSize.Y) ? Math.Clamp(WindowSize.Y, 560, 1600) : 720);
        Scroll = float.IsFinite(Scroll) ? Math.Clamp(Scroll, 0, 100000) : 0;
    }
}
