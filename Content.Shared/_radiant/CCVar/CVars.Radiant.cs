using Robust.Shared.Configuration;

namespace Content.Shared._Radiant.CCVar;

[CVarDefs]
public sealed partial class RadiantCVars
{
    /// <summary>
    /// Discord Webhooks
    /// </summary>

    public static readonly CVarDef<string> BanDiscordWebhook =
        CVarDef.Create("discord.ban_webhook", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);
}
