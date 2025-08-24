using Robust.Shared.GameStates;
using Content.Shared._radiant;

namespace Content.Shared.DetailExaminable;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DetailExaminableComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public string Content = string.Empty;

    [DataField("ERPStatus", required: true), AutoNetworkedField]
    [ViewVariables(VVAccess.ReadWrite)]
    public EnumERPStatus ERPStatus = EnumERPStatus.HALF;

    public string GetERPStatusName()
    {
        switch (ERPStatus)
        {
            case EnumERPStatus.HALF:
                return Loc.GetString("humanoid-erp-status-half");
            case EnumERPStatus.FULL:
                return Loc.GetString("humanoid-erp-status-full");
            default:
                return Loc.GetString("humanoid-erp-status-no");
        }
    }
}
