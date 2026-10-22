using System.Linq;
using Content.Shared.Corvax.TTS;

namespace Content.Client.Corvax.TTS;

/// <summary>
/// Bounded queues: local speech is independent per speaker, radio and announcements have separate lanes.
/// </summary>
public sealed class TTSPlaybackQueue
{
    public const int MaximumLanes = 64;
    public const int MaximumPending = 8;
    private readonly Dictionary<Lane, Queue<Entry>> _queues = new();

    public readonly record struct Lane(TTSPlaybackKind Kind, NetEntity? Source);
    private readonly record struct Entry(PlayTTSEvent Message, TimeSpan ReadyAt, TimeSpan ExpiresAt);

    public static Lane GetLane(PlayTTSEvent message) =>
        new(message.Kind, message.Kind == TTSPlaybackKind.Local ? message.SourceUid : null);

    public Lane[] Lanes => _queues.Keys.ToArray();

    public void Enqueue(PlayTTSEvent message, TimeSpan now)
    {
        var lane = GetLane(message);
        if (!_queues.TryGetValue(lane, out var queue))
        {
            if (_queues.Count >= MaximumLanes)
                return;
            _queues[lane] = queue = new Queue<Entry>();
        }

        if (queue.Count >= MaximumPending)
            queue.Dequeue();
        var delay = TimeSpan.FromSeconds(Math.Clamp(message.Delay.TotalSeconds, 0, 30));
        var lifetime = message.Kind == TTSPlaybackKind.Announcement ? 120 : 15;
        queue.Enqueue(new Entry(message, now + delay, now + delay + TimeSpan.FromSeconds(lifetime)));
    }

    public bool TryDequeue(Lane lane, TimeSpan now, out PlayTTSEvent? message)
    {
        message = null;
        if (!_queues.TryGetValue(lane, out var queue))
            return false;

        while (queue.TryPeek(out var entry))
        {
            if (entry.ExpiresAt <= now)
            {
                queue.Dequeue();
                continue;
            }
            if (entry.ReadyAt > now)
                return false;

            message = queue.Dequeue().Message;
            break;
        }

        if (queue.Count == 0)
            _queues.Remove(lane);
        return message != null;
    }

    public void Clear() => _queues.Clear();
}
