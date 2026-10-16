using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Events;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Events;
using Content.Shared._NF.Roles.Components;
using Content.Shared._radiant.Careers;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Careers;

public sealed class CareerSystem : EntitySystem
{
    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly GameTicker _ticker = default!;

    private readonly Dictionary<(NetUserId, Guid), Task> _saves = new();

    public override void Initialize()
    {
        SubscribeNetworkEvent<CareerRequestEvent>(OnRequest);
        SubscribeNetworkEvent<CareerSelectEvent>(OnSelect);
        SubscribeNetworkEvent<CareerSelectJobTitleEvent>(OnSelectJobTitle);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<IsJobAllowedEvent>(OnJobAllowed);
        SubscribeLocalEvent<GetDisallowedJobsEvent>(OnDisallowedJobs);
        SubscribeLocalEvent<StationJobsGetCandidatesEvent>(OnCandidates);
    }

    private bool TryProfile(NetUserId user, int? slot, out HumanoidCharacterProfile profile, out int index)
    {
        profile = default!;
        index = 0;
        if (!_prefs.TryGetCachedPreferences(user, out var prefs))
            return false;
        index = slot ?? prefs.SelectedCharacterIndex;
        if (!prefs.Characters.TryGetValue(index, out var raw) || raw is not HumanoidCharacterProfile humanoid)
            return false;
        profile = humanoid;
        return true;
    }

    public string GetProfession(ICommonSession session)
    {
        if (session.AttachedEntity is { } entity && TryGetCareer(entity, out var career))
            return career.Profession;
        return TryProfile(session.UserId, null, out var profile, out _) ? profile.Career.Profession : "Adventurer";
    }

    public bool IsJobAllowed(ICommonSession session, string job)
    {
        return CareerJobRules.IsAllowed(_prototypes, GetProfession(session), job);
    }

    private void OnJobAllowed(ref IsJobAllowedEvent args)
    {
        if (!IsJobAllowed(args.Player, args.JobId.Id))
            args.Cancelled = true;
    }

    private void OnDisallowedJobs(ref GetDisallowedJobsEvent args)
    {
        foreach (var job in _prototypes.EnumeratePrototypes<JobPrototype>())
        {
            if (!IsJobAllowed(args.Player, job.ID))
                args.Jobs.Add(job.ID);
        }
    }

    private void OnCandidates(ref StationJobsGetCandidatesEvent args)
    {
        if (_players.TryGetSessionById(args.Player, out var session))
            args.Jobs.RemoveAll(job => !IsJobAllowed(session, job.Id));
    }

    private void OnSpawn(PlayerSpawnCompleteEvent args)
    {
        if (HasComp<InterviewHologramComponent>(args.Mob) || args.JobId == null ||
            !TryProfile(args.Player.UserId, null, out var profile, out var slot))
            return;
        var actor = EnsureComp<CareerActorComponent>(args.Mob);
        actor.User = args.Player.UserId;
        actor.Slot = slot;
        actor.CharacterId = profile.Career.CharacterId;
        actor.Job = args.JobId;
    }

    public bool TryGetCareer(EntityUid player, out CareerData career)
    {
        career = default!;
        if (!TryComp<CareerActorComponent>(player, out var actor) ||
            !TryProfile(actor.User, actor.Slot, out var profile, out _) ||
            actor.CharacterId != profile.Career.CharacterId)
            return false;
        career = profile.Career;
        return true;
    }

    public int GetLevel(EntityUid player) => TryGetCareer(player, out var career) ? career.Level : 0;

    public bool IsWorking(EntityUid player) => TryComp<CareerActorComponent>(player, out var actor)
        && TryGetCareer(player, out var career) && career.Profession != "Adventurer"
        && _prototypes.TryIndex<CareerProfessionPrototype>(career.Profession, out var profession)
        && profession.Jobs.Contains(actor.Job);

    public IEnumerable<EntityUid> GetProgressParticipants(EntityUid player)
    {
        var participants = EntityManager.System<CareerFleetSystem>().GetParticipants(player);
        participants.Add(player);
        var profession = TryGetCareer(player, out var source) ? source.Profession : null;
        return participants.Where(member => IsWorking(member) && TryGetCareer(member, out var career)
            && career.Profession == profession).ToArray();
    }

    public bool RecordActivity(EntityUid player, CareerObjectiveType type, double amount,
        IReadOnlyCollection<string> prototypeIds, IReadOnlyCollection<string> tags, string? saleLocation = null,
        string? qualifier = null)
    {
        if (!IsWorking(player) || !TryGetCareer(player, out var career) || !double.IsFinite(amount) || amount <= 0 ||
            !_prototypes.TryIndex<CareerProfessionPrototype>(career.Profession, out var profession) ||
            career.Level < 0 || career.Level >= profession.Levels.Count)
            return false;
        var level = _prototypes.Index(profession.Levels[career.Level]);
        var changed = false;
        foreach (var objective in level.Objectives)
        {
            if (objective.Type != type ||
                objective.Prototypes.Count > 0 && !objective.Prototypes.Overlaps(prototypeIds) ||
                objective.Tags.Count > 0 && !objective.Tags.Overlaps(tags) ||
                objective.Qualifiers.Count > 0 && (qualifier == null || !objective.Qualifiers.Contains(qualifier)) ||
                type is CareerObjectiveType.SaleValue or CareerObjectiveType.SaleQuantity &&
                (saleLocation == null || !objective.SaleLocations.Contains(saleLocation)))
                continue;
            var previous = career.Progress.GetValueOrDefault(objective.Id);
            var next = Math.Min(objective.Target, previous + amount);
            if (next <= previous)
                continue;
            career.Progress[objective.Id] = next;
            changed = true;
        }
        if (!changed)
            return false;
        if (level.Objectives.All(objective => career.Progress.GetValueOrDefault(objective.Id) >= objective.Target))
        {
            career.Level++;
            career.PendingRewards.Add(career.Level);
            career.Progress.Clear();
            RaiseLocalEvent(new CareerStageChangedEvent(player));
        }
        Save(player);
        return true;
    }

    public void Save(EntityUid player)
    {
        if (!TryComp<CareerActorComponent>(player, out var actor) || !TryGetCareer(player, out var career))
            return;
        QueueSave(actor.User, actor.Slot, career);
        if (_players.TryGetSessionById(actor.User, out var session))
            SendState(session);
    }

    private void QueueSave(NetUserId user, int slot, CareerData career)
    {
        var snapshot = career.Clone();
        var key = (user, snapshot.CharacterId);
        _saves.TryGetValue(key, out var previous);
        _saves[key] = SaveAfter(previous, user, slot, snapshot);
    }

    private async Task SaveAfter(Task? previous, NetUserId user, int slot, CareerData snapshot)
    {
        try
        {
            if (previous != null)
                await previous;
            await _db.SaveCharacterCareerAsync(user, slot, snapshot.CharacterId, snapshot);
        }
        catch (Exception exception)
        {
            Log.Error($"Failed saving career for {user}/{snapshot.CharacterId}: {exception}");
        }
    }

    private void OnRequest(CareerRequestEvent args, EntitySessionEventArgs session) => SendState(session.SenderSession);

    private void OnSelectJobTitle(CareerSelectJobTitleEvent args, EntitySessionEventArgs session)
    {
        var sender = session.SenderSession;
        if (_ticker.UserHasJoinedGame(sender) || !TryProfile(sender.UserId, null, out var profile, out var slot)
            || profile.Career.CharacterId != args.CharacterId
            || !_prototypes.TryIndex<CareerProfessionPrototype>(profile.Career.Profession, out var profession))
        {
            SendState(sender, "career-change-lobby-only");
            return;
        }

        var title = profession.JobTitles.FirstOrDefault(title => title.Id == args.Title);
        if (title == null || !CareerTitleRules.IsUnlocked(title, profile.Career))
        {
            SendState(sender);
            return;
        }

        profile.Career.JobTitle = title.Id;
        QueueSave(sender.UserId, slot, profile.Career);
        SendState(sender);
    }

    public string GetJobTitle(HumanoidCharacterProfile profile, JobPrototype job)
    {
        if (_prototypes.TryIndex<CareerProfessionPrototype>(profile.Career.Profession, out var profession)
            && profession.Jobs.Contains(job.ID)
            && CareerTitleRules.GetSelected(profession, profile.Career) is { } title)
            return Loc.GetString(title.Name);
        return job.LocalizedName;
    }

    private async void OnSelect(CareerSelectEvent args, EntitySessionEventArgs session)
    {
        var sender = session.SenderSession;
        if (_ticker.UserHasJoinedGame(sender) || !TryProfile(sender.UserId, null, out var profile, out var slot) ||
            !_prototypes.TryIndex<CareerProfessionPrototype>(args.Profession, out var profession))
        {
            SendState(sender, "career-change-lobby-only");
            return;
        }
        if (profile.Career.Profession == profession.ID)
        {
            SendState(sender);
            return;
        }
        if (!args.ConfirmReset)
        {
            SendState(sender, "career-confirm-reset-required");
            return;
        }
        if (profession.RequiredSkill is { } skill && profile.SkillLevels[(int) skill] < profession.RequiredSkillLevel)
        {
            SendState(sender, "career-skill-required");
            return;
        }
        profile.Career = new CareerData { CharacterId = profile.Career.CharacterId, Profession = profession.ID };
        QueueSave(sender.UserId, slot, profile.Career);
        if (profession.Jobs.Count > 0)
        {
            // A new profession must have a candidate for round-start assignment immediately.
            try
            {
                await _prefs.SetProfile(sender.UserId, slot,
                    profile.WithJobPriority(profession.Jobs[0], JobPriority.High), validateFields: false);
                await _prefs.RefreshPreferencesAsync(sender, default);
            }
            catch (Exception exception)
            {
                Log.Error($"Failed refreshing career job preferences for {sender.UserId}: {exception}");
            }
        }
        SendState(sender);
    }

    private void SendState(ICommonSession session, string? message = null)
    {
        if (session.Status == SessionStatus.Disconnected)
            return;
        CareerData? career = null;
        if (session.AttachedEntity is { } entity && TryGetCareer(entity, out var active))
            career = active;
        else if (TryProfile(session.UserId, null, out var profile, out _))
            career = profile.Career;
        if (career != null)
            RaiseNetworkEvent(new CareerStateEvent(career.Clone(), !_ticker.UserHasJoinedGame(session),
                message == null ? null : Loc.GetString(message)), session.Channel);
    }
}
