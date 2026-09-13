using Robust.Shared.Localization;

namespace Content.Shared._radiant.WeaponSerial.Components;

/// <summary>
///     Маркер продавца: у магазина/вендингомата с этим компонентом купленное
///     оружие получает набитый серийный номер и попадает в реестр раунда
///     (см. WeaponSerialSystem). Список продавцов задаётся в прототипах (yml),
///     а не в C# — новый продавец добавляется одной строкой yml.
///     Вдобавок к номеру на оружие "набивается" происхождение (fluent id):
///     у аплинков ДВБ — служебное оружие, у вендинга лоджии — гражданское.
/// </summary>
[RegisterComponent]
public sealed partial class GiveSerialNumberComponent : Component
{
    /// <summary>
    ///     Fluent id надписи о происхождении, которая набивается на оружие
    ///     вместе с номером и пишется в реестр. Примеры:
    ///     gun-examine-department-dvb (аплинки ДВБ),
    ///     gun-examine-department-civilian (вендинг лоджии).
    /// </summary>
    [DataField(required: true)]
    public LocId ExamineDepartment = default!;
}
