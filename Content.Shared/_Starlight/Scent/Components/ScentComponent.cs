using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Scent.Components;

/// <summary>
/// Gives an entity a persistent scent identity and periodically emitted trail.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ScentComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? ScentId;

    [DataField]
    public TimeSpan EmitInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan NextEmit;
}
