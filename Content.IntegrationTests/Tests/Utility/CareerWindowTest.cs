using System.Linq;
using System.Numerics;
using Content.Client._radiant.Careers;
using Content.Client._NF.LateJoin.Controls;
using Content.Client._NF.LateJoin.Interfaces;
using Content.Client._NF.LateJoin.ListItems;
using Content.Shared._radiant.Careers;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class CareerWindowTest
{
    [Test]
    public async Task ProfessionListRewardsAndLiveProgress()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
        await pair.Client.WaitAssertion(() =>
        {
            using var window = new CareerWindow();
            var career = new CareerData
            {
                CharacterId = Guid.NewGuid(), Profession = "Mining", Level = 1, BoxTier = 1,
                Progress = new() { ["duty"] = 1258.132, ["gold"] = 30 },
            };
            window.UpdateState(new CareerStateEvent(career, false));
            var list = Descendants(window).OfType<ItemList>().Single();
            Assert.That(list.Count, Is.EqualTo(4));
            Assert.That(list.All(item => item.Icon != null), Is.True);
            Assert.That(Descendants(window).OfType<OptionButton>(), Is.Empty, "Other stages cannot be browsed.");
            Assert.That(Descendants(window).Single(c => c.Name == "SelectProfession").Visible, Is.False);

            var bar = Descendants(window).OfType<ProgressBar>().First();
            foreach (var size in new[] { new Vector2(620, 420), new Vector2(780, 620) })
            {
                for (var i = 0; i < 2; i++)
                {
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                }
                Assert.That(list.Size.X, Is.EqualTo(180).Within(1));
                Assert.That(bar.Size.X, Is.GreaterThan(200));
                Assert.That(bar.GlobalPosition.X, Is.GreaterThan(list.GlobalPosition.X + list.Size.X));
                Assert.That(Descendants(window).OfType<TextureRect>().Any(icon => icon.Texture != null && icon.Size.X == 32), Is.True,
                    "Reward items must have fixed-size icons");
            }

            career.Progress["duty"] = 1259.132;
            window.UpdateState(new CareerStateEvent(career, false));
            Assert.That(Descendants(window).OfType<ProgressBar>().First(), Is.SameAs(bar),
                "A timer tick must update existing controls without resetting scrolling");
            Assert.That(bar.Value, Is.EqualTo(1259.132 / 3600).Within(0.00001));

            list.Single(item => (string?) item.Metadata == "Adventurer").Selected = true;
            Assert.That(Descendants(window).OfType<ProgressBar>(), Is.Empty);
            list.Single(item => (string?) item.Metadata == "Mining").Selected = true;
            Assert.That(Descendants(window).OfType<ProgressBar>().Count(), Is.EqualTo(2));
            window.SetLoading();
            window.UpdateState(new CareerStateEvent(career, true));
            Assert.That(Descendants(window).Single(c => c.Name == "SelectProfession").Visible, Is.True);
            Assert.That(Descendants(window).OfType<OptionButton>(), Is.Empty);
            career.Level = 3;
            window.UpdateState(new CareerStateEvent(career, true));
            Assert.That(Descendants(window).OfType<ProgressBar>(), Is.Empty, "Completed stages cannot be inspected.");
            foreach (var stationTab in new[] { true, false })
            {
                using PickerControl picker = stationTab ? new StationPickerControl() : new CrewPickerControl();
                for (var id = 1; id <= 2; id++)
                {
                    var data = new System.Collections.Generic.Dictionary<NetEntity, StationJobInformation>
                    {
                        [new NetEntity(id)] = new($"Career destination {id}",
                            new System.Collections.Generic.Dictionary<ProtoId<JobPrototype>, int?> { ["ContractorInterview"] = 1 },
                            stationTab, null, null),
                    };
                    picker.UpdateUi(data);
                    Assert.That(Descendants(picker).OfType<Label>().Where(c => c.Name == "StationName")
                        .All(c => c.Text?.Contains($"Career destination {id}") == true), Is.True,
                        "A profession change must not retain the old destination.");
                    Assert.That(Descendants(picker).OfType<JobListItem>().Count(), Is.EqualTo(1));
                }
                picker.UpdateUi(new System.Collections.Generic.Dictionary<NetEntity, StationJobInformation>());
                Assert.That(Descendants(picker).OfType<JobListItem>(), Is.Empty);
            }
            TestContext.Out.WriteLine("Career list, reward icons, compact layout and live progress assertions completed");
        });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
