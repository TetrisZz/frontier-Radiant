using System.Numerics;
using Content.Shared._radiant.Social;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._radiant.Social;

public sealed class PoleDanceVisualizerSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PoleDancerComponent, ComponentShutdown>(OnStopped);
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<PoleDancerComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var dance, out var sprite))
        {
            if (!TryComp<PoleDanceVisualComponent>(uid, out var original))
            {
                original = AddComp<PoleDanceVisualComponent>(uid);
                original.Offset = sprite.Offset;
                original.Rotation = sprite.Rotation;
                original.Override = sprite.EnableDirectionOverride;
                original.Direction = sprite.DirectionOverride;
            }
            var time = (float) (_timing.CurTime - dance.StartedAt).TotalSeconds * MathF.Tau / 2.4f;
            var offset = Vector2.Zero;
            var angle = 0f;
            sprite.EnableDirectionOverride = original.Override;
            sprite.DirectionOverride = original.Direction;
            switch (dance.Move)
            {
                case PoleDanceMove.Sway:
                    offset = new Vector2(MathF.Sin(time) * 0.12f, MathF.Abs(MathF.Cos(time)) * 0.025f);
                    angle = MathF.Sin(time) * 0.1f;
                    break;
                case PoleDanceMove.Turn:
                    offset = new Vector2(MathF.Sin(time) * 0.2f, MathF.Cos(time) * 0.05f);
                    sprite.EnableDirectionOverride = true;
                    sprite.DirectionOverride = (((int) MathF.Floor(time / (MathF.PI / 2)) % 4 + 4) % 4) switch
                    {
                        0 => Direction.South,
                        1 => Direction.East,
                        2 => Direction.North,
                        _ => Direction.West,
                    };
                    break;
                case PoleDanceMove.Lean:
                    var lean = (1 - MathF.Cos(time)) / 2;
                    offset = new Vector2(lean * 0.12f, -lean * 0.1f);
                    angle = lean * 0.35f;
                    break;
            }
            _sprites.SetOffset((uid, sprite), original.Offset + offset);
            _sprites.SetRotation((uid, sprite), original.Rotation + new Angle(angle));
        }
    }

    private void OnStopped(Entity<PoleDancerComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<PoleDanceVisualComponent>(ent, out var original))
            return;
        if (TryComp<SpriteComponent>(ent, out var sprite))
        {
            _sprites.SetOffset((ent.Owner, sprite), original.Offset);
            _sprites.SetRotation((ent.Owner, sprite), original.Rotation);
            sprite.EnableDirectionOverride = original.Override;
            sprite.DirectionOverride = original.Direction;
        }
        RemComp<PoleDanceVisualComponent>(ent);
    }
}

[RegisterComponent]
public sealed partial class PoleDanceVisualComponent : Component
{
    public Vector2 Offset;
    public Angle Rotation;
    public bool Override;
    public Direction Direction;
}
