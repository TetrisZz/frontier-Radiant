using System.Linq;
using Content.Shared._radiant.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared.Corvax.TTS;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Medical.Surgery;

[RegisterComponent]
public sealed partial class SurgicalVoicePermissionComponent : Component
{
    public EntityUid Surgeon;
    public Sex Sex;
    public bool RequiresConsent;
    public TimeSpan Expires;
}

/// <summary>A short-lived, surgeon-bound permission issued only by a completed surgery.</summary>
public sealed partial class SurgicalVoiceSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _appearance = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedSurgerySystem _surgery = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SurgicalVoicePermissionComponent, SurgicalVoiceMessage>(OnSelected);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SurgicalVoicePermissionComponent>();
        while (query.MoveNext(out var uid, out var permission))
        {
            if (_timing.CurTime <= permission.Expires
                && TryComp<HumanoidAppearanceComponent>(uid, out var appearance)
                && appearance.Sex == permission.Sex
                && (!permission.RequiresConsent || !_surgery.IsErpDenied(uid) && !_surgery.IsErpDenied(permission.Surgeon)))
                continue;
            _ui.CloseUi(uid, SurgicalVoiceUiKey.Key);
            RemCompDeferred<SurgicalVoicePermissionComponent>(uid);
        }
    }

    public void OpenSelection(EntityUid patient, EntityUid surgeon, bool requiresConsent)
    {
        if (!TryComp<HumanoidAppearanceComponent>(patient, out var appearance))
            return;
        var permission = EnsureComp<SurgicalVoicePermissionComponent>(patient);
        permission.Surgeon = surgeon;
        permission.Sex = appearance.Sex;
        permission.RequiresConsent = requiresConsent;
        permission.Expires = _timing.CurTime + TimeSpan.FromMinutes(2);

        // Never retain a voice incompatible with the new sex if the picker is closed.
        if (!IsVoiceAllowed(appearance.Voice, appearance.Sex))
        {
            var fallback = _prototypes.EnumeratePrototypes<TTSVoicePrototype>()
                .Where(v => IsVoiceAllowed(v.ID, appearance.Sex)).OrderBy(v => v.ID).FirstOrDefault();
            if (fallback != null)
            {
                _appearance.SetTTSVoice(patient, fallback.ID, appearance);
                Dirty(patient, appearance);
            }
        }

        _ui.SetUi(patient, SurgicalVoiceUiKey.Key,
            new InterfaceData("SurgicalVoiceBoundUserInterface"));
        _ui.CloseUi(patient, SurgicalVoiceUiKey.Key);
        _ui.SetUiState(patient, SurgicalVoiceUiKey.Key, new SurgicalVoiceState(appearance.Sex, appearance.Voice,
            EntityManager.System<BrainRestorationSystem>().OriginalVoice(patient)));
        _ui.TryOpenUi(patient, SurgicalVoiceUiKey.Key, surgeon);
    }

    public bool IsVoiceAllowed(string id, Sex sex)
        => _prototypes.TryIndex<TTSVoicePrototype>(id, out var voice)
           && voice.RoundStart && HumanoidCharacterProfile.CanHaveVoice(voice, sex);

    private void OnSelected(Entity<SurgicalVoicePermissionComponent> ent, ref SurgicalVoiceMessage args)
    {
        if (args.Actor != ent.Comp.Surgeon || _timing.CurTime > ent.Comp.Expires
            || !TryComp<HumanoidAppearanceComponent>(ent, out var appearance)
            || appearance.Sex != ent.Comp.Sex
            || ent.Comp.RequiresConsent && (_surgery.IsErpDenied(ent) || _surgery.IsErpDenied(args.Actor))
            || !IsVoiceAllowed(args.Voice, appearance.Sex))
            return;

        // The BUI also enforces range and the actor's ability to interact.
        _appearance.SetTTSVoice(ent, args.Voice, appearance);
        Dirty(ent, appearance);
        _ui.CloseUi(ent.Owner, SurgicalVoiceUiKey.Key);
        RemComp<SurgicalVoicePermissionComponent>(ent);
    }
}
