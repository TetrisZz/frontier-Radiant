using Robust.Shared.Configuration;

namespace Content.Shared._Triad.CCVar;

[CVarDefs]
public sealed class TriadCCVars
{
    public static readonly CVarDef<string> TamperMode =
        CVarDef.Create("triad.tamper_mode", "enforce", CVar.SERVERONLY);
}
