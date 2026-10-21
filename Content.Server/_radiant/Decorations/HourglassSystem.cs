using Content.Shared._radiant.Decorations;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Decorations;

public sealed class HourglassSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HourglassComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<HourglassComponent, GetVerbsEvent<AlternativeVerb>>(OnVerbs);
        SubscribeLocalEvent<HourglassComponent, ExaminedEvent>(OnExamine);
    }

    public void Start(Entity<HourglassComponent> ent)
    {
        ent.Comp.StartedAt = _timing.CurTime;
        ent.Comp.EndsAt = _timing.CurTime + TimeSpan.FromSeconds(Math.Max(1, ent.Comp.Duration));
        ent.Comp.Running = true;
        _appearance.SetData(ent, HourglassVisuals.State, "flip");
    }

    private void OnUse(Entity<HourglassComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        Start(ent);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<HourglassComponent>();
        while (query.MoveNext(out var uid, out var clock))
        {
            if (!clock.Running)
                continue;
            if (_timing.CurTime >= clock.EndsAt)
            {
                clock.Running = false;
                _appearance.SetData(uid, HourglassVisuals.State, "idle");
                _popup.PopupEntity(Loc.GetString("hourglass-finished"), uid);
                continue;
            }
            var elapsed = (_timing.CurTime - clock.StartedAt).TotalSeconds;
            if (elapsed < 0.7)
                continue;
            var stage = Math.Clamp((int)(elapsed / Math.Max(1, clock.Duration) * 7) + 1, 1, 7);
            _appearance.SetData(uid, HourglassVisuals.State, "stage" + stage);
        }
    }

    private void OnVerbs(Entity<HourglassComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        foreach (var minutes in new[] { 1, 3, 5 })
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("hourglass-set-duration", ("minutes", minutes)),
                Act = () =>
                {
                    if (!_interaction.InRangeUnobstructed(user, ent.Owner))
                        return;
                    ent.Comp.Duration = minutes * 60;
                    Start(ent);
                },
            });
        }
    }

    private void OnExamine(Entity<HourglassComponent> ent, ref ExaminedEvent args)
    {
        args.PushText(Loc.GetString("hourglass-duration", ("seconds", ent.Comp.Duration)));
        if (ent.Comp.Running)
            args.PushText(Loc.GetString("hourglass-remaining", ("seconds", Math.Max(0, (int)Math.Ceiling((ent.Comp.EndsAt - _timing.CurTime).TotalSeconds)))));
    }
}
