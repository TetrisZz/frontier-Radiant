using Content.Server._NF.Bank;
using Content.Server.Chat.Systems;
using Content.Shared._radiant.Casino;
using Content.Shared.Dataset;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Casino;

public sealed class CasinoMachineSystem : EntitySystem
{
    [Dependency] private readonly BankSystem _bank = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly SoundPathSpecifier LossSong =
        new("/Audio/_radiant/Casino/in-the-mouth-of-this-casino.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CasinoMachineComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<CasinoMachineComponent, CasinoMachineSpinMessage>(OnSpin);
    }

    private void OnUiOpened(Entity<CasinoMachineComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateState(ent, args.Actor, CasinoSymbol.Cherry, CasinoSymbol.Clover, CasinoSymbol.Seven, 0,
            CasinoSpinResult.None);
    }

    private void OnSpin(Entity<CasinoMachineComponent> ent, ref CasinoMachineSpinMessage args)
    {
        if (_timing.CurTime < ent.Comp.NextSpinTime)
        {
            UpdateState(ent, args.Actor, CasinoSymbol.Cherry, CasinoSymbol.Clover, CasinoSymbol.Seven, 0,
                CasinoSpinResult.Cooldown);
            return;
        }

        if (args.Bet <= 0 || args.Bet > 100_000)
        {
            UpdateState(ent, args.Actor, CasinoSymbol.Cherry, CasinoSymbol.Clover, CasinoSymbol.Seven, 0,
                CasinoSpinResult.InvalidAmount);
            return;
        }

        ent.Comp.Bet = args.Bet;
        if (!_bank.TryBankWithdraw(args.Actor, args.Bet))
        {
            UpdateState(ent, args.Actor, CasinoSymbol.Cherry, CasinoSymbol.Clover, CasinoSymbol.Seven, 0,
                CasinoSpinResult.BankInsufficient);
            return;
        }

        ent.Comp.NextSpinTime = _timing.CurTime + ent.Comp.SpinCooldown;

        var first = RollSymbol();
        var second = RollSymbol();
        var third = RollSymbol();
        var payout = Math.Min(GetMultiplier(first, second, third) * args.Bet, 7_500_000);
        if (payout > 0)
            _bank.TryBankDeposit(args.Actor, payout);
        else if (_random.Prob(0.1f))
            _audio.PlayPvs(LossSong, ent.Owner, AudioParams.Default.WithVolume(-4f).WithMaxDistance(12f));

        SpeakAfterSpin(ent, payout > 0);

        Dirty(ent);
        UpdateState(ent, args.Actor, first, second, third, payout,
            payout > 0 ? CasinoSpinResult.Win : CasinoSpinResult.Loss);
    }

    private void SpeakAfterSpin(Entity<CasinoMachineComponent> ent, bool won)
    {
        if (!_random.Prob(0.5f))
            return;

        var dataset = _prototypes.Index<LocalizedDatasetPrototype>(won ? "RadiantArcadeWin" : "RadiantArcadeLoss");
        var message = Loc.GetString(_random.Pick(dataset.Values));
        _chat.TrySendInGameICMessage(ent, message, InGameICChatType.Speak, true);
    }

    private CasinoSymbol RollSymbol()
    {
        return (CasinoSymbol) _random.Next(Enum.GetValues<CasinoSymbol>().Length);
    }

    private int GetMultiplier(CasinoSymbol first, CasinoSymbol second, CasinoSymbol third)
    {
        if (first == CasinoSymbol.Diamond && second == CasinoSymbol.Diamond && third == CasinoSymbol.Diamond)
            return 75;
        if (first == CasinoSymbol.Seven && second == CasinoSymbol.Seven && third == CasinoSymbol.Seven)
            return 15;
        if (first == second && second == third)
        {
            return first switch
            {
                CasinoSymbol.Horseshoe => 10,
                CasinoSymbol.Clover => 8,
                CasinoSymbol.Plum => 6,
                CasinoSymbol.Lemon => 5,
                CasinoSymbol.Cherry => 4,
                _ => 4,
            };
        }

        // Pairs are common with seven symbols, so only some of them pay out.
        return (first == second || first == third || second == third) && _random.Prob(0.3f) ? 2 : 0;
    }

    private void UpdateState(
        Entity<CasinoMachineComponent> ent,
        EntityUid player,
        CasinoSymbol first,
        CasinoSymbol second,
        CasinoSymbol third,
        int payout,
        CasinoSpinResult result)
    {
        var bankBalance = -1;
        _bank.TryGetBalance(player, out bankBalance);
        _ui.SetUiState(ent.Owner, CasinoMachineUiKey.Key,
            new CasinoUiState(0, bankBalance, ent.Comp.Bet, first, second, third, payout, result));
    }
}
