using Content.Shared.UserInterface;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Dossiers;

[Serializable, NetSerializable]
public enum DossierUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class DossierUiState : BoundUserInterfaceState
{
    public readonly DossierKind Kind;
    public readonly byte Sex;
    public readonly Dictionary<NetEntity, string> People;
    public readonly NetEntity? Selected;
    public readonly string Name;
    public readonly string BasicDetails;
    public readonly string PassportNumber;
    public readonly DossierRecord? Record;

    public DossierUiState(DossierKind kind, byte sex, Dictionary<NetEntity, string> people, NetEntity? selected,
        string name, string basicDetails, string passportNumber, DossierRecord? record)
    {
        Kind = kind;
        Sex = sex;
        People = people;
        Selected = selected;
        Name = name;
        BasicDetails = basicDetails;
        PassportNumber = passportNumber;
        Record = record;
    }
}

[Serializable, NetSerializable]
public sealed class DossierSelectMessage(NetEntity selected) : BoundUserInterfaceMessage
{
    public readonly NetEntity Selected = selected;
}

[Serializable, NetSerializable]
public sealed class DossierEditMessage(NetEntity selected, DossierField field, string value) : BoundUserInterfaceMessage
{
    public readonly NetEntity Selected = selected;
    public readonly DossierField Field = field;
    public readonly string Value = value;
}

[Serializable, NetSerializable]
public sealed class DossierPrintMessage(NetEntity selected) : BoundUserInterfaceMessage
{
    public readonly NetEntity Selected = selected;
}

/// <summary>Per-viewer access and confidential notes; never part of the shared console state.</summary>
[Serializable, NetSerializable]
public sealed class DossierViewerAccessMessage(NetEntity selected, bool isOwnRecord, string? securityNotes)
    : BoundUserInterfaceMessage
{
    public readonly NetEntity Selected = selected;
    public readonly bool IsOwnRecord = isOwnRecord;
    public readonly string? SecurityNotes = securityNotes;
}

/// <summary>Opens the security dossier from the criminal records console.</summary>
[Serializable, NetSerializable]
public sealed class OpenSecurityDossierMessage : BoundUserInterfaceMessage;

/// <summary>Opens personal records from the tabletop station records computer.</summary>
[Serializable, NetSerializable]
public sealed class OpenPersonalDossierMessage : BoundUserInterfaceMessage;
