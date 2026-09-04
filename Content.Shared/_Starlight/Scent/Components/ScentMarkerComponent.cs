using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Scent.Components;

/// <summary>
/// A visible-to-smellers point in a scent trail.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ScentMarkerComponent : Component
{
    [DataField, AutoNetworkedField]
    public string ScentId = string.Empty;
}
