using System.Linq;
using Content.Client.Lobby.UI.Loadouts;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Client.Player;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class CareerLoadoutTest
{
    [TestCase("JobMedicalDoctor")]
    [TestCase("JobSalvageSpecialist")]
    public async Task CareerEquipmentWindowOpens(string role)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var collection = IoCManager.Instance!;
            var prototypes = collection.Resolve<IPrototypeManager>();
            var session = collection.Resolve<IPlayerManager>().LocalSession!;
            var profile = HumanoidCharacterProfile.DefaultWithSpecies();
            var rolePrototype = prototypes.Index<RoleLoadoutPrototype>(role);
            var underwear = new[] { "ContractorUnderwearBottom", "ContractorUnderwearSocks", "ContractorUnderwearTop" };
            Assert.That(rolePrototype.Groups.TakeLast(3).Select(id => id.Id), Is.EqualTo(underwear));
            Assert.That(rolePrototype.Groups.Any(id => id.Id == "Trinkets"), Is.True);
            var adventurer = prototypes.Index<RoleLoadoutPrototype>("JobContractor");
            foreach (var group in underwear)
                Assert.That(adventurer.Groups.Any(id => id.Id == group), Is.True);
            foreach (var group in rolePrototype.Groups.Where(id => id.Id.StartsWith("Career")))
            foreach (var item in prototypes.Index(group).Loadouts)
                Assert.That(prototypes.Index(item).Effects, Is.Empty, $"{item} must not require playtime");
            if (role == "JobMedicalDoctor")
            {
                var ears = prototypes.Index<LoadoutGroupPrototype>("CareerMedicalEars").Loadouts;
                Assert.That(ears.Select(id => prototypes.Index(id).Equipment["ears"].Id),
                    Is.EquivalentTo(new[] { "ClothingHeadsetMedical", "NFClothingWristsetMedicalCommon" }));
            }
            var loadout = new RoleLoadout(role);
            loadout.SetDefault(profile, session, prototypes);
            if (role == "JobSalvageSpecialist")
            {
                var fixedGroup = prototypes.Index<LoadoutGroupPrototype>("CareerMiningEquipment");
                Assert.That(fixedGroup.Hidden, Is.True);
                Assert.That(fixedGroup.MinLimit, Is.EqualTo(1));
                Assert.That(fixedGroup.MaxLimit, Is.EqualTo(1));
                Assert.That(fixedGroup.Loadouts.Count, Is.EqualTo(1));
                Assert.That(loadout.SelectedLoadouts[fixedGroup.ID].Count, Is.EqualTo(1));
                var gear = prototypes.Index<LoadoutPrototype>("CareerMiningEquipment");
                Assert.That(gear.Equipment["mask"].Id, Is.EqualTo("ClothingMaskGasExplorer"));
                Assert.That(gear.Equipment["belt"].Id, Is.EqualTo("ClothingBeltSalvageWebbing"));
                Assert.That(prototypes.Index<LoadoutGroupPrototype>("CareerMiningUniform").Loadouts.Count, Is.EqualTo(2));
                Assert.That(prototypes.Index<LoadoutGroupPrototype>("CareerMiningEars").Loadouts.Count, Is.EqualTo(4));
            }
            using var window = new LoadoutWindow(profile, loadout, rolePrototype, session, collection);
            Assert.DoesNotThrow(() => window.RefreshLoadouts(loadout, session, collection));
        });
        await pair.CleanReturnAsync();
    }
}
