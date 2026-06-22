namespace Content.Server.Chat.Systems;

using Content.Shared.Chat.Prototypes;
using Content.Shared.Damage;
using Content.Shared.FixedPoint; ///radiant sector
using Content.Shared.Traits.Assorted;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

public sealed class EmoteOnDamageSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmoteOnDamageComponent, DamageChangedEvent>(OnDamage);
    }

    private void OnDamage(EntityUid uid, EmoteOnDamageComponent emoteOnDamage, DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (HasComp<EmotionalRestraintComponent>(uid)) ///radiant sector
            return;

        if (args.DamageDelta == null) ///radiant sector
            return;

        if (emoteOnDamage.LastEmoteTime + emoteOnDamage.EmoteCooldown > _gameTiming.CurTime)
            return;

        if (emoteOnDamage.Emotes.Count == 0)
            return;

        var qualifyingDamage = GetQualifyingDamage(args.DamageDelta, emoteOnDamage.DamageTypes); ///radiant sector
        var threshold = emoteOnDamage.MaximumDamage > emoteOnDamage.MinimumDamage ///radiant sector
            ? _random.NextFloat(emoteOnDamage.MinimumDamage, emoteOnDamage.MaximumDamage) ///radiant sector
            : emoteOnDamage.MinimumDamage; ///radiant sector

        if (qualifyingDamage < FixedPoint2.New(threshold)) ///radiant sector
            return; ///radiant sector

        if (!_random.Prob(emoteOnDamage.EmoteChance))
            return;

        var emote = PickEmote(emoteOnDamage); ///radiant sector
        if (emoteOnDamage.WithChat)
        {
            _prototypeManager.TryIndex<EmotePrototype>(emote, out var prototype); ///Radiant Sector
            var chatMessage = emoteOnDamage.ChatMessages.GetValueOrDefault(emote);

            if (prototype != null)
            {
                _chatSystem.TryEmoteWithChat(
                    uid,
                    prototype,
                    chatMessage,
                    emoteOnDamage.HiddenFromChatWindow ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal);
            }
        }
        else
        {
            _chatSystem.TryEmoteWithoutChat(uid, emote);
        }

        emoteOnDamage.LastEmoteTime = _gameTiming.CurTime;
    }

    private string PickEmote(EmoteOnDamageComponent emoteOnDamage) ///radiant sector
    {
        if (emoteOnDamage.EmoteWeights.Count == 0)
            return _random.Pick(emoteOnDamage.Emotes);

        var totalWeight = 0f;
        foreach (var emote in emoteOnDamage.Emotes)
        {
            if (emoteOnDamage.EmoteWeights.TryGetValue(emote, out var weight) && weight > 0)
                totalWeight += weight;
            else if (!emoteOnDamage.EmoteWeights.ContainsKey(emote))
                totalWeight += 1f;
        }

        if (totalWeight <= 0)
            return _random.Pick(emoteOnDamage.Emotes);

        var roll = _random.NextFloat() * totalWeight;
        var accumulated = 0f;
        foreach (var emote in emoteOnDamage.Emotes)
        {
            var weight = emoteOnDamage.EmoteWeights.TryGetValue(emote, out var configuredWeight)
                ? configuredWeight
                : 1f;

            if (weight <= 0)
                continue;

            accumulated += weight;
            if (accumulated >= roll)
                return emote;
        }

        return _random.Pick(emoteOnDamage.Emotes);
    }

    private static FixedPoint2 GetQualifyingDamage(DamageSpecifier damage, HashSet<string> damageTypes) ///radiant sector
    {
        var total = FixedPoint2.Zero;

        foreach (var (type, value) in damage.DamageDict)
        {
            if (value <= FixedPoint2.Zero)
                continue;

            if (damageTypes.Count != 0 && !damageTypes.Contains(type))
                continue;

            total += value;
        }

        return total;
    }

    /// <summary>
    /// Try to add an emote to the entity, which will be performed at an interval.
    /// </summary>
    public bool AddEmote(EntityUid uid, string emotePrototypeId, EmoteOnDamageComponent? emoteOnDamage = null)
    {
        if (!Resolve(uid, ref emoteOnDamage, logMissing: false))
            return false;

        DebugTools.Assert(emoteOnDamage.LifeStage <= ComponentLifeStage.Running);
        DebugTools.Assert(_prototypeManager.HasIndex<EmotePrototype>(emotePrototypeId), "Prototype not found. Did you make a typo?");

        return emoteOnDamage.Emotes.Add(emotePrototypeId);
    }

    /// <summary>
    /// Stop preforming an emote. Note that by default this will queue empty components for removal.
    /// </summary>
    public bool RemoveEmote(EntityUid uid, string emotePrototypeId, EmoteOnDamageComponent? emoteOnDamage = null, bool removeEmpty = true)
    {
        if (!Resolve(uid, ref emoteOnDamage, logMissing: false))
            return false;

        DebugTools.Assert(_prototypeManager.HasIndex<EmotePrototype>(emotePrototypeId), "Prototype not found. Did you make a typo?");

        if (!emoteOnDamage.Emotes.Remove(emotePrototypeId))
            return false;

        if (removeEmpty && emoteOnDamage.Emotes.Count == 0)
            RemCompDeferred(uid, emoteOnDamage);

        return true;
    }
}
