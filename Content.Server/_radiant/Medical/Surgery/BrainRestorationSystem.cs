using System.Linq;
using Content.Server.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Forensics;
using Content.Shared.Forensics.Components;
using Content.Shared.DetailExaminable;
using Content.Shared._NF.Bank.Components;
using Content.Server._NF.Bank;
using Content.Server._EE.Contractors.Systems;
using Content.Shared._radiant.Passports;
using Content.Server._radiant.Dossiers;
using Content.Shared._radiant;
using Content.Shared._radiant.Skills;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared._radiant.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Popups;
using Content.Shared.Corvax.TTS;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._radiant.Medical.Surgery;

[RegisterComponent]
public sealed partial class BrainIdentityMemoryComponent : Component
{
    [DataField] public HumanoidAppearanceComponent Appearance = new();
    [DataField] public string OriginalName = "";
    [DataField] public string? Dna;
    [DataField] public string? Fingerprint;
    [DataField] public string? Description;
    [DataField] public EnumERPStatus? ErpStatus;
    [DataField] public bool HadBankAccount;
    [DataField] public int[]? SkillLevels;
    [DataField] public string? PassportNumber;
    [DataField] public string PassportCurrentDocumentNumber = "";
    [DataField] public string? PassportName;
    [DataField] public RadiantCitizenship PassportCitizenship;
    [DataField] public bool PassportNeedsReview;
}

/// <summary>Identity belongs to the transplanted brain, never to its recipient.</summary>
public sealed partial class BrainRestorationSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedBrainRestorationSystem _records = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _appearance = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PassportSystem _passports = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<SurgeryRestoreIdentityComponent, SurgeryStepEvent>(OnRestore);
    }

    private void OnSpawn(PlayerSpawnCompleteEvent args)
    {
        if (HasComp<HumanoidAppearanceComponent>(args.Mob))
            _passports.EnsureIdentity(args.Mob, args.Profile);
        foreach (var part in _body.GetBodyChildren(args.Mob))
        foreach (var organ in _body.GetPartOrgans(part.Id, part.Component))
            if (HasComp<BrainComponent>(organ.Id))
            {
                Capture(organ.Id, args.Mob);
                // Spawn subscribers may initialize the body's skill component in either order.
                if (TryComp<BrainIdentityMemoryComponent>(organ.Id, out var memory))
                    memory.SkillLevels = ProfessionalSkillRules.Normalize(args.Profile.SkillLevels);
            }
    }

    public void Capture(EntityUid brain, EntityUid donor)
    {
        if (HasComp<BrainIdentityMemoryComponent>(brain)
            || !TryComp<HumanoidAppearanceComponent>(donor, out var appearance))
            return;
        var memory = EnsureComp<BrainIdentityMemoryComponent>(brain);
        memory.Appearance = _serialization.CreateCopy(appearance, notNullableOverride: true);
        memory.OriginalName = MetaData(donor).EntityName;
        memory.Dna = CompOrNull<DnaComponent>(donor)?.DNA;
        memory.Fingerprint = CompOrNull<FingerprintComponent>(donor)?.Fingerprint;
        memory.HadBankAccount = HasComp<BankAccountComponent>(donor);
        if (TryComp<PassportIdentityComponent>(donor, out var identity))
        {
            memory.PassportNumber = identity.Number;
            memory.PassportCurrentDocumentNumber = identity.CurrentDocumentNumber;
            memory.PassportName = identity.RegisteredName;
            memory.PassportCitizenship = identity.Citizenship;
            memory.PassportNeedsReview = identity.NeedsReview;
        }
        memory.SkillLevels = CompOrNull<ProfessionalSkillsComponent>(donor)?.Levels.ToArray()
            ?? new int[ProfessionalSkillRules.Count];
        if (TryComp<DetailExaminableComponent>(donor, out var detail))
        {
            memory.Description = detail.Content;
            memory.ErpStatus = detail.ERPStatus;
        }
        var record = EnsureComp<BrainRestorationRecordComponent>(brain);
        record.Species = appearance.Species.Id;
        record.Sex = appearance.Sex;
        Dirty(brain, record);
    }

    public string? OriginalVoice(EntityUid patient)
        => _records.FindRecord(patient) is { } brain
            && TryComp<BrainIdentityMemoryComponent>(brain, out var memory)
            ? memory.Appearance.Voice.Id : null;

    public void CaptureCareer(EntityUid brain, EntityUid donor)
    {
        Capture(brain, donor);
        if (!TryComp<BrainIdentityMemoryComponent>(brain, out var memory))
            return;
        if (TryComp<ProfessionalSkillsComponent>(donor, out var skills))
            memory.SkillLevels = skills.Levels.ToArray();
        if (TryComp<PassportIdentityComponent>(donor, out var identity))
        {
            memory.PassportNumber = identity.Number;
            memory.PassportCurrentDocumentNumber = identity.CurrentDocumentNumber;
            memory.PassportName = identity.RegisteredName;
            memory.PassportCitizenship = identity.Citizenship;
            memory.PassportNeedsReview = identity.NeedsReview;
        }
        memory.HadBankAccount |= HasComp<BankAccountComponent>(donor);
        RestoreCareer(brain, brain, overwriteSkills: true);
    }

    public void RestoreCareer(EntityUid brain, EntityUid carrier, bool overwriteSkills = false)
    {
        if (!TryComp<BrainIdentityMemoryComponent>(brain, out var memory))
            return;
        if (memory.SkillLevels != null && (overwriteSkills || !HasComp<ProfessionalSkillsComponent>(carrier)))
        {
            var skills = EnsureComp<ProfessionalSkillsComponent>(carrier);
            skills.Levels = memory.SkillLevels.ToArray();
            Dirty(carrier, skills);
        }
        if (memory.HadBankAccount)
            EntityManager.System<BankSystem>().RestoreCharacterAccount(carrier);
    }

    public void UpdateIssuedPassport(EntityUid person, PassportIdentityComponent identity)
    {
        foreach (var part in _body.GetBodyChildren(person))
        foreach (var organ in _body.GetPartOrgans(part.Id, part.Component))
        {
            if (!TryComp<BrainIdentityMemoryComponent>(organ.Id, out var memory))
                continue;
            memory.PassportNumber = identity.Number;
            memory.PassportCurrentDocumentNumber = identity.CurrentDocumentNumber;
        }
    }

    public bool Restore(EntityUid patient, IdentityRestorationStage stage = IdentityRestorationStage.Appearance)
    {
        if (_records.Failure(patient) != null
            || _records.FindRecord(patient) is not { } brain
            || !TryComp<BrainIdentityMemoryComponent>(brain, out var memory)
            || !TryComp<HumanoidAppearanceComponent>(patient, out var current))
            return false;
        var saved = memory.Appearance;
        if (stage == IdentityRestorationStage.Eyes)
        {
            current.EyeColor = saved.EyeColor;
            foreach (var part in _body.GetBodyChildren(patient))
            foreach (var organ in _body.GetPartOrgans(part.Id, part.Component))
            {
                if (!TryComp<DecorativeCyberEyesComponent>(organ.Id, out var eyes))
                    continue;
                eyes.IrisColor = saved.EyeColor;
                eyes.PreviousEyeColor = saved.EyeColor;
                Dirty(organ.Id, eyes);
            }
            Dirty(patient, current);
            return true;
        }
        if (stage == IdentityRestorationStage.Voice)
        {
            // Restore the exact saved voice, never a random or sex-based replacement.
            if (!_prototypes.HasIndex<TTSVoicePrototype>(saved.Voice))
                return false;
            EnsureComp<TTSComponent>(patient);
            _appearance.SetTTSVoice(patient, saved.Voice.Id, current);
            Dirty(patient, current);
            return true;
        }
        // This stage preserves eyes and voice for their subsequent surgical steps.
        // Sex, organs and cybernetic limb overrides are never replaced.
        current.MarkingSet = new MarkingSet(saved.MarkingSet);
        current.Age = saved.Age;
        current.HairColoringMode = saved.HairColoringMode;
        current.HairGradientColor = saved.HairGradientColor;
        current.HairGradientDirection = saved.HairGradientDirection;
        current.FacialHairColoringMode = saved.FacialHairColoringMode;
        current.FacialHairGradientColor = saved.FacialHairGradientColor;
        current.FacialHairGradientDirection = saved.FacialHairGradientDirection;
        _appearance.SetSkinColor(patient, saved.SkinColor, humanoid: current);
        _appearance.SetHeight(patient, saved.Height, humanoid: current);
        _appearance.SetWidth(patient, saved.Width, humanoid: current);
        _appearance.SetGender((patient, current), saved.Gender);
        Dirty(patient, current);
        _metadata.SetEntityName(patient, memory.OriginalName);
        if (memory.PassportNumber != null)
        {
            var identity = EnsureComp<PassportIdentityComponent>(patient);
            identity.Number = memory.PassportNumber;
            identity.CurrentDocumentNumber = memory.PassportCurrentDocumentNumber;
            identity.RegisteredName = memory.PassportName ?? memory.OriginalName;
            identity.Citizenship = memory.PassportCitizenship;
            identity.NeedsReview = memory.PassportNeedsReview;
        }
        RestoreCareer(brain, patient);
        EntityManager.System<DossierSystem>().RestoreFromBrain(brain, patient);
        // Null means an older record without these fields. An empty description,
        // unlike null, is intentional and must clear the recipient's old text.
        if (memory.Description != null || memory.ErpStatus != null)
        {
            var detail = EnsureComp<DetailExaminableComponent>(patient);
            if (memory.Description != null)
                detail.Content = memory.Description;
            if (memory.ErpStatus is { } status)
                detail.ERPStatus = status;
            Dirty(patient, detail);
        }
        if (memory.Dna is { } dna)
        {
            var comp = EnsureComp<DnaComponent>(patient);
            comp.DNA = dna;
            Dirty(patient, comp);
            var changed = new GenerateDnaEvent { Owner = patient, DNA = dna };
            RaiseLocalEvent(patient, ref changed);
        }
        if (memory.Fingerprint is { } fingerprint)
        {
            var comp = EnsureComp<FingerprintComponent>(patient);
            comp.Fingerprint = fingerprint;
            Dirty(patient, comp);
        }
        return true;
    }

    private void OnRestore(Entity<SurgeryRestoreIdentityComponent> ent, ref SurgeryStepEvent args)
    {
        if (args.IsCancelled)
            return;
        if (Restore(args.Body, ent.Comp.Stage))
            _popup.PopupEntity(Loc.GetString(ent.Comp.Stage == IdentityRestorationStage.Voice
                ? "restoration-complete" : "restoration-stage-complete"), args.Body, args.User);
        else
        {
            args.IsCancelled = true;
            _popup.PopupEntity(Loc.GetString(_records.Failure(args.Body)
                ?? (ent.Comp.Stage == IdentityRestorationStage.Voice
                    ? "restoration-voice-unavailable" : "restoration-no-record")), args.Body, args.User);
        }
    }
}
