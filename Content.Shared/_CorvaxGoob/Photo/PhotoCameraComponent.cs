using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using System.Numerics;

namespace Content.Shared._CorvaxGoob.Photo;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PhotoCameraComponent : Component
{
    [DataField]
    public Vector2 ViewBox = new Vector2(5, 5);
    [DataField]
    public float MinZoom = 0.2f, MaxZoom = 1f;

    [DataField]
    public SoundSpecifier PhotoSound = new SoundPathSpecifier("/Audio/_CorvaxGoob/Effects/photo_shoot.ogg");
    [DataField]
    public SoundSpecifier ErrorSound = new SoundPathSpecifier("/Audio/Machines/airlock_deny.ogg");

    [DataField]
    public string CardPrototype = "PhotoCard";
    [DataField]
    public string CardMaterial = "Paper";
    [DataField]
    public int CardCost = 100;

    [ViewVariables]
    public EntityUid? User;

    // Radiant Sector: the selected view mode belongs to the camera item, not to its UI window.
    [DataField, AutoNetworkedField]
    public bool SelfieMode;
}

[Serializable, NetSerializable]
public sealed class PhotoCameraUiState : BoundUserInterfaceState
{
    public NetEntity CameraEntity { get; }

    public bool HasPaper { get; }

    public bool SelfieMode { get; }

    public PhotoCameraUiState(NetEntity cameraEntity, bool hasPaper, bool selfieMode)
    {
        CameraEntity = cameraEntity;
        HasPaper = hasPaper;
        SelfieMode = selfieMode;
    }
}

[Serializable, NetSerializable]
public enum PhotoCameraUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public sealed class PhotoCameraTakeImageMessage : BoundUserInterfaceMessage
{
    public byte[] Data { get; }
    public MapCoordinates PhotoPosition { get; }
    public float Zoom { get; }

    public PhotoCameraTakeImageMessage(byte[] data, MapCoordinates photoPosition, float zoom)
    {
        Data = data;
        PhotoPosition = photoPosition;
        Zoom = zoom;
    }
}
