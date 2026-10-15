using System;
using System.Collections.Generic;
using System.Text.Json;
using Content.Shared.Paper;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared._NF.Library;
using Content.Shared.Eui;
using Robust.Shared.Network;

namespace Content.Server._NF.Library;

/// <summary>
/// Server-side EUI for the admin library panel.
/// Loads all library books from the database and handles deletion requests.
/// </summary>
public sealed class AdminLibraryEui : BaseEui
{
    [Dependency] private readonly IServerDbManager _dbManager = default!;

    private List<AdminLibraryBookEntry> _books = new();

    public AdminLibraryEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        _ = LoadBooksAsync();
    }

    public override EuiStateBase GetNewState()
    {
        return new AdminLibraryEuiState(_books);
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        switch (msg)
        {
            case AdminLibraryEuiMsg.Close:
                Close();
                break;

            case AdminLibraryEuiMsg.DeleteBook deleteMsg:
                _ = DeleteBookAsync(deleteMsg.BookId);
                break;
        }
    }

    private static LibraryPaperSaveData? ReadPaperData(string content)
    {
        try
        {
            return JsonSerializer.Deserialize<LibraryPaperSaveData>(content);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task LoadBooksAsync()
    {
        var books = await _dbManager.GetNFLibraryBooksAsync();

        var booksWithNames = new List<AdminLibraryBookEntry>(books.Count);

        foreach (var book in books)
        {
            var playerRecord = await _dbManager.GetPlayerRecordByUserId(
                new NetUserId(book.AuthorPlayerUserId));

            var authorPlayerUserName = playerRecord?.LastSeenUserName ?? string.Empty;

            var paperData = ReadPaperData(book.Content);

            var stamps = paperData?.Stamps
                .Select(stamp => new AdminLibraryStampEntry
                {
                    StampedName = stamp.StampedName,
                    StampSprite = stamp.StampSprite,
                    Red = stamp.Red,
                    Green = stamp.Green,
                    Blue = stamp.Blue,
                    Alpha = stamp.Alpha,
                    Type = stamp.Type,
                    Reapply = stamp.Reapply
                })
                .ToList() ?? new List<AdminLibraryStampEntry>();

            booksWithNames.Add(new AdminLibraryBookEntry(
                book.Id,
                book.Title,
                book.Author,
                paperData?.Content ?? book.Content,
                book.Date.ToString("yyyy-MM-dd"),
                book.AuthorPlayerUserId,
                authorPlayerUserName,
                paperData?.Language,
                paperData?.EditingDisabled ?? false,
                paperData?.StampState,
                stamps));
        }

        _books = booksWithNames;

        StateDirty();
    }

    private static string GetReadablePaperContent(string storedContent)
    {
        try
        {
            var data =
                JsonSerializer.Deserialize<LibraryPaperSaveData>(storedContent);
        }
        catch (JsonException)
        {
            // Older records may contain plain text instead of JSON.
        }

        return storedContent;
    }

    private async Task DeleteBookAsync(int bookId)
    {
        await _dbManager.DeleteNFLibraryBookAsync(bookId);
        await LoadBooksAsync();
    }
}
