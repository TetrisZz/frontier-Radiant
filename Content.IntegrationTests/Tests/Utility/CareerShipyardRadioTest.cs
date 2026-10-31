using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Shared._NF.Shipyard.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class CareerShipyardRadioTest
{
    public sealed class DeliveryRecorder : EntitySystem
    {
        public readonly List<RadioMessageDeliveredEvent> Deliveries = new();

        public override void Initialize()
        {
            SubscribeLocalEvent<RadioMessageDeliveredEvent>(OnDelivered);
        }

        private void OnDelivered(ref RadioMessageDeliveredEvent ev) => Deliveries.Add(ev);
    }

    [Test]
    public async Task CareerPurchaseAnnouncementsReachTrafficWithoutLocalTelecom()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var recorder = em.System<DeliveryRecorder>();
                var receiver = em.SpawnEntity(null, map.GridCoords);
                em.AddComponent<ActiveRadioComponent>(receiver).Channels.Add("Traffic");
                var player = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var shipyard = em.System<ShipyardSystem>();
                var announce = typeof(ShipyardSystem).GetMethod("SendPurchaseMessage",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                foreach (var prototype in new[] { "ComputerShipyardCareerMining", "ComputerShipyardCareerMedical",
                             "ComputerShipyardCareerEngineering", "ComputerShipyardCareers" })
                {
                    var console = em.SpawnEntity(prototype, map.GridCoords);
                    var channel = em.GetComponent<ShipyardConsoleComponent>(console).ShipyardChannel.Id;
                    recorder.Deliveries.Clear();
                    announce.Invoke(shipyard, new object[] { console, player, prototype, channel, false });
                    var delivery = recorder.Deliveries.Single();
                    Assert.That(delivery.Message.Channel.ID, Is.EqualTo("Traffic"), prototype);
                    Assert.That(delivery.Message.Message, Does.Contain(prototype));
                    Assert.That(delivery.Message.IsAnnouncement, Is.True);
                    Assert.That(delivery.Receivers, Does.Contain(receiver),
                        $"{prototype} must announce purchases even at an outpost without a telecom server.");
                    em.DeleteEntity(console);
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}
