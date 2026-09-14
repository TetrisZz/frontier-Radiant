using System.Linq;
using Content.Client.UserInterface.Systems.Hands.Controls;
using Content.Shared.Hands.Components;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class SurgicalHandUiTest
{
    [Test]
    public async Task ReattachedHandsKeepAnatomicalHudOrder()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            using var container = new HandsContainer();
            void Add(string name, HandLocation side) => container.AddButton(new HandButton(name, side));
            void Check() => Assert.That(container.GetButtons().Select(button => button.SlotName),
                Is.EqualTo(new[] { "right", "left" }));

            Add("left", HandLocation.Left);
            Add("right", HandLocation.Right);
            Check();
            container.RemoveButton("right");
            Assert.That(container.GetButton("right"), Is.Null);
            Add("right", HandLocation.Right);
            Check();
            container.RemoveButton("left");
            Add("left", HandLocation.Left);
            Check();
            container.Clear();
            Add("right", HandLocation.Right);
            Add("left", HandLocation.Left);
            Check();
            container.Clear();
            Add("module-b", HandLocation.Middle);
            Add("module-a", HandLocation.Middle);
            Assert.That(container.GetButtons().Select(button => button.SlotName),
                Is.EqualTo(new[] { "module-b", "module-a" }), "Same-side extra hands must retain their order.");
        });
        await pair.CleanReturnAsync();
    }
}
