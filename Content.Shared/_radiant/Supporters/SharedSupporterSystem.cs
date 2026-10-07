using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Configuration;

namespace Content.Shared._radiant.Supporters;

public abstract class SharedSupporterSystem : EntitySystem
{
    public abstract bool HasAccess(NetUserId userId);
}

[Serializable, NetSerializable]
public sealed class SupporterStatusEvent(bool active) : EntityEventArgs
{
    public readonly bool Active = active;
}

[Serializable, NetSerializable]
public sealed class SupporterLinkCodeEvent(string command) : EntityEventArgs
{
    public readonly string Command = command;
}

[CVarDefs]
public static class SupporterCVars
{
    // Leave the existing manual unlocks working until Boosty and Discord are configured.
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("radiant.supporter_enabled", false, CVar.SERVER | CVar.REPLICATED);
    public static readonly CVarDef<string> DiscordRole =
        CVarDef.Create("radiant.supporter_discord_role", "", CVar.SERVERONLY);
    public static readonly CVarDef<bool> ShowOnline =
        CVarDef.Create("discord.show_online", true, CVar.SERVERONLY);
}
