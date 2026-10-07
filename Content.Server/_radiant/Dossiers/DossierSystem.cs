using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using Content.Server.Body.Components;
using Content.Server.Database;
using Content.Server._EE.Contractors.Systems;
using Content.Server.Administration.Managers;
using Content.Server.Preferences.Managers;
using Content.Server._NF.CryoSleep;
using Content.Shared.Body.Systems;
using Content.Shared._radiant.Dossiers;
using Content.Shared.Access.Systems;
using Content.Shared.Administration;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Paper;
using Content.Shared.Mind;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Dossiers;

public sealed partial class DossierSystem : EntitySystem
{
    private readonly Dictionary<(NetUserId UserId, int Slot), Task> _pendingSaves = new();
    private readonly Dictionary<(NetUserId UserId, int Slot), EntityUid> _cryoArchives = new();
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IServerNetManager _net = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IAdminManager _admins = default!;
    [Dependency] private ISharedAdminLogManager _adminLogs = default!;
    [Dependency] private SharedMindSystem _minds = default!;

    public override void Initialize()
    {
        base.Initialize();
        _net.RegisterNetMessage<MsgDossierRequest>(OnLobbyRequest);
        _net.RegisterNetMessage<MsgDossierResponse>();
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<DossierHolderComponent, CryosleepEnterEvent>(OnCryoEntered);
        SubscribeLocalEvent<DossierHolderComponent, CryosleepWakeUpEvent>(OnCryoWakeUp);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<DossierConsoleComponent, GetVerbsEvent<AlternativeVerb>>(OnVerb);
        Subs.BuiEvents<DossierConsoleComponent>(DossierUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<DossierSelectMessage>(OnSelected);
            subs.Event<DossierEditMessage>(OnEdited);
            subs.Event<DossierPrintMessage>(OnPrint);
        });
    }

    private async void OnLobbyRequest(MsgDossierRequest request)
    {
        var userId = request.MsgChannel.UserId;
        var slot = request.Slot;
        if (!_preferences.TryGetCachedPreferences(userId, out var preferences) ||
            !preferences.Characters.ContainsKey(slot))
            return;

        try
        {
            if (_pendingSaves.TryGetValue((userId, slot), out var previous))
                await previous;
            var json = await _db.GetCharacterDossierAsync(userId, slot);
            var saved = JsonSerializer.Deserialize<DossierRecord>(json ?? "{}") ?? new DossierRecord();
            // Never return another character's record, nor make staff entries part of editable preferences.
            var staff = new DossierRecord
            {
                MedicalInstructions = saved.MedicalInstructions,
                MedicalRestrictions = saved.MedicalRestrictions,
                MedicalPhysiology = saved.MedicalPhysiology,
                MedicalPsychology = saved.MedicalPsychology,
                MedicalNotes = saved.MedicalNotes,
                SecurityPermissions = saved.SecurityPermissions,
                SecurityArrests = saved.SecurityArrests,
                SecurityConvictions = saved.SecurityConvictions,
                LastMedicalEditor = saved.LastMedicalEditor,
                LastSecurityEditor = saved.LastSecurityEditor,
            };
            _net.ServerSendMessage(new MsgDossierResponse
            {
                Slot = slot,
                Staff = staff,
            }, request.MsgChannel);
        }
        catch (Exception error)
        {
            Log.Error($"Could not load lobby dossier: {error}");
        }
    }

    private void OnVerb(Entity<DossierConsoleComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (ent.Comp.Kind != DossierKind.Security || !args.CanAccess || !args.CanInteract ||
            !CanUse(ent.Owner, args.User))
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("radiant-dossier-open-security"),
            Act = () => _ui.TryOpenUi(ent.Owner, DossierUiKey.Key, user),
        });
    }

    private void OnSpawn(PlayerSpawnCompleteEvent args)
    {
        if (!HasComp<HumanoidAppearanceComponent>(args.Mob) ||
            !_preferences.TryGetCachedPreferences(args.Player.UserId, out var preferences))
            return;

        var holder = EnsureComp<DossierHolderComponent>(args.Mob);
        holder.UserId = args.Player.UserId;
        holder.Slot = preferences.SelectedCharacterIndex;
        RemoveCryoArchive((holder.UserId, holder.Slot));
        holder.Record = new DossierRecord
        {
            Residence = args.Profile.Residence,
            FamilyStatus = args.Profile.FamilyStatus,
            Children = args.Profile.Children,
            EmergencyContact = args.Profile.EmergencyContact,
            DistinguishingFeatures = args.Profile.DistinguishingFeatures,
            Birthplace = args.Profile.Birthplace,
            Occupation = args.Profile.Occupation,
            Education = args.Profile.Education,
            Allergies = args.Profile.Allergies,
            MedicalHistory = args.Profile.MedicalHistory,
            BloodGroup = args.Profile.BloodGroup,
        };
        _ = LoadAsync(args.Mob, holder);
    }

    private void OnCryoEntered(Entity<DossierHolderComponent> ent, ref CryosleepEnterEvent args)
    {
        var holder = ent.Comp;
        if (!holder.Loaded || holder.Archived)
            return;

        var key = (holder.UserId, holder.Slot);
        RemoveCryoArchive(key);
        holder.HiddenInCryo = true;
        var archive = Spawn(null, MapCoordinates.Nullspace);
        var copy = EnsureComp<DossierHolderComponent>(archive);
        copy.UserId = holder.UserId;
        copy.Slot = holder.Slot;
        copy.Loaded = true;
        copy.Archived = true;
        copy.Record = holder.Record;
        copy.DisplayName = Name(ent.Owner);
        if (TryComp<HumanoidAppearanceComponent>(ent.Owner, out var appearance))
        {
            copy.Sex = appearance.Sex;
            var species = _prototypes.TryIndex<SpeciesPrototype>(appearance.Species, out var prototype)
                ? Loc.GetString(prototype.Name)
                : appearance.Species.Id;
            copy.BasicDetails = Loc.GetString("radiant-dossier-summary", ("species", species), ("age", appearance.Age));
        }
        if (TryComp<PassportIdentityComponent>(ent.Owner, out var passport))
            copy.PassportNumber = passport.DossierNumber;
        _cryoArchives[key] = archive;
    }

    private void OnCryoWakeUp(Entity<DossierHolderComponent> ent, ref CryosleepWakeUpEvent args)
    {
        ent.Comp.HiddenInCryo = false;
        RemoveCryoArchive((ent.Comp.UserId, ent.Comp.Slot));
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        foreach (var archive in _cryoArchives.Values)
            QueueDel(archive);
        _cryoArchives.Clear();
    }

    private void RemoveCryoArchive((NetUserId UserId, int Slot) key)
    {
        if (!_cryoArchives.Remove(key, out var archive))
            return;
        RemComp<DossierHolderComponent>(archive);
        QueueDel(archive);
    }

    public bool IsHiddenCryoBody(DossierHolderComponent holder)
        => holder.HiddenInCryo;

    private async Task LoadAsync(EntityUid mob, DossierHolderComponent holder)
    {
        string? json;
        try
        {
            if (_pendingSaves.TryGetValue((holder.UserId, holder.Slot), out var previous))
                await previous;
            json = await _db.GetCharacterDossierAsync(holder.UserId, holder.Slot);
        }
        catch (Exception error)
        {
            Log.Error($"Could not load character dossier: {error}");
            return;
        }

        _tasks.RunOnMainThread(() =>
        {
            if (Deleted(mob) || !TryComp<DossierHolderComponent>(mob, out var current) || current != holder)
                return;
            try
            {
                var stored = JsonSerializer.Deserialize<DossierRecord>(json ?? "{}");
                if (stored != null)
                {
                    var personal = current.Record;
                    stored.Residence = personal.Residence;
                    stored.FamilyStatus = personal.FamilyStatus;
                    stored.Children = personal.Children;
                    stored.EmergencyContact = personal.EmergencyContact;
                    stored.DistinguishingFeatures = personal.DistinguishingFeatures;
                    stored.Birthplace = personal.Birthplace;
                    stored.Occupation = personal.Occupation;
                    stored.Education = personal.Education;
                    stored.Allergies = personal.Allergies;
                    stored.MedicalHistory = personal.MedicalHistory;
                    stored.BloodGroup = personal.BloodGroup;
                    current.Record = stored;
                }
            }
            catch (JsonException error)
            {
                Log.Error($"Invalid character dossier data: {error}");
            }
            current.Loaded = true;
            RememberOnBrain(mob, current);
        });
    }

    private void OnOpened(Entity<DossierConsoleComponent> ent, ref BoundUIOpenedEvent args)
        => SendState(ent.Owner, ent.Comp, args.Actor);

    public bool TryOpenSelected(EntityUid console, EntityUid user, EntityUid target)
    {
        if (!TryComp<DossierConsoleComponent>(console, out var component) ||
            !TryComp<DossierHolderComponent>(target, out var holder) || !holder.Loaded ||
            !CanUse(console, user))
            return false;

        component.Selected = target;
        _ui.TryOpenUi(console, DossierUiKey.Key, user);
        SendState(console, component, user);
        return true;
    }

    private void OnSelected(Entity<DossierConsoleComponent> ent, ref DossierSelectMessage msg)
    {
        if (!CanUse(ent.Owner, msg.Actor) || !TryGetEntity(msg.Selected, out var target) || target == null ||
            !HasComp<DossierHolderComponent>(target.Value))
            return;
        ent.Comp.Selected = target.Value;
        SendState(ent.Owner, ent.Comp, msg.Actor);
    }

    private void OnEdited(Entity<DossierConsoleComponent> ent, ref DossierEditMessage msg)
    {
        if (!CanUse(ent.Owner, msg.Actor) || !TryGetEntity(msg.Selected, out var target) || target == null ||
            ent.Comp.Selected != target.Value || !TryComp<DossierHolderComponent>(target.Value, out var holder) ||
            !holder.Loaded || IsOwnRecord(msg.Actor, holder) || msg.Value.Length > 1024)
            return;

        var record = holder.Record;
        var value = msg.Value.Trim();
        var previous = (ent.Comp.Kind, msg.Field) switch
        {
            (DossierKind.Medical, DossierField.MedicalInstructions) => record.MedicalInstructions,
            (DossierKind.Medical, DossierField.MedicalRestrictions) => record.MedicalRestrictions,
            (DossierKind.Medical, DossierField.MedicalPhysiology) => record.MedicalPhysiology,
            (DossierKind.Medical, DossierField.MedicalPsychology) => record.MedicalPsychology,
            (DossierKind.Medical, DossierField.MedicalNotes) => record.MedicalNotes,
            (DossierKind.Security, DossierField.SecurityPermissions) => record.SecurityPermissions,
            (DossierKind.Security, DossierField.SecurityArrests) => record.SecurityArrests,
            (DossierKind.Security, DossierField.SecurityConvictions) => record.SecurityConvictions,
            (DossierKind.Security, DossierField.SecurityNotes) => record.SecurityNotes,
            _ => null,
        };
        if (previous == null || previous == value)
            return;

        if (ent.Comp.Kind == DossierKind.Medical)
        {
            switch (msg.Field)
            {
                case DossierField.MedicalInstructions: record.MedicalInstructions = value; break;
                case DossierField.MedicalRestrictions: record.MedicalRestrictions = value; break;
                case DossierField.MedicalPhysiology: record.MedicalPhysiology = value; break;
                case DossierField.MedicalPsychology: record.MedicalPsychology = value; break;
                case DossierField.MedicalNotes: record.MedicalNotes = value; break;
                default: return;
            }
            record.LastMedicalEditor = Name(msg.Actor);
        }
        else if (ent.Comp.Kind == DossierKind.Security)
        {
            switch (msg.Field)
            {
                case DossierField.SecurityPermissions: record.SecurityPermissions = value; break;
                case DossierField.SecurityArrests: record.SecurityArrests = value; break;
                case DossierField.SecurityConvictions: record.SecurityConvictions = value; break;
                case DossierField.SecurityNotes: record.SecurityNotes = value; break;
                default: return;
            }
            record.LastSecurityEditor = Name(msg.Actor);
        }
        else return;

        var key = (holder.UserId, holder.Slot);
        _pendingSaves[key] = SaveAsync(_pendingSaves.GetValueOrDefault(key, Task.CompletedTask),
            holder.UserId, holder.Slot, JsonSerializer.Serialize(record));
        _adminLogs.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(msg.Actor):player} edited {ent.Comp.Kind} dossier field {msg.Field} " +
            $"for {ToPrettyString(target.Value):target} (account {holder.UserId}, slot {holder.Slot}) " +
            $"via {ToPrettyString(ent.Owner):console}; " +
            $"old ({previous.Length} chars): '{AuditExcerpt(previous)}', " +
            $"new ({value.Length} chars): '{AuditExcerpt(value)}'");
        RememberOnBrain(target.Value, holder);
        SendState(ent.Owner, ent.Comp, msg.Actor);
    }

    private static string AuditExcerpt(string value)
    {
        const int maxLength = 120;
        var excerpt = value.Length <= maxLength ? value : value[..maxLength] + "…";
        return excerpt.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    private void OnPrint(Entity<DossierConsoleComponent> ent, ref DossierPrintMessage msg)
    {
        if (!CanUse(ent.Owner, msg.Actor) || !TryGetEntity(msg.Selected, out var target) || target == null ||
            ent.Comp.Selected != target.Value || !TryComp<DossierHolderComponent>(target.Value, out var holder) ||
            !holder.Loaded || IsHiddenCryoBody(holder))
            return;

        var name = holder.DisplayName ?? Name(target.Value);
        var prototype = ent.Comp.Kind switch
        {
            DossierKind.Security => "RadiantPrintedSecurityDossier",
            DossierKind.Medical => "RadiantPrintedMedicalDossier",
            _ => "RadiantPrintedDossier",
        };
        var printout = Spawn(prototype, Transform(ent.Owner).Coordinates);
        var printoutInfo = Comp<DossierPrintoutComponent>(printout);
        printoutInfo.SubjectName = name;
        printoutInfo.PassportNumber = TryComp<PassportIdentityComponent>(target.Value, out var passport)
            ? passport.DossierNumber
            : holder.PassportNumber;
        if (TryComp<HumanoidAppearanceComponent>(target.Value, out var appearance))
        {
            printoutInfo.Portrait = PassportSystem.CapturePortrait(appearance);
            printoutInfo.Species = appearance.Species.Id;
            printoutInfo.Sex = appearance.Sex;
            printoutInfo.Age = appearance.Age;
        }
        Dirty(printout, printoutInfo);
        _metadata.SetEntityName(printout, Loc.GetString("radiant-dossier-printout-name", ("name", name)));
        _metadata.SetEntityDescription(printout, Loc.GetString("radiant-dossier-printout-description"));
        _paper.SetContent(printout, MakePrintout(ent.Comp.Kind, target.Value, holder, name,
            printoutInfo.PassportNumber));
    }

    private string MakePrintout(DossierKind kind, EntityUid target, DossierHolderComponent holder, string name,
        string passport)
    {
        var record = holder.Record;
        var text = new StringBuilder();
        var sex = holder.Sex;
        var details = holder.BasicDetails;
        if (TryComp<HumanoidAppearanceComponent>(target, out var appearance))
        {
            sex = appearance.Sex;
            var species = _prototypes.TryIndex<SpeciesPrototype>(appearance.Species, out var prototype)
                ? Loc.GetString(prototype.Name)
                : appearance.Species.Id;
            details = Loc.GetString("radiant-dossier-summary", ("species", species), ("age", appearance.Age));
        }
        var title = Loc.GetString(kind switch
        {
            DossierKind.Medical => "radiant-dossier-print-title-medical",
            DossierKind.Security => "radiant-dossier-print-title-security",
            _ => "radiant-dossier-print-title-personal",
        });
        var accent = kind == DossierKind.Security ? "#713a35" : "#35566b";
        text.Append("[color=").Append(accent).Append("][bold]")
            .Append(Loc.GetString("radiant-dossier-print-agency"))
            .AppendLine("[/bold][/color]");
        text.Append("[bold]").Append(title).AppendLine("[/bold]");
        if (kind == DossierKind.Security)
            text.Append("[color=#9a3c38][bold]")
                .Append(Loc.GetString("radiant-dossier-print-classified"))
                .AppendLine("[/bold][/color]");
        text.AppendLine("────────────────────────────");

        void Section(string key)
        {
            text.AppendLine();
            text.Append("[color=").Append(accent).Append("][bold]")
                .Append(Loc.GetString(key)).AppendLine("[/bold][/color]");
            text.AppendLine("────────────────────────────");
        }

        void Field(string label, string value)
        {
            text.Append("[bold]").Append(Loc.GetString(label)).Append("[/bold]: ")
                .AppendLine(string.IsNullOrWhiteSpace(value) ? "[form]" : FormattedMessage.EscapeText(value));
        }

        Section("radiant-dossier-print-identity");
        Field("radiant-dossier-print-name", name);
        Field("radiant-dossier-passport-number", passport);
        Field("radiant-dossier-print-species-age", details);
        Field("radiant-dossier-birthplace", record.Birthplace);
        Field("radiant-dossier-residence", record.Residence);

        if (kind == DossierKind.Personal)
        {
            Section("radiant-dossier-print-background");
            Field("radiant-dossier-family", Loc.GetString(DossierFamilyStatus.LocalizationKey(record.FamilyStatus, sex)));
            Field("radiant-dossier-occupation", record.Occupation);
            Field("radiant-dossier-education", record.Education);
            Section("radiant-dossier-print-contacts");
            Field("radiant-dossier-emergency", record.EmergencyContact);
            Field("radiant-dossier-features", record.DistinguishingFeatures);
            Section("radiant-dossier-print-health");
            Field("radiant-dossier-blood-group", record.BloodGroup);
            Field("radiant-dossier-allergies", record.Allergies);
            Field("radiant-dossier-medical-history", record.MedicalHistory);
        }
        else if (kind == DossierKind.Medical)
        {
            Section("radiant-dossier-print-health");
            Field("radiant-dossier-blood-group", record.BloodGroup);
            Field("radiant-dossier-allergies", record.Allergies);
            Field("radiant-dossier-medical-history", record.MedicalHistory);
            Section("radiant-dossier-medical-section");
            Field("radiant-dossier-instructions", record.MedicalInstructions);
            Field("radiant-dossier-restrictions", record.MedicalRestrictions);
            Field("radiant-dossier-physiology", record.MedicalPhysiology);
            Field("radiant-dossier-psychology", record.MedicalPsychology);
            Field("radiant-dossier-medical-notes", record.MedicalNotes);
        }
        else
        {
            Section("radiant-dossier-print-background");
            Field("radiant-dossier-occupation", record.Occupation);
            Field("radiant-dossier-education", record.Education);
            Field("radiant-dossier-features", record.DistinguishingFeatures);
            Section("radiant-dossier-print-clearance");
            Field("radiant-dossier-permissions", record.SecurityPermissions);
            Section("radiant-dossier-print-history");
            Field("radiant-dossier-arrests", record.SecurityArrests);
            Field("radiant-dossier-convictions", record.SecurityConvictions);
        }

        text.AppendLine();
        text.AppendLine("────────────────────────────");
        var editor = kind == DossierKind.Security ? record.LastSecurityEditor : record.LastMedicalEditor;
        if (kind != DossierKind.Personal && !string.IsNullOrWhiteSpace(editor))
            text.AppendLine(FormattedMessage.EscapeText(Loc.GetString("radiant-dossier-last-edit", ("name", editor))));
        text.Append("[italic]").Append(Loc.GetString("radiant-dossier-print-signature"))
            .AppendLine("[/italic] [form]");

        return text.ToString();
    }

    private void RememberOnBrain(EntityUid mob, DossierHolderComponent holder)
    {
        foreach (var part in _body.GetBodyChildren(mob))
        foreach (var organ in _body.GetPartOrgans(part.Id, part.Component))
        {
            if (!HasComp<BrainComponent>(organ.Id))
                continue;
            var memory = EnsureComp<BrainDossierMemoryComponent>(organ.Id);
            memory.Source = mob;
            memory.UserId = holder.UserId;
            memory.Slot = holder.Slot;
            memory.Record = holder.Record;
        }
    }

    public void RestoreFromBrain(EntityUid brain, EntityUid patient)
    {
        if (!TryComp<BrainDossierMemoryComponent>(brain, out var memory))
            return;
        if (memory.Source is { } previous && previous != patient && !Deleted(previous))
            RemComp<DossierHolderComponent>(previous);
        var holder = EnsureComp<DossierHolderComponent>(patient);
        holder.UserId = memory.UserId;
        holder.Slot = memory.Slot;
        holder.Record = memory.Record;
        holder.Loaded = true;
        memory.Source = patient;
    }

    private async Task SaveAsync(Task previous, NetUserId userId, int slot, string json)
    {
        await previous;
        try { await _db.SaveCharacterDossierAsync(userId, slot, json); }
        catch (Exception error) { Log.Error($"Could not save character dossier: {error}"); }
    }

    private bool CanUse(EntityUid console, EntityUid user) => _access.IsAllowed(user, console);

    private bool IsOwnRecord(EntityUid user, DossierHolderComponent holder)
    {
        if (!_players.TryGetSessionByEntity(user, out var session))
            return true;

        EntityUid? character = user;
        if (HasComp<GhostComponent>(user) && _minds.TryGetMind(user, out _, out var mind))
            character = mind.CurrentEntity;

        if (character is { } current && TryComp<DossierHolderComponent>(current, out var currentHolder))
            return currentHolder.UserId == holder.UserId && currentHolder.Slot == holder.Slot;

        // A ghost without a living body still belongs only to the selected character,
        // not every character ever used by the same account.
        return session.UserId == holder.UserId &&
            (!_preferences.TryGetCachedPreferences(session.UserId, out var preferences) ||
             preferences.SelectedCharacterIndex == holder.Slot);
    }

    private bool IsAdminGhost(EntityUid user)
        => TryComp<GhostComponent>(user, out var ghost) && ghost.CanGhostInteract &&
           _players.TryGetSessionByEntity(user, out var session) &&
           _admins.HasAdminFlag(session, AdminFlags.Admin);

    /// <summary>Refreshes open dossiers after a replacement passport is issued.</summary>
    public void RefreshPassportNumber(EntityUid person)
    {
        var query = EntityQueryEnumerator<DossierConsoleComponent>();
        while (query.MoveNext(out var console, out var component))
        {
            if (component.Selected != person)
                continue;
            foreach (var viewer in _ui.GetActors(console, DossierUiKey.Key))
            {
                if (!CanUse(console, viewer))
                    continue;
                SendState(console, component, viewer);
                break;
            }
        }
    }

    private void SendState(EntityUid console, DossierConsoleComponent component, EntityUid user)
    {
        if (!CanUse(console, user))
            return;
        var people = new Dictionary<NetEntity, string>();
        var query = EntityQueryEnumerator<DossierHolderComponent>();
        while (query.MoveNext(out var uid, out var holder))
        {
            if (!holder.Loaded || IsHiddenCryoBody(holder))
                continue;
            people[GetNetEntity(uid)] = holder.DisplayName ?? Name(uid);
        }

        DossierRecord? shown = null;
        NetEntity? selected = null;
        var name = "";
        var details = "";
        var passportNumber = "";
        var sex = Sex.Unsexed;
        DossierHolderComponent? holderForAccess = null;
        if (component.Selected is { } target && TryComp<DossierHolderComponent>(target, out var selectedHolder) &&
            selectedHolder.Loaded)
        {
            holderForAccess = selectedHolder;
            selected = GetNetEntity(target);
            name = selectedHolder.DisplayName ?? Name(target);
            if (component.Kind == DossierKind.Personal && TryComp<HumanoidAppearanceComponent>(target, out var appearance))
            {
                sex = appearance.Sex;
                var species = _prototypes.TryIndex<SpeciesPrototype>(appearance.Species, out var prototype)
                    ? Loc.GetString(prototype.Name)
                    : appearance.Species.Id;
                details = Loc.GetString("radiant-dossier-summary", ("species", species), ("age", appearance.Age));
            }
            else if (selectedHolder.Archived)
            {
                sex = selectedHolder.Sex;
                details = selectedHolder.BasicDetails;
            }
            var source = selectedHolder.Record;
            shown = new DossierRecord();
            if (TryComp<PassportIdentityComponent>(target, out var passportIdentity))
                passportNumber = passportIdentity.DossierNumber;
            else if (selectedHolder.Archived)
                passportNumber = selectedHolder.PassportNumber;
            if (component.Kind == DossierKind.Personal)
            {
                shown.Residence = source.Residence;
                shown.FamilyStatus = source.FamilyStatus;
                shown.EmergencyContact = source.EmergencyContact;
                shown.DistinguishingFeatures = source.DistinguishingFeatures;
                shown.Birthplace = source.Birthplace;
                shown.Occupation = source.Occupation;
                shown.Education = source.Education;
                shown.Allergies = source.Allergies;
                shown.MedicalHistory = source.MedicalHistory;
                shown.BloodGroup = source.BloodGroup;
            }
            else if (component.Kind == DossierKind.Medical)
            {
                shown.MedicalInstructions = source.MedicalInstructions;
                shown.MedicalRestrictions = source.MedicalRestrictions;
                shown.MedicalPhysiology = source.MedicalPhysiology;
                shown.MedicalPsychology = source.MedicalPsychology;
                shown.MedicalNotes = source.MedicalNotes;
                shown.LastMedicalEditor = source.LastMedicalEditor;
            }
            else if (component.Kind == DossierKind.Security)
            {
                shown.SecurityPermissions = source.SecurityPermissions;
                shown.SecurityArrests = source.SecurityArrests;
                shown.SecurityConvictions = source.SecurityConvictions;
                shown.LastSecurityEditor = source.LastSecurityEditor;
            }
        }
        _ui.SetUiState(console, DossierUiKey.Key,
            new DossierUiState(component.Kind, (byte) sex, people, selected, name, details, passportNumber, shown));
        if (selected is not { } selectedEntity || holderForAccess == null)
            return;

        foreach (var viewer in _ui.GetActors(console, DossierUiKey.Key))
        {
            if (!CanUse(console, viewer))
                continue;
            var own = IsOwnRecord(viewer, holderForAccess);
            var notes = component.Kind == DossierKind.Security && (!own || IsAdminGhost(viewer))
                ? holderForAccess.Record.SecurityNotes
                : null;
            _ui.ServerSendUiMessage(console, DossierUiKey.Key,
                new DossierViewerAccessMessage(selectedEntity, own, notes), viewer);
        }
    }
}

[RegisterComponent]
public sealed partial class DossierHolderComponent : Component
{
    public NetUserId UserId;
    public int Slot;
    public bool Loaded;
    public DossierRecord Record = new();
    public bool Archived;
    public bool HiddenInCryo;
    public string? DisplayName;
    public Sex Sex = Sex.Unsexed;
    public string BasicDetails = "";
    public string PassportNumber = "";
}

[RegisterComponent]
public sealed partial class BrainDossierMemoryComponent : Component
{
    public EntityUid? Source;
    public NetUserId UserId;
    public int Slot;
    public DossierRecord Record = new();
}
