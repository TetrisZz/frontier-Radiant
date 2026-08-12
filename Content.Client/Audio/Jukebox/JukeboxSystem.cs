using Content.Client._radiant.CCVar;
using Content.Shared.Audio.Jukebox;
using Robust.Client.Animations;
using Robust.Client.Audio; // Radiant Sector
using Robust.Client.GameObjects;
using Robust.Shared.Audio.Components; // Radiant Sector
using Robust.Shared.Audio.Systems; // Radiant Sector
using Robust.Shared.Configuration; // Radiant Sector
using Robust.Shared.Prototypes;
using Robust.Shared.Containers; // Frontier

namespace Content.Client.Audio.Jukebox;


public sealed class JukeboxSystem : SharedJukeboxSystem
{
    private bool _clientMusicEnabled = true; // Radiant Sector

    [Dependency] private readonly AnimationPlayerSystem _animationPlayer = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!; // Radiant Sector
    [Dependency] private readonly SharedAudioSystem _audio = default!; // Radiant Sector

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(AudioSystem)); // Radiant Sector: keep locally muted music silent after audio updates.
        base.Initialize();
        SubscribeLocalEvent<JukeboxComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<JukeboxComponent, AnimationCompletedEvent>(OnAnimationCompleted);
        SubscribeLocalEvent<JukeboxComponent, AfterAutoHandleStateEvent>(OnJukeboxAfterState);
        SubscribeLocalEvent<JukeboxComponent, EntInsertedIntoContainerMessage>(OnRecordInserted); // Frontier

        // Radiant Sector: allow this client to mute jukebox and boombox music.
        Subs.CVar(_configuration, RadiantClientCCVars.JukeboxMusicEnabled, OnJukeboxMusicChanged, true);

        _protoManager.PrototypesReloaded += OnProtoReload;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _protoManager.PrototypesReloaded -= OnProtoReload;
    }

    private void OnProtoReload(PrototypesReloadedEventArgs obj)
    {
        if (!obj.WasModified<JukeboxPrototype>())
            return;

        var query = AllEntityQuery<JukeboxComponent, UserInterfaceComponent>();

        while (query.MoveNext(out var uid, out _, out var ui))
        {
            if (!_uiSystem.TryGetOpenUi<JukeboxBoundUserInterface>((uid, ui), JukeboxUiKey.Key, out var bui))
                continue;

            bui.PopulateMusic();
        }
    }

    private void OnJukeboxAfterState(Entity<JukeboxComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        // Radiant Sector: reapply the local mute after the server changes this machine or its stream.
        ApplyClientAudibility(ent.Comp);

        if (!_uiSystem.TryGetOpenUi<JukeboxBoundUserInterface>(ent.Owner, JukeboxUiKey.Key, out var bui))
            return;

        bui.PopulateMusic(); // Frontier
        bui.Reload();
    }

    // Radiant Sector start
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_clientMusicEnabled)
            MuteAllClientStreams();
    }

    private void OnJukeboxMusicChanged(bool enabled)
    {
        _clientMusicEnabled = enabled;

        if (enabled)
            ApplyClientAudibilityToAll();
        else
            MuteAllClientStreams();
    }

    private void MuteAllClientStreams()
    {
        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out _, out var jukebox))
        {
            if (jukebox.AudioStream is not { } stream || !TryComp<AudioComponent>(stream, out var audio))
                continue;

            audio.Gain = 0f;
        }
    }

    private void ApplyClientAudibilityToAll()
    {
        var query = EntityQueryEnumerator<JukeboxComponent>();
        while (query.MoveNext(out _, out var jukebox))
            ApplyClientAudibility(jukebox);
    }

    private void ApplyClientAudibility(JukeboxComponent jukebox)
    {
        if (jukebox.AudioStream is not { } stream || !TryComp<AudioComponent>(stream, out var audio))
            return;

        if (!_clientMusicEnabled)
        {
            audio.Gain = 0f;
            return;
        }

        var volume = SharedAudioSystem.GainToVolume(Math.Clamp(jukebox.Volume, 0f, 1f));
        _audio.SetVolume(audio.Owner, volume, audio);
    }
    // Radiant Sector end

    // Frontier
    public void OnRecordInserted(Entity<JukeboxComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (!_uiSystem.TryGetOpenUi<JukeboxBoundUserInterface>(ent.Owner, JukeboxUiKey.Key, out var bui))
            return;

        bui.PopulateMusic();
    }
    // End Frontier
    private void OnAnimationCompleted(EntityUid uid, JukeboxComponent component, AnimationCompletedEvent args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        if (!TryComp<AppearanceComponent>(uid, out var appearance) ||
            !_appearanceSystem.TryGetData<JukeboxVisualState>(uid, JukeboxVisuals.VisualState, out var visualState, appearance))
        {
            visualState = JukeboxVisualState.On;
        }

        UpdateAppearance((uid, sprite), visualState, component);
    }

    private void OnAppearanceChange(EntityUid uid, JukeboxComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (!args.AppearanceData.TryGetValue(JukeboxVisuals.VisualState, out var visualStateObject) ||
            visualStateObject is not JukeboxVisualState visualState)
        {
            visualState = JukeboxVisualState.On;
        }

        UpdateAppearance((uid, args.Sprite), visualState, component);
    }

    private void UpdateAppearance(Entity<SpriteComponent> entity, JukeboxVisualState visualState, JukeboxComponent component)
    {
        SetLayerState(JukeboxVisualLayers.Base, component.OffState, entity);

        switch (visualState)
        {
            case JukeboxVisualState.On:
                SetLayerState(JukeboxVisualLayers.Base, component.OnState, entity);
                break;

            case JukeboxVisualState.Off:
                SetLayerState(JukeboxVisualLayers.Base, component.OffState, entity);
                break;

            case JukeboxVisualState.Select:
                PlayAnimation(entity.Owner, JukeboxVisualLayers.Base, component.SelectState, 1.0f, entity);
                break;
        }
    }

    private void PlayAnimation(EntityUid uid, JukeboxVisualLayers layer, string? state, float animationTime, SpriteComponent sprite)
    {
        if (string.IsNullOrEmpty(state))
            return;

        if (!_animationPlayer.HasRunningAnimation(uid, state))
        {
            var animation = GetAnimation(layer, state, animationTime);
            _sprite.LayerSetVisible((uid, sprite), layer, true);
            _animationPlayer.Play(uid, animation, state);
        }
    }

    private static Animation GetAnimation(JukeboxVisualLayers layer, string state, float animationTime)
    {
        return new Animation
        {
            Length = TimeSpan.FromSeconds(animationTime),
            AnimationTracks =
                {
                    new AnimationTrackSpriteFlick
                    {
                        LayerKey = layer,
                        KeyFrames =
                        {
                            new AnimationTrackSpriteFlick.KeyFrame(state, 0f)
                        }
                    }
                }
        };
    }

    private void SetLayerState(JukeboxVisualLayers layer, string? state, Entity<SpriteComponent> sprite)
    {
        if (string.IsNullOrEmpty(state))
            return;

        _sprite.LayerSetVisible(sprite.AsNullable(), layer, true);
        _sprite.LayerSetAutoAnimated(sprite.AsNullable(), layer, true);
        _sprite.LayerSetRsiState(sprite.AsNullable(), layer, state);
    }
}
