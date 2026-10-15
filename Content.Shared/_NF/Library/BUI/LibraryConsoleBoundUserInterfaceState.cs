using System.Collections.Generic;
using Content.Shared._NF.Library.Events;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Library.BUI;

[NetSerializable, Serializable]
public sealed class LibraryConsoleBoundUserInterfaceState(
    bool enabled,
    string? bookContent = null,
    List<LibraryBookData>? books = null,
    bool uploadBlocked = false,
    int uploadCooldownSeconds = 0) : BoundUserInterfaceState
{
    /// <summary>
    /// Whether or not the buttons on the interface are enabled.
    /// </summary>
    public bool Enabled = enabled;

    /// <summary>
    /// The content of the inserted BookRandomSimple, or null if the book is not inserted.
    /// </summary>
    public string? BookContent = bookContent;

    /// <summary>
    /// All library books available on this server, for the browse tab.
    /// </summary>
    public List<LibraryBookData>? Books = books;

    public bool UploadBlocked = uploadBlocked;

    public int UploadCooldownSeconds = uploadCooldownSeconds;
}

[Serializable, NetSerializable]
public enum LibraryConsoleUiKey : byte
{
    Key,
}
