using System.Diagnostics.CodeAnalysis;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Content.Shared.Roles;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Shared._radiant.Supporters;

public sealed partial class SupporterLoadoutEffect : LoadoutEffect
{
    public override bool Validate(HumanoidCharacterProfile profile, RoleLoadout loadout,
        ICommonSession? session, IDependencyCollection collection,
        [NotNullWhen(false)] out FormattedMessage? reason)
    {
        if (session != null && collection.Resolve<IEntityManager>().System<SharedSupporterSystem>().HasAccess(session.UserId))
        {
            reason = null;
            return true;
        }

        if (!collection.Resolve<IConfigurationManager>().GetCVar(SupporterCVars.Enabled))
        {
            return new JobRequirementLoadoutEffect
            {
                Requirement = new RoleTimeRequirement { Role = "JobBoxer", Time = TimeSpan.FromHours(100) },
            }.Validate(profile, loadout, session, collection, out reason);
        }

        reason = FormattedMessage.FromMarkupOrThrow(Loc.GetString("supporter-loadout-required"));
        return false;
    }
}
