using Content.Shared._radiant.Addictions;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Client._radiant.Addictions;

public sealed partial class WithdrawalOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    private WithdrawalOverlay _overlay = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    private EntityUid? _heartbeat;
    private float _soundTimer;

    public override void Initialize()
    {
        _overlay = new WithdrawalOverlay();
        _overlays.AddOverlay(_overlay);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnDetached);
    }

    private void OnDetached(LocalPlayerDetachedEvent args)
    {
        _overlay.Intensity = 0;
        StopHeartbeat();
    }

    private void StopHeartbeat()
    {
        _audio.Stop(_heartbeat);
        _heartbeat = null;
        _soundTimer = 0;
    }

    public override void FrameUpdate(float frameTime)
    {
        var target = TryComp<WithdrawalVisualsComponent>(_player.LocalEntity, out var visuals)
            ? visuals.Intensity : 0f;
        _overlay.Intensity += (target - _overlay.Intensity) * Math.Clamp(frameTime * 1.5f, 0, 1);
        if (target < .65f)
        {
            if (_heartbeat != null || _soundTimer != 0)
                StopHeartbeat();
            return;
        }
        _soundTimer -= frameTime;
        if (_soundTimer <= 0)
        {
            _audio.Stop(_heartbeat);
            _heartbeat = _audio.PlayGlobal(new SoundPathSpecifier("/Audio/_radiant/Addictions/heartbeat.ogg"),
                Filter.Local(), false, AudioParams.Default.WithVolume(-8))?.Entity;
            _soundTimer = 25;
        }
    }

    public override void Shutdown()
    {
        StopHeartbeat();
        _overlays.RemoveOverlay(_overlay);
        base.Shutdown();
    }
}
