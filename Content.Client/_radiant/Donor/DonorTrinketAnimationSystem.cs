using System.Numerics;
using Content.Shared._radiant.Donor;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Shared.Animations;

namespace Content.Client._radiant.Donor;

public sealed class DonorTrinketAnimationSystem : EntitySystem
{
    [Dependency] private readonly AnimationPlayerSystem _animations = default!;
    private const string Key = "donor-trinket";

    public override void Initialize() => SubscribeNetworkEvent<DonorTrinketAnimationEvent>(OnAnimate);

    private void OnAnimate(DonorTrinketAnimationEvent args)
    {
        var uid = GetEntity(args.Entity);
        if (!TryComp<SpriteComponent>(uid, out var sprite) || _animations.HasRunningAnimation(uid, Key))
            return;
        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(0.8),
            AnimationTracks =
            {
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Scale),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(sprite.Scale, 0),
                        new AnimationTrackProperty.KeyFrame(new Vector2(sprite.Scale.X * 0.1f, sprite.Scale.Y), 0.2f),
                        new AnimationTrackProperty.KeyFrame(sprite.Scale, 0.2f),
                        new AnimationTrackProperty.KeyFrame(new Vector2(sprite.Scale.X * 0.1f, sprite.Scale.Y), 0.2f),
                        new AnimationTrackProperty.KeyFrame(sprite.Scale, 0.2f),
                    },
                },
                new AnimationTrackComponentProperty
                {
                    ComponentType = typeof(SpriteComponent),
                    Property = nameof(SpriteComponent.Offset),
                    InterpolationMode = AnimationInterpolationMode.Linear,
                    KeyFrames =
                    {
                        new AnimationTrackProperty.KeyFrame(sprite.Offset, 0),
                        new AnimationTrackProperty.KeyFrame(sprite.Offset + new Vector2(0, 0.25f), 0.4f),
                        new AnimationTrackProperty.KeyFrame(sprite.Offset, 0.4f),
                    },
                },
            },
        };
        _animations.Play(uid, animation, Key);
    }
}
