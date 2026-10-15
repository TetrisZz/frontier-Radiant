using System;
using Content.Server._NF.Library.Systems;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Audio;

namespace Content.Server._NF.Library.Components;

/// <summary>
/// Component for the library console.
/// </summary>
[RegisterComponent]
[Access(typeof(LibraryConsoleSystem))]
public sealed partial class LibraryConsoleComponent : Component
{
    [DataField]
    public ItemSlot BookSlot = new();

    [DataField]
    public SoundSpecifier PrintSound =
        new SoundPathSpecifier("/Audio/Machines/printer.ogg");

    [DataField]
    public SoundSpecifier ErrorSound =
        new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    public TimeSpan UploadCooldownUntil = TimeSpan.Zero;

    public bool UploadInProgress;
}