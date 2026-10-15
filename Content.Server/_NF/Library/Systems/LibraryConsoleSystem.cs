using System;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server._NF.Library.Components;
using Content.Server.Database;
using Content.Server.Popups;
using Content.Server.GameTicking;
using Content.Shared._NF.Library;
using Content.Shared._NF.Library.BUI;
using Content.Shared._NF.Library.Events;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Paper;
using Content.Shared.Power;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using static Content.Shared.Paper.PaperComponent;

namespace Content.Server._NF.Library.Systems;

public sealed partial class LibraryConsoleSystem : EntitySystem
{
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IServerDbManager _dbManager = default!;
    [Dependency] private ServerDbEntryManager _serverDbEntry = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AppearanceSystem _appearance = default!;

    private readonly Dictionary<EntityUid, List<LibraryBookData>> _cachedBookData = new();

    private const string BookSlotId = "bookConsole_Book";

    private static readonly TimeSpan UploadCooldown =
        TimeSpan.FromMinutes(5);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LibraryConsoleComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<LibraryConsoleComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<LibraryConsoleComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<LibraryConsoleComponent, LibraryConsoleUploadBookMessage>(OnUploadBook);
        SubscribeLocalEvent<LibraryConsoleComponent, LibraryConsoleDownloadBookMessage>(OnDownloadBook);
        SubscribeLocalEvent<LibraryConsoleComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<LibraryConsoleComponent, EntInsertedIntoContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<LibraryConsoleComponent, EntRemovedFromContainerMessage>(OnSlotChanged);
    }

    private void OnComponentInit(
        Entity<LibraryConsoleComponent> ent,
        ref ComponentInit args)
    {
        _itemSlots.AddItemSlot(ent, BookSlotId, ent.Comp.BookSlot);
    }

    private void OnComponentRemove(
        Entity<LibraryConsoleComponent> ent,
        ref ComponentRemove args)
    {
        _cachedBookData.Remove(ent.Owner);
        _itemSlots.RemoveItemSlot(ent, ent.Comp.BookSlot);
    }

    private void OnSlotChanged(
        EntityUid uid,
        LibraryConsoleComponent component,
        ContainerModifiedMessage args)
    {
        if (args.Container.ID != component.BookSlot.ID)
            return;

        UpdateUiState((uid, component));
    }

    private void OnUiOpened(
        Entity<LibraryConsoleComponent> ent,
        ref BoundUIOpenedEvent args)
    {
        UpdateUiState(ent);
    }

    /// <summary>
    /// Gets the character name.
    /// </summary>
    private string GetPlayerName(EntityUid player)
    {
        var identityEntity = player;

        if (TryComp<IdentityComponent>(player, out var identity) &&
            identity.IdentityEntitySlot.ContainedEntity is { } idEntity)
        {
            identityEntity = idEntity;
        }

        var name = MetaData(identityEntity).EntityName;

        if (string.IsNullOrWhiteSpace(name))
            name = MetaData(player).EntityName;

        return name;
    }

    private void OnUploadBook(
        Entity<LibraryConsoleComponent> ent,
        ref LibraryConsoleUploadBookMessage args)
    {
        if (!_playerManager.TryGetSessionByEntity(args.Actor, out var session))
            return;

        if (ent.Comp.UploadInProgress)
        {
            _audio.PlayPvs(ent.Comp.ErrorSound, ent.Owner);

            _popup.PopupEntity(
                Loc.GetString("library-console-upload-in-progress"),
                args.Actor,
                args.Actor);

            return;
        }

        var remaining = ent.Comp.UploadCooldownUntil - _timing.CurTime;

        if (remaining > TimeSpan.Zero)
        {
            _audio.PlayPvs(ent.Comp.ErrorSound, ent.Owner);

            _popup.PopupEntity(
                Loc.GetString(
                    "library-console-upload-cooldown",
                    ("seconds", (int)Math.Ceiling(remaining.TotalSeconds))),
                args.Actor,
                args.Actor);

            return;
        }

        var title = args.Title.Trim();

        if (string.IsNullOrWhiteSpace(title))
        {
            _audio.PlayPvs(ent.Comp.ErrorSound, ent.Owner);

            _popup.PopupEntity(
                Loc.GetString("library-console-upload-missing-fields"),
                args.Actor,
                args.Actor);

            return;
        }

        if (ent.Comp.BookSlot.Item is not { } paperEntity ||
            !TryComp<PaperComponent>(paperEntity, out var paper))
        {
            _audio.PlayPvs(ent.Comp.ErrorSound, ent.Owner);

            _popup.PopupEntity(
                Loc.GetString("library-console-upload-no-paper"),
                args.Actor,
                args.Actor);

            return;
        }

        var author = GetPlayerName(args.Actor);
        var paperContent = paper.Content;

        if (string.IsNullOrWhiteSpace(author) ||
            string.IsNullOrWhiteSpace(paperContent))
        {
            _audio.PlayPvs(ent.Comp.ErrorSound, ent.Owner);

            _popup.PopupEntity(
                Loc.GetString("library-console-upload-missing-fields"),
                args.Actor,
                args.Actor);

            return;
        }

        if (title.Length > LibraryBookLimits.MaxTitleLength ||
            author.Length > LibraryBookLimits.MaxAuthorLength ||
            paperContent.Length > LibraryBookLimits.MaxContentLength)
        {
            _audio.PlayPvs(ent.Comp.ErrorSound, ent.Owner);

            _popup.PopupEntity(
                Loc.GetString(
                    "library-console-upload-too-long",
                    ("maxTitle", LibraryBookLimits.MaxTitleLength),
                    ("maxAuthor", LibraryBookLimits.MaxAuthorLength),
                    ("maxContent", LibraryBookLimits.MaxContentLength)),
                args.Actor,
                args.Actor);

            return;
        }

        var appearanceStampState = paper.StampState ?? string.Empty;
        var appearanceStampRsi = string.Empty;

        if (TryComp<AppearanceComponent>(paperEntity, out var appearance))
        {
            if (_appearance.TryGetData<string>(
                    paperEntity,
                    PaperVisuals.Stamp,
                    out var stampState,
                    appearance))
            {
                appearanceStampState = stampState;
            }

            if (_appearance.TryGetData<string>(
                    paperEntity,
                    PaperVisuals.StampRsi,
                    out var stampRsi,
                    appearance))
            {
                appearanceStampRsi = stampRsi;
            }
        }

        // Serialize the document state into the existing database Content field.
        var paperData = new LibraryPaperSaveData
        {
            Content = paper.Content,
            Language = paper.Language,
            EditingDisabled = paper.EditingDisabled,
            StampState = paper.StampState,

            AppearanceStampState = appearanceStampState,
            AppearanceStampRsi = appearanceStampRsi,

            Stamps = paper.StampedBy.Select(stamp => new LibraryStampSaveData
            {
                StampedName = stamp.StampedName,
                StampSprite = stamp.StampSprite ?? string.Empty,
                Red = stamp.StampedColor.R,
                Green = stamp.StampedColor.G,
                Blue = stamp.StampedColor.B,
                Alpha = stamp.StampedColor.A,
                Type = stamp.Type,
                Reapply = stamp.Reapply
            }).ToList()
        };

        var serializedContent = JsonSerializer.Serialize(paperData);
        var date = DateTime.UtcNow;
        var authorPlayerUserId = session.UserId.UserId;

        // Lock console while saving.
        ent.Comp.UploadInProgress = true;
        UpdateUiState(ent);

        _ = AddBookAsync(
            ent,
            args.Actor,
            title,
            author,
            serializedContent,
            date,
            authorPlayerUserId);
    }

    private async Task AddBookAsync(
        Entity<LibraryConsoleComponent> ent,
        EntityUid actor,
        string title,
        string author,
        string content,
        DateTime date,
        Guid authorPlayerUserId)
    {
        var consoleUid = ent.Owner;
        var uploaded = false;
        var duplicate = false;

        try
        {
            var books = await _dbManager.GetNFLibraryBooksAsync();

            duplicate = books.Any(book =>
                string.Equals(
                    book.Title?.Trim(),
                    title.Trim(),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    book.Author?.Trim(),
                    author.Trim(),
                    StringComparison.OrdinalIgnoreCase));

            if (!duplicate)
            {
                var server = await _serverDbEntry.ServerEntity;

                await _dbManager.AddNFLibraryBookAsync(
                    _gameTicker.RoundId,
                    server.Id,
                    title,
                    author,
                    content,
                    date,
                    authorPlayerUserId);

                uploaded = true;
            }
        }
        catch (Exception)
        {
            if (EntityManager.EntityExists(actor))
            {
                _popup.PopupEntity(
                    Loc.GetString("library-console-upload-failed"),
                    actor,
                    actor);
            }
        }

        if (!TryComp<LibraryConsoleComponent>(consoleUid, out var component))
            return;

        component.UploadInProgress = false;

        if (uploaded)
        {
            component.UploadCooldownUntil =
                _timing.CurTime + UploadCooldown;

            ScheduleCooldownUiRefresh(consoleUid);

            _audio.PlayPvs(component.PrintSound, consoleUid);

            if (EntityManager.EntityExists(actor))
            {
                _adminLog.Add(
                    LogType.Action,
                    LogImpact.Medium,
                    $"{ToPrettyString(actor):player} uploaded book \"{title}\" by \"{author}\" to {ToPrettyString(consoleUid):entity}");
            }
        }
        else
        {
            _audio.PlayPvs(component.ErrorSound, consoleUid);

            if (duplicate && EntityManager.EntityExists(actor))
            {
                _popup.PopupEntity(
                    Loc.GetString("library-console-upload-duplicate"),
                    actor,
                    actor);
            }
        }

        UpdateUiState((consoleUid, component));
    }

    private void OnDownloadBook(
        Entity<LibraryConsoleComponent> ent,
        ref LibraryConsoleDownloadBookMessage args)
    {
        _ = DownloadBookAsync(ent, args.BookId);
    }

    private async Task DownloadBookAsync(
        Entity<LibraryConsoleComponent> ent,
        int bookId)
    {
        var books = await _dbManager.GetNFLibraryBooksAsync();
        var book = books.FirstOrDefault(b => b.Id == bookId);

        if (book == null ||
            !TryComp<LibraryConsoleComponent>(ent.Owner, out var component))
        {
            return;
        }

        if (component.BookSlot.Item is not { } paperEntity ||
            !TryComp<PaperComponent>(paperEntity, out var paper))
        {
            _audio.PlayPvs(component.ErrorSound, ent.Owner);
            return;
        }

        LibraryPaperSaveData? data;

        try
        {
            data = JsonSerializer.Deserialize<LibraryPaperSaveData>(book.Content);
        }
        catch (JsonException)
        {
            data = null;
        }

        if (data == null)
        {
            _audio.PlayPvs(component.ErrorSound, ent.Owner);

            _popup.PopupEntity(
                Loc.GetString("library-console-download-failed"),
                ent.Owner,
                ent.Owner);

            return;
        }

        // Restore the text.
        _paper.SetContent((paperEntity, paper), data.Content);

        // Restore the document state.
        paper.Language = data.Language;
        paper.EditingDisabled = data.EditingDisabled;
        paper.StampState = data.StampState;

        paper.StampedBy = data.Stamps.Select(stamp => new StampDisplayInfo
        {
            StampedName = stamp.StampedName,
            StampSprite = stamp.StampSprite,
            StampedColor = new Color(
                stamp.Red,
                stamp.Green,
                stamp.Blue,
                stamp.Alpha),
            Type = stamp.Type,
            Reapply = stamp.Reapply
        }).ToList();

        // Restore the actual sprite appearance from the database.
        var appearanceStampState = data.AppearanceStampState ?? string.Empty;
        var appearanceStampRsi = data.AppearanceStampRsi ?? string.Empty;

        if (TryComp<AppearanceComponent>(paperEntity, out var appearance))
        {
            _appearance.SetData(
                paperEntity,
                PaperVisuals.Stamp,
                appearanceStampState,
                appearance);

            _appearance.SetData(
                paperEntity,
                PaperVisuals.StampRsi,
                appearanceStampRsi,
                appearance);
        }

        // Synchronize the updated paper component.
        Dirty(paperEntity, paper);

        // Keep the physical paper and give it the document's name.
        _metaData.SetEntityName(paperEntity, book.Title);

        _audio.PlayPvs(component.PrintSound, ent.Owner);

        UpdateUiState((ent.Owner, component));
    }

    private void OnPowerChanged(
        Entity<LibraryConsoleComponent> ent,
        ref PowerChangedEvent args)
    {
        if (!args.Powered)
            _ui.CloseUi(ent.Owner, LibraryConsoleUiKey.Key);
    }

    private void UpdateUiState(Entity<LibraryConsoleComponent> ent)
    {
        _ = UpdateUiStateAsync(ent);
    }

    private void ScheduleCooldownUiRefresh(EntityUid consoleUid)
    {
        Timer.Spawn(TimeSpan.FromSeconds(1), () =>
        {
            if (!TryComp<LibraryConsoleComponent>(
                    consoleUid, out var component))
            {
                return;
            }

            UpdateCooldownUiState((consoleUid, component));

            if (component.UploadCooldownUntil > _timing.CurTime)
                ScheduleCooldownUiRefresh(consoleUid);
        });
    }

    private void UpdateCooldownUiState(
        Entity<LibraryConsoleComponent> ent)
    {
        if (!_cachedBookData.TryGetValue(ent.Owner, out var books))
        {
            UpdateUiState(ent);
            return;
        }

        var component = ent.Comp;

        var remaining = component.UploadCooldownUntil - _timing.CurTime;

        var cooldownSeconds = remaining > TimeSpan.Zero
            ? (int)Math.Ceiling(remaining.TotalSeconds)
            : 0;

        var uploadBlocked =
            component.UploadInProgress || cooldownSeconds > 0;

        string? bookContent = null;

        if (component.BookSlot.Item is { } paperEntity &&
            TryComp<PaperComponent>(paperEntity, out var paper))
        {
            bookContent = paper.Content;
        }

        var state = new LibraryConsoleBoundUserInterfaceState(
            enabled: true,
            bookContent: bookContent,
            books: books,
            uploadBlocked: uploadBlocked,
            uploadCooldownSeconds: cooldownSeconds);

        _ui.SetUiState(ent.Owner, LibraryConsoleUiKey.Key, state);
    }

    private async Task UpdateUiStateAsync(
        Entity<LibraryConsoleComponent> ent)
    {
        if (!TryComp<LibraryConsoleComponent>(
                ent.Owner, out var component))
        {
            return;
        }

        var books = await _dbManager.GetNFLibraryBooksAsync();

        if (!TryComp<LibraryConsoleComponent>(
                ent.Owner, out component))
        {
            return;
        }

        var remaining = component.UploadCooldownUntil - _timing.CurTime;

        var cooldownSeconds = remaining > TimeSpan.Zero
            ? (int)Math.Ceiling(remaining.TotalSeconds)
            : 0;

        var uploadBlocked =
            component.UploadInProgress || cooldownSeconds > 0;

        string? bookContent = null;

        if (component.BookSlot.Item is { } paperEntity &&
            TryComp<PaperComponent>(paperEntity, out var paper))
        {
            bookContent = paper.Content;
        }

        var bookData = books
            .Select(b => new LibraryBookData(
                b.Id,
                b.Title,
                b.Author,
                b.Date.ToString("yyyy-MM-dd")))
            .ToList();

        _cachedBookData[ent.Owner] = bookData;

        var state = new LibraryConsoleBoundUserInterfaceState(
            enabled: true,
            bookContent: bookContent,
            books: bookData,
            uploadBlocked: uploadBlocked,
            uploadCooldownSeconds: cooldownSeconds);

        _ui.SetUiState(ent.Owner, LibraryConsoleUiKey.Key, state);
    }

}
