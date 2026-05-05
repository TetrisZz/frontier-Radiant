using Robust.Shared.GameStates;
using Content.Shared._radiant;

namespace Content.Shared.DetailExaminable;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DetailExaminableComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public string Content = string.Empty;

    [DataField, AutoNetworkedField]
    public string OOCFlavorText = string.Empty;

    [DataField, AutoNetworkedField]
    public string CharacterFlavorText = string.Empty;

    [DataField, AutoNetworkedField]
    public string GreenFlavorText = string.Empty;

    [DataField, AutoNetworkedField]
    public string YellowFlavorText = string.Empty;

    [DataField, AutoNetworkedField]
    public string RedFlavorText = string.Empty;

    [DataField, AutoNetworkedField]
    public string TagsFlavorText = string.Empty;

    [DataField, AutoNetworkedField]
    public string LinksFlavorText = string.Empty;

    [DataField, AutoNetworkedField]
    public string NSFWFlavorText = string.Empty;

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
