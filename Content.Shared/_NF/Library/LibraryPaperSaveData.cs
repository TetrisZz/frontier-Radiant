using System.Collections.Generic;
using Content.Shared.Paper;

namespace Content.Shared._NF.Library;

public sealed class LibraryPaperSaveData
{
    public string Content { get; set; } = string.Empty;
    public string? Language { get; set; }

    public bool EditingDisabled { get; set; }
    public string? StampState { get; set; }

    public string? AppearanceStampState { get; set; }

    public string? AppearanceStampRsi { get; set; }

    public List<LibraryStampSaveData> Stamps { get; set; } = new();
}

public sealed class LibraryStampSaveData
{
    public string StampedName { get; set; } = string.Empty;
	public string StampSprite { get; set; } = string.Empty;

    public float Red { get; set; }
    public float Green { get; set; }
    public float Blue { get; set; }
    public float Alpha { get; set; }

    public StampType Type { get; set; }
    public bool Reapply { get; set; }
}