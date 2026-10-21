using System.Collections.Generic;
using Content.Server.Chat.Systems;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class TTSAutomaticAnnouncementRoutingTest
{
    public sealed class AnnouncementRecorder : EntitySystem
    {
        public readonly List<AutomaticAnnouncementEvent> Announcements = new();
        public override void Initialize() => SubscribeLocalEvent<AutomaticAnnouncementEvent>(OnAnnouncement);
        private void OnAnnouncement(ref AutomaticAnnouncementEvent ev) => Announcements.Add(ev);
    }

    [Test]
    public async Task NotificationsKeepTheirAudienceAndConsoleOptOutPreventsDoubleTts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var chat = entities.System<ChatSystem>();
            var recorder = entities.System<AnnouncementRecorder>();
            recorder.Announcements.Clear();
            var filter = Filter.Empty();
            chat.DispatchFilteredAnnouncement(filter, "Event warning", playSound: false,
                announcementSound: new SoundPathSpecifier("/Audio/Announcements/power_off.ogg"));
            Assert.That(recorder.Announcements.Count, Is.EqualTo(1));
            Assert.That(recorder.Announcements[0].Message, Is.EqualTo("Event warning"));
            Assert.That(recorder.Announcements[0].Recipients, Is.SameAs(filter));
            Assert.That(recorder.Announcements[0].Sound, Is.Null, "Recorded speech must not delay the synthesized notification");
            chat.DispatchFilteredAnnouncement(filter, "Player console announcement", playSound: false, playTts: false);
            Assert.That(recorder.Announcements.Count, Is.EqualTo(1), "The console already generates speech in the sender's voice");
            var signal = new SoundPathSpecifier("/Audio/Misc/notice1.ogg");
            chat.DispatchFilteredAnnouncement(filter, "Another event", playSound: false, announcementSound: signal);
            Assert.That(recorder.Announcements.Count, Is.EqualTo(2));
            Assert.That(recorder.Announcements[1].Sound, Is.SameAs(signal));
        });
        await pair.CleanReturnAsync();
    }
}
