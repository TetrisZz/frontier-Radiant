using System;
using System.Collections.Generic;
using Content.Shared.Eui;
using Content.Shared.Paper;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Library;

/// <summary>
/// EUI state sent from server to client containing all library books.
/// </summary>
[Serializable, NetSerializable]
public sealed class AdminLibraryEuiState : EuiStateBase
{
    public List<AdminLibraryBookEntry> Books { get; }

    public AdminLibraryEuiState(List<AdminLibraryBookEntry> books)
    {
        Books = books;
    }
}

/// <summary>
/// Library book with decoded paper content and stamp data.
/// </summary>
[Serializable, NetSerializable]
public sealed class AdminLibraryBookEntry
{
    public int Id;
    public string Title;
    public string Author;
    public string Content;
    public string Date;
    public Guid AuthorPlayerUserId;
    public string AuthorPlayerUserName;

    public string? Language;
    public bool EditingDisabled;
    public string? StampState;
    public List<AdminLibraryStampEntry> Stamps;

    public AdminLibraryBookEntry(
        int id,
        string title,
        string author,
        string content,
        string date,
        Guid authorPlayerUserId,
        string authorPlayerUserName,
        string? language = null,
        bool editingDisabled = false,
        string? stampState = null,
        List<AdminLibraryStampEntry>? stamps = null)
    {
        Id = id;
        Title = title;
        Author = author;
        Content = content;
        Date = date;
        AuthorPlayerUserId = authorPlayerUserId;
        AuthorPlayerUserName = authorPlayerUserName;

        Language = language;
        EditingDisabled = editingDisabled;
        StampState = stampState;
        Stamps = stamps ?? new List<AdminLibraryStampEntry>();
    }
}

/// <summary>
/// Stamp information transferred to the admin client.
/// </summary>
[Serializable, NetSerializable]
public sealed class AdminLibraryStampEntry
{
    public string StampedName = string.Empty;
	public string StampSprite = string.Empty;
    public float Red;
    public float Green;
    public float Blue;
    public float Alpha;
    public StampType Type;
    public bool Reapply;
}

/// <summary>
/// Messages sent from the admin library client UI to the server.
/// </summary>
public static class AdminLibraryEuiMsg
{
    [Serializable, NetSerializable]
    public sealed class Close : EuiMessageBase { }

    [Serializable, NetSerializable]
    public sealed class DeleteBook : EuiMessageBase
    {
        public int BookId { get; }

        public DeleteBook(int bookId)
        {
            BookId = bookId;
        }
    }
}
