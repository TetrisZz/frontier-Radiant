using Content.Server.Chat.Systems;
using Content.Shared._radiant.Arousal;
using Content.Shared._radiant.Arousal.Components;
using Content.Shared.Humanoid;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Content.Shared.Rejuvenate;

namespace Content.Server._radiant.Arousal;

/// <summary>
/// Server-authoritative arousal simulation:
/// - applies stimulation
/// - handles decay
/// - triggers climax effects
/// </summary>
public sealed class ArousalSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    /// <summary>
    /// Prevents re-entrancy: climax plays the configured emote, which must not grant arousal again.
    /// </summary>
    private bool _suppressEmoteArousal;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HumanoidAppearanceComponent, EmoteEvent>(OnEmotePerformed);
        SubscribeLocalEvent<ArousalComponent, ApplyArousalFromMetabolismEvent>(OnApplyArousalFromMetabolism);
        SubscribeLocalEvent<ArousalComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnRejuvenate(Entity<ArousalComponent> ent, ref RejuvenateEvent args)
    {
        ent.Comp.CurrentArousal = 0f;
        ent.Comp.State = ArousalState.Calm;
        ent.Comp.NextClimaxAt = TimeSpan.Zero;
        Dirty(ent);
    }

    private void OnApplyArousalFromMetabolism(Entity<ArousalComponent> ent, ref ApplyArousalFromMetabolismEvent args)
    {
        AddArousal(ent, args.Amount);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ArousalComponent>();
        while (query.MoveNext(out var uid, out var arousal))
        {
            var config = EnsureComp<ArousalGenderConfigComponent>(uid);
            var genderConfig = ResolveGenderConfig(uid, config);

            // Climax / cap must run before decay: otherwise one tick of decay drops us below Max and climax never fires.
            if (arousal.CurrentArousal >= arousal.MaxArousal)
            {
                ProcessAtMaxArousal(uid, arousal, genderConfig);
                Dirty(uid, arousal);
                continue;
            }

            if (arousal.CurrentArousal > 0f)
            {
                arousal.CurrentArousal = MathF.Max(0f,
                    arousal.CurrentArousal - (arousal.DecayPerSecond * genderConfig.DecayMultiplier * frameTime));
            }

            if (arousal.CurrentArousal <= 0.01f)
                arousal.State = ArousalState.Calm;

            Dirty(uid, arousal);
        }
    }

    private void OnEmotePerformed(EntityUid uid, HumanoidAppearanceComponent _, ref EmoteEvent args)
    {
        if (_suppressEmoteArousal)
            return;

        AddArousal(uid, args.Emote.ArousalPoints);
    }

    public void AddArousal(EntityUid uid, float amount)
    {
        if (amount <= 0f)
            return;

        var arousal = EnsureComp<ArousalComponent>(uid);
        var config = EnsureComp<ArousalGenderConfigComponent>(uid);
        var genderConfig = ResolveGenderConfig(uid, config);

        arousal.CurrentArousal = Math.Clamp(
            arousal.CurrentArousal + amount * genderConfig.GainMultiplier,
            0f,
            arousal.MaxArousal);

        if (arousal.CurrentArousal > 0f)
            arousal.State = ArousalState.Rising;

        if (arousal.CurrentArousal >= arousal.MaxArousal)
            ProcessAtMaxArousal(uid, arousal, genderConfig);

        Dirty(uid, arousal);
    }

    /// <summary>
    /// Partner arousal from interaction panel: base fraction of initiator gain plus a decay allowance over one
    /// <paramref name="interactionCooldown"/> window so passive gain is not fully erased before the next action.
    /// Capped below initiator's raw points so the initiator stays stronger per tick.
    /// </summary>
    public void AddPassivePartnerArousal(EntityUid target, int initiatorArousal, float partnerMultiplier, TimeSpan interactionCooldown)
    {
        if (initiatorArousal <= 0 || partnerMultiplier <= 0f)
            return;

        var arousalComp = EnsureComp<ArousalComponent>(target);
        var config = EnsureComp<ArousalGenderConfigComponent>(target);
        var genderConfig = ResolveGenderConfig(target, config);

        var decayRate = arousalComp.DecayPerSecond * genderConfig.DecayMultiplier;
        var cooldownSec = MathF.Max((float)interactionCooldown.TotalSeconds, 0.25f);

        const float DecayAllowanceFraction = 0.42f;
        const float MaxVsInitiatorRaw = 0.88f;

        var basePartner = initiatorArousal * partnerMultiplier;
        var decayAllowance = decayRate * cooldownSec * DecayAllowanceFraction;
        var amount = MathF.Min(basePartner + decayAllowance, initiatorArousal * MaxVsInitiatorRaw);

        if (amount <= 0f)
            return;

        AddArousal(target, amount);
    }

    /// <summary>
    /// At or above max: trigger climax if cooldown elapsed, else hold at max (cooldown) without decay.
    /// </summary>
    private void ProcessAtMaxArousal(EntityUid uid, ArousalComponent arousal, ArousalGenderConfig genderConfig)
    {
        if (_timing.CurTime < arousal.NextClimaxAt)
        {
            arousal.CurrentArousal = arousal.MaxArousal;
            arousal.State = ArousalState.ClimaxCooldown;
            return;
        }

        TriggerClimax(uid, arousal, genderConfig);
    }

    private void TriggerClimax(EntityUid uid, ArousalComponent arousal, ArousalGenderConfig genderConfig)
    {
        _suppressEmoteArousal = true;
        try
        {
            _chat.TryEmoteWithoutChat(uid, genderConfig.ClimaxEmoteId, ignoreActionBlocker: true);
        }
        finally
        {
            _suppressEmoteArousal = false;
        }

        if (genderConfig.EnableFluidEffect)
            TriggerMaleFluidEffect(uid);

        arousal.NextClimaxAt = _timing.CurTime + arousal.ClimaxCooldown;
        arousal.CurrentArousal = arousal.MaxArousal * 0.35f;
        arousal.State = ArousalState.ClimaxCooldown;
    }

    private void TriggerMaleFluidEffect(EntityUid uid)
    {
        var coordinates = Transform(uid).Coordinates;
        var puddleEnt = Spawn("PuddleCum", coordinates);
        var sound = new SoundPathSpecifier("/Audio/_radiant/Voice/Human/male_moan_2.ogg");
        _audio.PlayPvs(sound, puddleEnt);
    }

    private ArousalGenderConfig ResolveGenderConfig(EntityUid uid, ArousalGenderConfigComponent config)
    {
        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return config.Fallback;

        return humanoid.Sex switch
        {
            Sex.Male => config.Male,
            Sex.Female => config.Female,
            _ => config.Fallback
        };
    }
}

