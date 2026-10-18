using System.Linq;
using System.Numerics;
using Content.Shared._radiant.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.Utility;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;

namespace Content.Client._radiant.Humanoid;

public sealed class HairGradientSystem : EntitySystem
{
    [Dependency] private readonly IResourceCache _resources = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly Dictionary<(ResPath, RSI.StateId), Vector4> _bounds = new();
    private HashSet<(ResPath, RSI.StateId)>? _hairStates;

    public override void Initialize()
    {
        base.Initialize();
        _resources.OnRsiLoaded += OnRsiLoaded;
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        _resources.OnRsiLoaded -= OnRsiLoaded;
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _hairStates = null;
    }

    private void OnRsiLoaded(RsiLoadedEventArgs args)
    {
        _hairStates ??= _prototypes.EnumeratePrototypes<MarkingPrototype>()
            .Where(p => p.MarkingCategory is MarkingCategories.Hair or MarkingCategories.FacialHair)
            .SelectMany(p => p.Sprites.OfType<SpriteSpecifier.Rsi>())
            .Select(s => (SpriteSpecifierSerializer.TextureRoot / s.RsiPath, (RSI.StateId) s.RsiState))
            .ToHashSet();

        if (args.Atlas is not Image<Rgba32> atlas)
            return;

        foreach (var (state, directions) in args.AtlasOffsets)
        {
            var key = (args.Path, state);
            if (!_hairStates.Contains(key))
                continue;

            _bounds[key] = CalculateBounds(atlas, args.Resource.RSI.Size, directions.SelectMany(d => d));
        }
    }

    // Union all frames and directions so the gradient does not move when the hair animates.
    public static Vector4 CalculateBounds(Image<Rgba32> image, Vector2i size, IEnumerable<Vector2i> offsets)
    {
        var pixels = image.GetPixelSpan();
        var width = image.Width;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var offset in offsets.Distinct())
        {
            for (var y = 0; y < size.Y; y++)
            for (var x = 0; x < size.X; x++)
            {
                if (pixels[(offset.Y + y) * width + offset.X + x].A == 0)
                    continue;

                var uv = new Vector2((x + 0.5f) / size.X, (y + 0.5f) / size.Y);
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }
        }

        return min.X == float.MaxValue ? new Vector4(0, 0, 1, 1) : new Vector4(min.X, min.Y, max.X, max.Y);
    }

    public Vector4 GetBounds(MarkingPrototype marking)
    {
        var min = Vector2.One;
        var max = Vector2.Zero;
        foreach (var sprite in marking.Sprites.OfType<SpriteSpecifier.Rsi>())
        {
            // Loading fires OnRsiLoaded before returning, including for packed RSIs.
            var path = SpriteSpecifierSerializer.TextureRoot / sprite.RsiPath;
            _resources.GetResource<RSIResource>(path);
            if (!_bounds.TryGetValue((path, sprite.RsiState), out var bounds))
                continue;

            min = Vector2.Min(min, new Vector2(bounds.X, bounds.Y));
            max = Vector2.Max(max, new Vector2(bounds.Z, bounds.W));
        }

        return min.X > max.X ? new Vector4(0, 0, 1, 1) : new Vector4(min.X, min.Y, max.X, max.Y);
    }

    public ShaderInstance CreateShader(MarkingPrototype marking, int spriteIndex, Color primary, Color secondary,
        HairGradientDirection direction, bool displaced = false)
    {
        var shader = _prototypes.Index<ShaderPrototype>(HairColoringHelper.GradientShader).InstanceUnique();
        var first = Color.FromSrgb(primary);
        var second = Color.FromSrgb(secondary);
        shader.SetParameter("color1", new Vector3(first.R, first.G, first.B));
        shader.SetParameter("color2", new Vector3(second.R, second.G, second.B));
        shader.SetParameter("hairAlpha", primary.A);
        shader.SetParameter("direction", HairColoringHelper.DirectionToShaderParam(direction));
        shader.SetParameter("hairBounds", GetBounds(marking));
        shader.SetParameter("displaced", displaced ? 1f : 0f);
        if (marking.Sprites[spriteIndex] is SpriteSpecifier.Rsi sprite)
        {
            var size = _resources.GetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / sprite.RsiPath).RSI.Size;
            shader.SetParameter("framePixelSize", new Vector2(1f / size.X, 1f / size.Y));
        }
        return shader;
    }
}
