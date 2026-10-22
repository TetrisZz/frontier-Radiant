using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server.Chat.Systems;

/// <summary>Actual audience and remaining notification sound for an automated announcement.</summary>
[ByRefEvent]
public readonly record struct AutomaticAnnouncementEvent(string Message, Filter Recipients, SoundSpecifier? Sound);
