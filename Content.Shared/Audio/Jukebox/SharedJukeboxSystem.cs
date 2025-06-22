using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization; // Frontier

namespace Content.Shared.Audio.Jukebox;

public abstract class SharedJukeboxSystem : EntitySystem
{
    [Dependency] protected readonly SharedAudioSystem Audio = default!;
    public static float MapToRange( float value, float leftMin, float leftMax, float rightMin, float rightMax ) // Ganimed edit
    {
        return rightMin + ( value - leftMin ) * ( rightMax - rightMin ) / ( leftMax - leftMin );
    }
}

// Frontier: Shuffle & Repeat
[Serializable, NetSerializable]
public sealed class JukeboxInterfaceState(JukeboxPlaybackMode playbackMode) : BoundUserInterfaceState
{
    public JukeboxPlaybackMode PlaybackMode { get; set; } = playbackMode;
}
// End Frontier: Shuffle & Repeat
