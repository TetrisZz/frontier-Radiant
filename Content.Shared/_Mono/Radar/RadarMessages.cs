using System.Linq;
using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Radar;

[Serializable, NetSerializable]
public enum RadarBlipShape
{
    Circle,
    Square,
    Triangle,
    Star,
    Diamond,
    Hexagon,
    Arrow,
    Ring
}

[Serializable, NetSerializable]
public sealed class GiveBlipsEvent : EntityEventArgs
{
    /// <summary>
    /// Blips are now (grid entity, position, scale, color, shape).
    /// If grid entity is null, position is in world coordinates.
    /// If grid entity is not null, position is in grid-local coordinates.
    /// </summary>
    public readonly List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> Blips;

    // Constructor for back-compatibility
    public GiveBlipsEvent(List<(Vector2, float, Color)> blips)
    {
        Blips = blips.Select(b => ((NetEntity?)null, b.Item1, b.Item2, b.Item3, RadarBlipShape.Circle)).ToList();
    }

    public GiveBlipsEvent(List<(NetEntity? Grid, Vector2 Position, float Scale, Color Color, RadarBlipShape Shape)> blips)
    {
        Blips = blips;
    }
}

/// <summary>
/// A request for radar blips around a given entity.
/// Entity must have the RadarConsoleComponent to receive a response.
/// Requests are rate-limited server-side, unhandled messages will not receive a response.
/// </summary>
[Serializable, NetSerializable]
public sealed class RequestBlipsEvent : EntityEventArgs
{
    /// <summary>
    /// The radar entity for which blips are being requested.
    /// </summary>
    public readonly NetEntity Radar;

    /// <summary>
    /// Constructor for RequestBlipsEvent.
    /// </summary>
    /// <param name="radar">The radar entity.</param>
    public RequestBlipsEvent(NetEntity radar)
    {
        Radar = radar;
    }
}