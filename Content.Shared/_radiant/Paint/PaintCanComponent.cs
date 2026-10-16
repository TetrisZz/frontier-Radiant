using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Paint;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PaintCanComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color Color = Color.White;

    [DataField, AutoNetworkedField]
    public int Uses = 40;
}
