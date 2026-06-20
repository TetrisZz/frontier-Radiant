using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Preferences.Loadouts.Effects;

public sealed partial class AllowedSexesLoadoutEffect : LoadoutEffect
{
    [DataField("allowedSexes")]
    public HashSet<Sex> AllowedSexes { get; private set; } = new();

    [DataField]
    public bool Inverted;

    public override bool Validate(
        HumanoidCharacterProfile profile,
        RoleLoadout loadout,
        ICommonSession? session,
        IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        bool isAllowed = AllowedSexes.Contains(profile.Sex);

        if (Inverted)
            isAllowed = !isAllowed;

        if (isAllowed)
        {
            reason = null;
            return true;
        }

        string sexNames = string.Join(", ", AllowedSexes.Select(s =>
            Loc.GetString($"humanoid-profile-editor-sex-{s.ToString().ToLower()}-text")));

        string message = Inverted
            ? Loc.GetString("loadout-group-AllowedSexes-restriction-inverted", ("sexes", sexNames))
            : Loc.GetString("loadout-group-AllowedSexes-restriction", ("sexes", sexNames));

        reason = FormattedMessage.FromMarkup(message);
        return false;
    }

    public override void Apply(RoleLoadout loadout) { }
}
