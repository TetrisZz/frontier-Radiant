using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Speech.Components;
using Content.Server.Traits.Assorted;
using Content.Shared.Abilities;
using Content.Shared.Chat;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Overlays;
using Content.Shared.Stunnable;
using Content.Shared.Traits.Assorted;
using Content.Shared._radiant.Abilities.Shadowkin;
using Content.Shared._radiant.Medical.Genetics;
using Robust.Shared.Audio;
using Robust.Shared.Random;

namespace Content.Server._radiant.Medical.Genetics;

public sealed partial class GeneticModificationSystem
{
    [Dependency] private IRobustRandom _mutationRandom = default!;
    [Dependency] private MovementSpeedModifierSystem _geneticMovement = default!;
    [Dependency] private BlurryVisionSystem _geneticBlur = default!;
    [Dependency] private SharedStunSystem _geneticStun = default!;
    [Dependency] private ChatSystem _geneticChat = default!;

    public bool HasActive(EntityUid uid, string gene)
        => TryComp<GeneticProfileComponent>(uid, out var profile)
            && profile.Modifications.TryGetValue(gene, out var remaining) && remaining <= 0;

    private void RollComplication(GeneticProfileComponent profile, GeneticModificationPrototype gene)
    {
        if (gene.Complications.Count > 0 && _mutationRandom.Prob(Math.Clamp(gene.ComplicationChance, 0, 1)))
            profile.Complications[gene.ID] = gene.Complications[_mutationRandom.Next(gene.Complications.Count)];
    }

    public IEnumerable<string> VisibleChanges(EntityUid uid)
    {
        if (!TryComp<GeneticProfileComponent>(uid, out var profile)) yield break;
        foreach (var (id, remaining) in profile.Modifications)
            if (remaining <= 0 && _prototypes.TryIndex<GeneticModificationPrototype>(id, out var gene)
                && gene.ExamineHint != null)
                yield return gene.ExamineHint;
    }

    private void RefreshGeneticEffects(EntityUid uid, GeneticProfileComponent profile)
    {
        var active = profile.Modifications.Where(pair => pair.Value <= 0).Select(pair => pair.Key).ToHashSet();
        var flaws = profile.Complications.Where(pair => active.Contains(pair.Key)).Select(pair => pair.Value).ToHashSet();
        var effects = EnsureComp<GeneticBodyEffectsComponent>(uid);
        var strength = active.Contains("RadiantStrength") ? 1.25f : 1f;
        if (flaws.Contains("weakness")) strength = .5f;
        var speed = flaws.Contains("weakness") ? .9f : 1;
        var poorVision = flaws.Contains("poorvision");
        var insulated = active.Contains("RadiantInsulation");
        if (effects.UnarmedMultiplier != strength || effects.Speed != speed
            || effects.PoorVision != poorVision || effects.Insulated != insulated)
        {
            effects.UnarmedMultiplier = strength;
            effects.Speed = speed;
            effects.PoorVision = poorVision;
            effects.Insulated = insulated;
            Dirty(uid, effects);
            _geneticMovement.RefreshMovementSpeedModifiers(uid);
            _geneticBlur.UpdateBlurMagnitude(uid);
        }

        OwnEffect<UltraVisionComponent>(uid, profile, active.Contains("RadiantUltravision"));
        OwnEffect<ShadowkinUltravisionTintComponent>(uid, profile, active.Contains("RadiantUltravision"));
        OwnEffect<BlackAndWhiteOverlayComponent>(uid, profile, flaws.Contains("monochromacy"));
        OwnEffect<SlurredAccentComponent>(uid, profile, flaws.Contains("slurred"));
        OwnEffect<ScrambledAccentComponent>(uid, profile, flaws.Contains("scrambled"));
        OwnEffect<StutteringAccentComponent>(uid, profile, flaws.Contains("mixedaccent"));
        OwnEffect<FrenchAccentComponent>(uid, profile, flaws.Contains("mixedaccent"));
        OwnEffect<GermanAccentComponent>(uid, profile, flaws.Contains("mixedaccent"));
        if (OwnEffect<ParacusiaComponent>(uid, profile, flaws.Contains("paracusia")))
        {
            var sounds = EntityManager.System<ParacusiaSystem>();
            sounds.SetSounds(uid, new SoundCollectionSpecifier("Paracusia"));
            sounds.SetTime(uid, 60, 180);
            sounds.SetDistance(uid, 7);
        }
    }

    // Remove only the exact component created by genetics, never a pre-existing species or trait component.
    private bool OwnEffect<T>(EntityUid uid, GeneticProfileComponent profile, bool needed) where T : Component, new()
    {
        var key = typeof(T);
        if (needed)
        {
            if (HasComp<T>(uid)) return false;
            profile.OwnedEffects[key] = AddComp<T>(uid);
            return true;
        }
        if (profile.OwnedEffects.Remove(key, out var owned)
            && TryComp<T>(uid, out var current) && ReferenceEquals(current, owned))
            RemComp<T>(uid);
        return false;
    }

    private void AdvanceComplications(EntityUid uid, GeneticProfileComponent profile, float seconds)
    {
        if (!profile.Complications.Any(pair => pair.Value == "tics"
            && profile.Modifications.GetValueOrDefault(pair.Key, 1) <= 0))
        {
            profile.TicTimer = 120;
            return;
        }
        profile.TicTimer -= seconds;
        if (profile.TicTimer > 0) return;
        profile.TicTimer = _mutationRandom.Next(90, 181);
        _geneticChat.TrySendInGameICMessage(uid, Loc.GetString("genetics-tic-utterance"), InGameICChatType.Speak, false);
        _geneticStun.TryUpdateParalyzeDuration(uid, TimeSpan.FromSeconds(_mutationRandom.Next(2, 31)));
    }
}
