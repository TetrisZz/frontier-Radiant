namespace Content.Shared._Starlight.Storage;

/// <summary>
/// Makes an internal storage immediately accessible to its owner and delayed for outsiders.
/// </summary>
[RegisterComponent, Access(typeof(SharedPrivateStorageSystem))]
public sealed partial class PrivateStorageComponent : Component
{
    [DataField]
    public TimeSpan AccessDelay = TimeSpan.FromSeconds(5);

    [DataField]
    public LocId AccessPopup = "action-storage-accessing-outsider";
}
