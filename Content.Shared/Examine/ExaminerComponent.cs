using Content.Shared.Radiant;

namespace Content.Shared.Examine
{
    /// <summary>
    ///     Component required for a player to be able to examine things.
    /// </summary>
    [RegisterComponent]
    public sealed partial class ExaminerComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("skipChecks")]
        public bool SkipChecks = false;

        [ViewVariables(VVAccess.ReadWrite)]
        [DataField("checkInRangeUnOccluded")]
        public bool CheckInRangeUnOccluded = true;

        [DataField("ERPStatus")]
        [ViewVariables(VVAccess.ReadWrite)]
        public EnumERPStatus ERPStatus = EnumERPStatus.NO;

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
}
