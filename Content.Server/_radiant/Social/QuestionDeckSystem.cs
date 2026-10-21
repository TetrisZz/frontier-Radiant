using Content.Shared._radiant.Social;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Robust.Shared.Random;

namespace Content.Server._radiant.Social;

public sealed class QuestionDeckSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<QuestionDeckComponent, UseInHandEvent>(OnDraw);
        SubscribeLocalEvent<QuestionDeckComponent, ExaminedEvent>((Entity<QuestionDeckComponent> ent, ref ExaminedEvent args) =>
            args.PushText(Loc.GetString("question-deck-count", ("count", ent.Comp.Blank ? ent.Comp.BlankCards : (ent.Comp.Remaining ?? ent.Comp.Cards).Count))));
    }

    private void OnDraw(Entity<QuestionDeckComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        ent.Comp.Remaining ??= new(ent.Comp.Cards);
        if (ent.Comp.Blank ? ent.Comp.BlankCards <= 0 : ent.Comp.Remaining.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("question-deck-empty"), ent, args.User);
            return;
        }
        var card = Spawn("QuestionCard", Transform(args.User).Coordinates);
        if (ent.Comp.Blank)
            ent.Comp.BlankCards--;
        else
        {
            var index = _random.Next(ent.Comp.Remaining.Count);
            _paper.SetContent(card, Loc.GetString(ent.Comp.Remaining[index]));
            ent.Comp.Remaining.RemoveAt(index);
        }
        _hands.TryPickupAnyHand(args.User, card);
    }
}
