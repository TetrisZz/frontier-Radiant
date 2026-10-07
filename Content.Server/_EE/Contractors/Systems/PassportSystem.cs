using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Content.Server.Radio.EntitySystems;
using Content.Shared._EE.Contractors.Components;
using Content.Shared._radiant.Passports;
using Content.Server._radiant.Dossiers;
using Content.Server._radiant.Medical.Surgery;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Preferences;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Network;
using Robust.Server.Player;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Map;

namespace Content.Server._EE.Contractors.Systems;

public sealed partial class PassportSystem : EntitySystem
{
    private const string PassportPrototype = "EEPassport";
    private const string NumberCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
    private const string UndocumentedTrait = "EEUndocumentedImmigrant";
    private const string ForgedTrait = "EEForgedPassport";
    private readonly Dictionary<string, string> _registry = new();
    // A temporary document has its own serial number, linked to the permanent identity.
    private readonly Dictionary<string, string> _temporaryNumbers = new();
    private readonly Dictionary<NetUserId, ReviewAlert> _reviewAlerts = new();

    private sealed class ReviewAlert(TimeSpan due)
    {
        public TimeSpan Due = due;
        public bool Sent;
        public bool Resolved;
    }

    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawn);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ =>
        {
            _registry.Clear();
            _temporaryNumbers.Clear();
            _reviewAlerts.Clear();
        });
        SubscribeLocalEvent<PassportComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<PassportComponent, BoundUIClosedEvent>(OnPassportClosed);
        SubscribeLocalEvent<PassportComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PassportComponent, AfterInteractEvent>(OnSignPaper);
        SubscribeLocalEvent<PassportVerifierComponent, AfterInteractEvent>(OnVerify);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        foreach (var (key, alert) in _reviewAlerts)
        {
            if (alert.Sent || alert.Resolved || _timing.CurTime < alert.Due ||
                !_players.TryGetSessionById(key, out var session) ||
                session.AttachedEntity is not { } person ||
                !TryComp<PassportIdentityComponent>(person, out var identity))
                continue;

            if (!identity.NeedsReview)
            {
                alert.Resolved = true;
                continue;
            }

            // A temporary system speaker keeps the warning anonymous, including radio coordinates.
            var sender = Spawn("RadiantPassportRegistrySignal", Transform(person).Coordinates);
            _radio.SendRadioMessage(sender, Loc.GetString("ee-passport-security-alert"), "Security", sender,
                hideCoordinates: true);
            QueueDel(sender);
            alert.Sent = true;
        }
    }

    private void OnPlayerSpawn(PlayerSpawnCompleteEvent args)
    {
        if (!HasComp<HumanoidAppearanceComponent>(args.Mob))
            return;
        var identity = EnsureIdentity(args.Mob, args.Profile);
        var isUndocumented = args.Profile.TraitPreferences.Contains(UndocumentedTrait);
        var hasDiscrepancy = args.Profile.TraitPreferences.Contains(ForgedTrait);
        if (hasDiscrepancy)
        {
            var key = args.Player.UserId;
            if (!_reviewAlerts.TryGetValue(key, out var alert) || alert.Resolved || alert.Sent)
            {
                alert = new ReviewAlert(_timing.CurTime + TimeSpan.FromMinutes(10));
                _reviewAlerts[key] = alert;
            }
        }
        identity.NeedsReview = hasDiscrepancy;
        if (!isUndocumented || hasDiscrepancy)
            SpawnPassportForPlayer(args.Mob, args.Profile, hasDiscrepancy);
    }

    public PassportIdentityComponent EnsureIdentity(EntityUid mob, HumanoidCharacterProfile profile)
    {
        var identity = EnsureComp<PassportIdentityComponent>(mob);
        if (identity.RegisteredName.Length == 0)
            identity.RegisteredName = MetaData(mob).EntityName;
        if (identity.Number.Length == 0)
        {
            identity.Citizenship = RadiantCitizenships.Normalize(profile.Citizenship);
            identity.Number = GenerateNumber();
        }
        if (identity.Number.Length > 0)
            _registry[identity.Number] = identity.RegisteredName;
        return identity;
    }

    public void SpawnPassportForPlayer(EntityUid mob, HumanoidCharacterProfile profile, bool flawed = false)
    {
        if (!HasComp<HumanoidAppearanceComponent>(mob))
            return;

        var identity = EnsureIdentity(mob, profile);
        if (identity.Number.Length == 0)
            identity.Number = GenerateNumber(); // Explicit issuance, including the admin command.
        if (!flawed)
            identity.NeedsReview = false;
        _registry[identity.Number] = identity.RegisteredName;

        var passport = Spawn(PassportPrototypeFor(identity.Citizenship), Transform(mob).Coordinates);
        var data = Comp<PassportComponent>(passport);
        data.Citizenship = identity.Citizenship;
        data.OwnerName = identity.RegisteredName;
        data.Species = profile.Species.Id;
        data.Sex = profile.Sex.ToString();
        data.Age = profile.Age;
        if (_prototypes.TryIndex<SpeciesPrototype>(profile.Species, out var species))
            data.HeightCm = (int)MathF.Round(profile.Height * species.AverageHeight);
        data.Number = identity.Number;
        data.FaceSignature = FaceSignature(Comp<HumanoidAppearanceComponent>(mob));
        data.Portrait = CapturePortrait(Comp<HumanoidAppearanceComponent>(mob));
        data.Residence = profile.Residence;
        data.EmergencyContact = profile.EmergencyContact;
        data.FamilyStatus = profile.FamilyStatus;
        if (flawed)
        {
            data.IsForged = true;
            ApplyDiscrepancies(data, profile.Name);
        }
        Dirty(passport, data);

        if (_inventory.TryGetSlotEntity(mob, "back", out var bag) &&
            TryComp<StorageComponent>(bag, out var storage))
            _storage.Insert(bag.Value, passport, out _, storageComp: storage, playSound: false);
    }

    /// <summary>Prints a verified replacement next to an issuing terminal.</summary>
    public EntityUid? IssuePassport(EntityUid person, EntityCoordinates output,
        string registeredName, int age, int height, string residence, string emergencyContact, string familyStatus)
    {
        if (!TryComp<PassportIdentityComponent>(person, out var identity) ||
            !TryComp<HumanoidAppearanceComponent>(person, out var appearance) ||
            !_prototypes.TryIndex<SpeciesPrototype>(appearance.Species, out var species))
            return null;

        if (identity.Number.Length == 0)
            identity.Number = GenerateNumber();
        identity.RegisteredName = MetaData(person).EntityName;
        _registry[identity.Number] = identity.RegisteredName;

        var passport = Spawn("EEPassportTemporary", output);
        var data = Comp<PassportComponent>(passport);
        data.IsTemporary = true;
        data.Citizenship = identity.Citizenship;
        data.OwnerName = registeredName;
        data.Species = appearance.Species.Id;
        data.Sex = appearance.Sex.ToString();
        data.Age = age;
        data.HeightCm = height;
        data.Number = GenerateNumber();
        _temporaryNumbers[data.Number] = identity.Number;
        _registry[data.Number] = identity.RegisteredName;
        data.FaceSignature = FaceSignature(appearance);
        data.Portrait = CapturePortrait(appearance);
        data.Residence = residence;
        data.EmergencyContact = emergencyContact;
        data.FamilyStatus = familyStatus;
        identity.NeedsReview = !MatchesIdentity(person, data) || !MatchesAppearance(person, data);
        if (!identity.NeedsReview && TryComp<ActorComponent>(person, out var actor) &&
            _reviewAlerts.TryGetValue(actor.PlayerSession.UserId, out var alert))
            alert.Resolved = true;
        identity.CurrentDocumentNumber = data.Number;
        EntityManager.System<BrainRestorationSystem>().UpdateIssuedPassport(person, identity);
        EntityManager.System<DossierSystem>().RefreshPassportNumber(person);
        Dirty(passport, data);
        return passport;
    }

    private static void ApplyDiscrepancies(PassportComponent data, string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}|{data.Number}"));
        var choices = new[] { 0, 1, 2, 3, 4 };
        for (var index = choices.Length - 1; index > 0; index--)
        {
            var other = hash[index] % (index + 1);
            (choices[index], choices[other]) = (choices[other], choices[index]);
        }

        var count = 2 + hash[5] % 2;
        foreach (var choice in choices.Take(count))
        {
            switch (choice)
            {
                case 0:
                    var chars = data.Number.ToCharArray();
                    chars[0] = chars[0] == 'A' ? 'B' : 'A';
                    data.Number = new string(chars);
                    break;
                case 1:
                    var name = data.OwnerName;
                    data.OwnerName = name.Length > 1 ? $"{name[1]}{name[0]}{name[2..]}" : name + "a";
                    break;
                case 2:
                    data.Species = data.Species == "Human" ? "Dwarf" : "Human";
                    break;
                case 3:
                    if (hash[6] % 2 == 0)
                    {
                        data.PhotoKind = PassportPhotoKind.Stranger;
                        var sex = Enum.TryParse<Sex>(data.Sex, out var parsed) ? parsed : Sex.Male;
                        data.Portrait = HumanoidCharacterAppearance.Random(data.Species, sex);
                    }
                    else
                    {
                        data.PhotoKind = PassportPhotoKind.Sketch;
                        data.Portrait = null;
                    }
                    break;
                case 4:
                    if (hash[7] % 2 == 0)
                        data.StarSeal = true;
                    else
                    {
                        var citizenships = Enum.GetValues<RadiantCitizenship>();
                        data.SealCitizenship = citizenships[
                            ((int) data.Citizenship + 1 + hash[8] % (citizenships.Length - 1)) % citizenships.Length];
                    }
                    break;
            }
        }
    }

    public bool MatchesIdentity(EntityUid person, PassportComponent passport)
        => !passport.IsForged
           && TryComp<PassportIdentityComponent>(person, out var identity)
           && identity.Citizenship == passport.Citizenship
           && identity.Number.Length > 0
           && (identity.Number == passport.Number ||
               passport.IsTemporary && _temporaryNumbers.TryGetValue(passport.Number, out var permanentNumber) &&
               permanentNumber == identity.Number)
           && identity.RegisteredName == passport.OwnerName
           && _registry.TryGetValue(passport.Number, out var registeredName)
           && registeredName == passport.OwnerName
           && MetaData(person).EntityName == identity.RegisteredName;

    private static string PassportPrototypeFor(RadiantCitizenship citizenship) => citizenship switch
    {
        RadiantCitizenship.Hesperia => "EEPassportHesperia",
        RadiantCitizenship.Aurum => "EEPassportAurum",
        RadiantCitizenship.Zon => "EEPassportZon",
        RadiantCitizenship.Midgard => "EEPassportMidgard",
        RadiantCitizenship.Avrelia => "EEPassportAvrelia",
        RadiantCitizenship.NT => "EEPassportNT",
        _ => PassportPrototype,
    };

    private bool MatchesAppearance(EntityUid person, PassportComponent passport)
    {
        if (!TryComp<HumanoidAppearanceComponent>(person, out var appearance) ||
            appearance.Species.Id != passport.Species ||
            appearance.Sex.ToString() != passport.Sex ||
            appearance.Age != passport.Age ||
            !_prototypes.TryIndex<SpeciesPrototype>(appearance.Species, out var species))
            return false;

        return (int)MathF.Round(appearance.Height * species.AverageHeight) == passport.HeightCm
               && passport.FaceSignature.Length > 0
               && FaceSignature(appearance) == passport.FaceSignature;
    }

    private static string FaceSignature(HumanoidAppearanceComponent appearance)
    {
        var signature = $"{appearance.SkinColor}|{appearance.EyeColor}";
        foreach (var category in new[]
                 {
                     MarkingCategories.Head, MarkingCategories.HeadTop, MarkingCategories.HeadSide,
                     MarkingCategories.Snout, MarkingCategories.SnoutCover,
                 })
        {
            if (!appearance.MarkingSet.Markings.TryGetValue(category, out var markings))
                continue;
            foreach (var marking in markings)
                signature += $"|{category}:{marking.MarkingId}:{string.Join(",", marking.MarkingColors)}";
        }
        return signature;
    }

    public static HumanoidCharacterAppearance CapturePortrait(HumanoidAppearanceComponent appearance)
    {
        appearance.MarkingSet.Markings.TryGetValue(MarkingCategories.Hair, out var hairMarkings);
        appearance.MarkingSet.Markings.TryGetValue(MarkingCategories.FacialHair, out var facialMarkings);
        var hair = hairMarkings?.FirstOrDefault();
        var facialHair = facialMarkings?.FirstOrDefault();
        var markings = new List<Marking>();
        foreach (var (category, entries) in appearance.MarkingSet.Markings)
        {
            if (category is MarkingCategories.Hair or MarkingCategories.FacialHair or
                MarkingCategories.UndergarmentTop or MarkingCategories.UndergarmentBottom)
                continue;
            markings.AddRange(entries.Where(entry => entry.Visible).Select(entry => new Marking(entry)));
        }

        return new HumanoidCharacterAppearance(
            hair?.MarkingId ?? HairStyles.DefaultHairStyle,
            hair is { MarkingColors.Count: > 0 } ? hair.MarkingColors[0] : Color.Black,
            facialHair?.MarkingId ?? HairStyles.DefaultFacialHairStyle,
            facialHair is { MarkingColors.Count: > 0 } ? facialHair.MarkingColors[0] : Color.Black,
            appearance.EyeColor,
            appearance.SkinColor,
            markings)
        {
            HairColoringMode = appearance.HairColoringMode,
            HairGradientColor = appearance.HairGradientColor,
            HairGradientDirection = appearance.HairGradientDirection,
            FacialHairColoringMode = appearance.FacialHairColoringMode,
            FacialHairGradientColor = appearance.FacialHairGradientColor,
            FacialHairGradientDirection = appearance.FacialHairGradientDirection,
        };
    }

    public bool HasPresentedPassport(EntityUid person)
    {
        foreach (var item in _hands.EnumerateHeld(person))
            if (TryComp<PassportComponent>(item, out var passport) &&
                MatchesIdentity(person, passport) && MatchesAppearance(person, passport))
                return true;
        return false;
    }

    private void OnSignPaper(Entity<PassportComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target ||
            !TryComp<PaperComponent>(target, out var paper))
            return;
        args.Handled = true;
        if (!MatchesIdentity(args.User, ent.Comp) || !MatchesAppearance(args.User, ent.Comp))
        {
            _popup.PopupEntity(Loc.GetString("ee-passport-not-owner"), ent.Owner, args.User);
            return;
        }

        var signed = _paper.TrySignWithPassport((target, paper), ent.Comp.OwnerName, ent.Comp.Number);
        _popup.PopupEntity(Loc.GetString(signed ? "ee-passport-signed" : "ee-passport-already-signed"),
            target, args.User);
    }

    private void OnVerify(Entity<PassportVerifierComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target ||
            !HasComp<HumanoidAppearanceComponent>(target))
            return;
        args.Handled = true;
        PassportComponent? presented = null;
        foreach (var item in _hands.EnumerateHeld(target))
            if (TryComp<PassportComponent>(item, out presented))
                break;
        if (presented == null)
            foreach (var item in _hands.EnumerateHeld(args.User))
                if (TryComp<PassportComponent>(item, out presented))
                    break;

        var result = presented == null ? "ee-passport-verify-missing"
            : presented.IsForged ? "ee-passport-verify-forged"
            : !_registry.ContainsKey(presented.Number) ? "ee-passport-verify-number"
            : TryComp<PassportIdentityComponent>(target, out var identity) &&
              identity.Citizenship != presented.Citizenship ? "ee-passport-verify-citizenship"
            : !MatchesIdentity(target, presented) ? "ee-passport-verify-name"
            : TryComp<HumanoidAppearanceComponent>(target, out var appearance) && appearance.Species.Id != presented.Species
                ? "ee-passport-verify-species"
            : MatchesAppearance(target, presented) ? "ee-passport-verify-match" : "ee-passport-verify-appearance";
        _popup.PopupEntity(Loc.GetString(result), ent.Owner, args.User);
    }

    private void OnUseInHand(Entity<PassportComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ui.SetUiState(ent.Owner, PassportUiKey.Key, new PassportUiState(
            ent.Comp.OwnerName, ent.Comp.Species, ent.Comp.Sex, ent.Comp.Age,
            ent.Comp.HeightCm, ent.Comp.Number, ent.Comp.Residence, ent.Comp.EmergencyContact,
            ent.Comp.FamilyStatus, ent.Comp.Citizenship, ent.Comp.Portrait,
            ent.Comp.PhotoKind, ent.Comp.SealCitizenship, ent.Comp.StarSeal, ent.Comp.IsTemporary));
        if (!_ui.TryOpenUi(ent.Owner, PassportUiKey.Key, args.User))
            return;
        ent.Comp.IsClosed = false;
        Dirty(ent, ent.Comp);
    }

    private void OnPassportClosed(Entity<PassportComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!Equals(args.UiKey, PassportUiKey.Key))
            return;
        ent.Comp.IsClosed = true;
        Dirty(ent, ent.Comp);
    }

    private void OnExamined(Entity<PassportComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || ent.Comp.IsClosed || ent.Comp.OwnerName.Length == 0)
            return;

        var data = ent.Comp;
        var speciesName = _prototypes.TryIndex<SpeciesPrototype>(data.Species, out var species)
            ? Loc.GetString(species.Name)
            : data.Species;

        args.PushText(Loc.GetString("ee-passport-owner", ("name", data.OwnerName)));
        if (!data.IsTemporary)
            args.PushText(Loc.GetString("ee-passport-citizenship", ("citizenship",
                Loc.GetString($"radiant-citizenship-{data.Citizenship.ToString().ToLowerInvariant()}"))));
        args.PushText(Loc.GetString("ee-passport-species", ("species", speciesName)));
        args.PushText(Loc.GetString("ee-passport-sex", ("sex", Loc.GetString("ee-passport-sex-" + data.Sex.ToLowerInvariant()))));
        args.PushText(Loc.GetString("ee-passport-age", ("age", data.Age)));
        args.PushText(Loc.GetString("ee-passport-height", ("height", data.HeightCm)));
        args.PushText(Loc.GetString("ee-passport-number", ("number", data.Number)));
        if (!string.IsNullOrWhiteSpace(data.Residence))
            args.PushText(Loc.GetString("ee-passport-residence", ("residence", data.Residence)));
        if (!string.IsNullOrWhiteSpace(data.EmergencyContact))
            args.PushText(Loc.GetString("ee-passport-emergency", ("contact", data.EmergencyContact)));
        if (!string.IsNullOrWhiteSpace(data.FamilyStatus))
            args.PushText(Loc.GetString("ee-passport-family", ("status",
                Loc.GetString(Content.Shared._radiant.Dossiers.DossierFamilyStatus.LocalizationKey(data.FamilyStatus,
                    Enum.TryParse<Content.Shared.Humanoid.Sex>(data.Sex, out var sex) ? sex : Content.Shared.Humanoid.Sex.Unsexed)))));
    }

    private string GenerateNumber()
    {
        string number;
        do
        {
            var chars = new char[17];
            for (var index = 0; index < chars.Length; index++)
                chars[index] = index is 5 or 11 ? '-' : NumberCharacters[_random.Next(NumberCharacters.Length)];
            number = new string(chars);
        } while (_registry.ContainsKey(number));
        return number;
    }
}
