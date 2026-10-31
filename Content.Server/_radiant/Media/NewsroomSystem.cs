using System.Linq;
using Content.Server._CorvaxGoob.Photo;
using Content.Server.MassMedia.Systems;
using Content.Server.Power.EntitySystems;
using Content.Shared._CorvaxGoob.Photo;
using Content.Shared._radiant.Media;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.MassMedia.Components;
using Content.Shared.MassMedia.Systems;
using Content.Shared.Materials;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Media;

public sealed class NewsroomSystem : EntitySystem
{
    [Dependency] private readonly NewsSystem _news = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materials = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    private readonly Dictionary<Guid, byte[]> _photos = new();
    private readonly Dictionary<EntityUid, string> _states = new();
    private float _refresh;
    private static readonly SoundSpecifier PrintSound = new SoundPathSpecifier("/Audio/Machines/printer.ogg");

    public override void Initialize()
    {
        SubscribeLocalEvent<NewsroomComponent, BoundUIOpenedEvent>((Entity<NewsroomComponent> ent, ref BoundUIOpenedEvent ev) => UpdateUi(ent, true));
        SubscribeLocalEvent<NewsroomComponent, ComponentShutdown>((Entity<NewsroomComponent> ent, ref ComponentShutdown ev) => _states.Remove(ent.Owner));
        SubscribeLocalEvent<NewsroomComponent, NewsroomActionMessage>(OnAction);
        SubscribeLocalEvent<NewsroomComponent, InteractUsingEvent>(OnInsert);
        SubscribeLocalEvent<NewsEditionComponent, BoundUIOpenedEvent>((Entity<NewsEditionComponent> ent, ref BoundUIOpenedEvent ev) => UpdateDocument(ent));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _photos.Clear(); _states.Clear(); });
    }

    public override void Update(float frameTime)
    {
        _refresh -= frameTime;
        if (_refresh > 0) return;
        _refresh = 1;
        var query = EntityQueryEnumerator<NewsroomComponent>();
        while (query.MoveNext(out var uid, out var component))
            if (_ui.IsUiOpen(uid, NewsroomUiKey.Key)) UpdateUi((uid, component));
    }

    private bool Ready(Entity<NewsroomComponent> ent) => Transform(ent).Anchored && (!ent.Comp.NeedsPower || _power.IsPowered(ent));

    private void OnInsert(Entity<NewsroomComponent> ent, ref InteractUsingEvent ev)
    {
        if (ev.Handled || !TryComp<PhotoCardComponent>(ev.Used, out var photo)) return;
        ev.Handled = true;
        if (!Ready(ent) || photo.ImageData is not { } bytes || !NewsPhotoRules.Valid(bytes))
        {
            ent.Comp.Status = "newsroom-bad-photo";
            _popup.PopupEntity(Loc.GetString(ent.Comp.Status), ent, ev.User);
        }
        else
        {
            ent.Comp.AttachedPhoto = bytes.ToArray();
            ent.Comp.Status = "newsroom-photo-attached";
        }
        UpdateUi(ent);
    }

    private void OnAction(Entity<NewsroomComponent> ent, ref NewsroomActionMessage ev)
    {
        if (!_interaction.InRangeUnobstructed(ev.Actor, ent.Owner) || !Ready(ent)) return;
        if (ev.Action == NewsroomAction.Select) ent.Comp.Selected = ev.Article;
        if (ev.Action == NewsroomAction.ClearPhoto) ent.Comp.AttachedPhoto = null;
        if (ev.Action == NewsroomAction.Channel && ev.Channel.Length <= 32) ent.Comp.Channel = ev.Channel.Trim();
        if (ev.Action == NewsroomAction.Publish) TryPublish(ent, ev.Actor, ev.Channel, ev.Title, ev.Content);
        if (ev.Action == NewsroomAction.Print) TryPrint(ent, ev.Actor);
        UpdateUi(ent);
    }

    internal bool TryPublish(Entity<NewsroomComponent> ent, EntityUid user, string channel, string title, string content)
    {
        if (!Ready(ent) || !_interaction.InRangeUnobstructed(user, ent.Owner) || _timing.CurTime < ent.Comp.NextPublish) return false;
        channel = channel.Trim(); title = title.Trim(); content = content.Trim();
        if (channel.Length > 32 || title.Length is < 1 or > SharedNewsSystem.MaxTitleLength || content.Length is < 1 or > SharedNewsSystem.MaxContentLength)
        { ent.Comp.Status = "newsroom-invalid-article"; return false; }
        if (!SectorNewsComponent.Articles.Any(a => (a.Channel ?? "") == channel) && SectorNewsComponent.Articles.Select(a => a.Channel ?? "").Distinct().Count() >= 16)
        { ent.Comp.Status = "newsroom-channel-limit"; return false; }
        if (ent.Comp.AttachedPhoto != null && _photos.Count >= 64)
        { ent.Comp.Status = "newsroom-photo-limit"; return false; }
        if (!_news.TryAddNews(ent, title, content, out var article, Name(user), user, channel))
        { ent.Comp.Status = "newsroom-article-limit"; return false; }
        ent.Comp.NextPublish = _timing.CurTime + TimeSpan.FromSeconds(20);
        ent.Comp.Selected = article.Value.Id;
        ent.Comp.Channel = channel;
        if (ent.Comp.AttachedPhoto is { } image) _photos[article.Value.Id] = image.ToArray();
        ent.Comp.AttachedPhoto = null;
        ent.Comp.Status = "newsroom-published";
        return true;
    }

    internal EntityUid? TryPrint(Entity<NewsroomComponent> ent, EntityUid user)
    {
        if (!Ready(ent) || !_interaction.InRangeUnobstructed(user, ent.Owner) || _timing.CurTime < ent.Comp.NextPrint) return null;
        var articles = SectorNewsComponent.Articles.Where(a => ent.Comp.Channel == "" || a.Channel == ent.Comp.Channel)
            .TakeLast(5).Select(a => Article(a, true)).ToList();
        if (articles.Count == 0) { ent.Comp.Status = "newsroom-empty-edition"; return null; }
        if (!_materials.TryChangeMaterialAmount(ent, "Paper", -articles.Count * 100))
        { ent.Comp.Status = "newsroom-no-paper"; return null; }
        ent.Comp.NextPrint = _timing.CurTime + TimeSpan.FromSeconds(3);
        var paper = Spawn("RadiantNewspaper", Transform(ent).Coordinates);
        var edition = Comp<NewsEditionComponent>(paper);
        edition.Edition = ent.Comp.Channel == "" ? Loc.GetString("newsroom-edition") : ent.Comp.Channel;
        edition.Articles = articles;
        var text = "[head=1]" + FormattedMessage.EscapeText(edition.Edition) + "[/head]\n\n";
        foreach (var article in articles)
            text += $"[head=2]{FormattedMessage.EscapeText(article.Title)}[/head]\n{FormattedMessage.EscapeText(article.Author)} | {article.Time}\n{FormattedMessage.EscapeText(article.Content)}\n\n";
        _paper.SetContent(paper, text);
        UpdateDocument((paper, edition));
        _audio.PlayPvs(PrintSound, ent);
        ent.Comp.Status = "newsroom-printed";
        return paper;
    }

    private NewsEditionArticle Article(NewsArticle article, bool body)
        => new()
        {
            Id = article.Id, Title = article.Title, Author = article.Author ?? Loc.GetString("news-read-ui-no-author"),
            Channel = article.Channel ?? "", Time = article.ShareTime.ToString(@"hh\:mm\:ss"),
            Content = body ? article.Content : "", Photo = body && _photos.TryGetValue(article.Id, out var image) ? image.ToArray() : null,
        };

    private void UpdateUi(Entity<NewsroomComponent> ent, bool force = false)
    {
        var articles = SectorNewsComponent.Articles;
        var ready = Ready(ent);
        var canPublish = _timing.CurTime >= ent.Comp.NextPublish;
        var canPrint = _timing.CurTime >= ent.Comp.NextPrint;
        var paper = _materials.GetMaterialAmount(ent, "Paper") / 100;
        var signature = $"{ready}|{canPublish}|{canPrint}|{paper}|{ent.Comp.Selected}|{ent.Comp.Channel}|{ent.Comp.Status}|{ent.Comp.AttachedPhoto != null}|" +
            string.Join(';', articles.Select(a => a.Id));
        if (!force && _states.GetValueOrDefault(ent.Owner) == signature) return;
        _states[ent.Owner] = signature;
        var selected = articles.Where(a => a.Id == ent.Comp.Selected).Select(a => Article(a, true)).FirstOrDefault();
        _ui.SetUiState(ent.Owner, NewsroomUiKey.Key, new NewsroomUiState(articles.Select(a => Article(a, false)).Reverse().ToList(),
            selected, ent.Comp.Channel, ent.Comp.AttachedPhoto != null, paper, ready, canPublish, canPrint,
            ent.Comp.Status == "" ? "" : Loc.GetString(ent.Comp.Status)));
    }

    private void UpdateDocument(Entity<NewsEditionComponent> ent)
        => _ui.SetUiState(ent.Owner, NewsEditionUiKey.Key, new NewsEditionUiState(ent.Comp.Edition, ent.Comp.Articles));
}
