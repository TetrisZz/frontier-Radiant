using System.Numerics;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Server._radiant.Basketball;

[RegisterComponent]
public sealed partial class BasketballBallComponent : Component;

[RegisterComponent]
public sealed partial class BasketballHoopComponent : Component
{
    [DataField] public float ShotChance = 0.5f;
    [ViewVariables] public int Score;
}

public sealed partial class BasketballSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ThrownItemSystem _thrown = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private TransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BasketballHoopComponent, ThrowHitByEvent>(OnThrowHit);
        SubscribeLocalEvent<BasketballHoopComponent, InteractUsingEvent>(OnDunk);
        SubscribeLocalEvent<BasketballHoopComponent, ExaminedEvent>(OnExamined);
    }

    private void OnThrowHit(Entity<BasketballHoopComponent> ent, ref ThrowHitByEvent args)
    {
        if (!HasComp<BasketballBallComponent>(args.Thrown))
            return;

        if (!_random.Prob(ent.Comp.ShotChance))
        {
            _popup.PopupEntity(Loc.GetString("radiant-basketball-miss"), ent);
            return;
        }

        _thrown.StopThrow(args.Thrown, args.Component);
        _physics.SetLinearVelocity(args.Thrown, Vector2.Zero);
        _transform.SetCoordinates(args.Thrown, Transform(ent).Coordinates);
        ent.Comp.Score++;
        _popup.PopupEntity(Loc.GetString("radiant-basketball-score", ("score", ent.Comp.Score)), ent);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/chime.ogg"), ent);
    }

    private void OnDunk(Entity<BasketballHoopComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<BasketballBallComponent>(args.Used) ||
            !_hands.TryDrop(args.User, args.Used, Transform(ent).Coordinates))
            return;

        args.Handled = true;
        ent.Comp.Score++;
        _popup.PopupEntity(Loc.GetString("radiant-basketball-dunk",
            ("player", Name(args.User)), ("score", ent.Comp.Score)), ent);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/chime.ogg"), ent);
    }

    private void OnExamined(Entity<BasketballHoopComponent> ent, ref ExaminedEvent args)
        => args.PushText(Loc.GetString("radiant-basketball-examine", ("score", ent.Comp.Score)));
}
