using System.Linq;
using System.Numerics;
using Content.Client.Items.Systems;
using Content.Shared._radiant.Paint;
using Content.Shared.Clothing;
using Content.Shared.Hands;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._radiant.Paint;

public sealed class WashablePaintVisualSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ItemSystem _items = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;

    private sealed class PaintVisuals
    {
        public required ShaderInstance Shader;
        public Color LinearColor;
        public readonly Dictionary<string, ShaderInstance> EquipmentShaders = new();
        public Color OriginalColor;
        public readonly Dictionary<SpriteComponent.Layer, (Color Color, ShaderInstance? Shader, string? Prototype)> Layers = new();
    }

    private readonly Dictionary<EntityUid, PaintVisuals> _originals = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<WashablePaintComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WashablePaintComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<WashablePaintComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WashablePaintComponent, EquipmentVisualsUpdatedEvent>(OnEquipment);
        SubscribeLocalEvent<WashablePaintComponent, HeldVisualsUpdatedEvent>(OnHeld);
    }

    private void OnStartup(Entity<WashablePaintComponent> ent, ref ComponentStartup args) => Apply(ent);
    private void OnState(Entity<WashablePaintComponent> ent, ref AfterAutoHandleStateEvent args) => Apply(ent);

    private void Apply(Entity<WashablePaintComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;
        if (!_originals.TryGetValue(ent, out var visuals))
        {
            visuals = new PaintVisuals
            {
                Shader = _prototypes.Index<ShaderPrototype>("WashablePaint").InstanceUnique(),
                OriginalColor = sprite.Color,
            };
            _originals.Add(ent, visuals);
        }
        visuals.LinearColor = Color.FromSrgb(ent.Comp.Color);
        SetPaintColor(visuals.Shader, visuals.LinearColor);
        foreach (var shader in visuals.EquipmentShaders.Values)
            SetPaintColor(shader, visuals.LinearColor);
        _sprites.SetColor((ent, sprite), new Color(1f, 1f, 1f, visuals.OriginalColor.A));
        var layers = sprite.AllLayers.ToArray();
        for (var i = 0; i < layers.Length; i++)
        {
            var layer = (SpriteComponent.Layer) layers[i];
            if (!visuals.Layers.ContainsKey(layer))
                visuals.Layers.Add(layer, (layer.Color, layer.Shader, layer.ShaderPrototype?.ToString()));
            _sprites.LayerSetColor(layer, new Color(1f, 1f, 1f, visuals.Layers[layer].Color.A));
            sprite.LayerSetShader(i, visuals.Shader, "WashablePaint");
        }
        _items.VisualsChanged(ent);
    }

    private void OnShutdown(Entity<WashablePaintComponent> ent, ref ComponentShutdown args)
    {
        if (!_originals.Remove(ent, out var visuals))
            return;
        if (TryComp<SpriteComponent>(ent, out var sprite))
        {
            _sprites.SetColor((ent, sprite), visuals.OriginalColor);
            var layers = sprite.AllLayers.ToArray();
            for (var i = 0; i < layers.Length; i++)
            {
                var layer = (SpriteComponent.Layer) layers[i];
                if (!visuals.Layers.TryGetValue(layer, out var original))
                    continue;
                _sprites.LayerSetColor(layer, original.Color);
                sprite.LayerSetShader(i, original.Shader, original.Prototype);
            }
            if (LifeStage(ent) < EntityLifeStage.Terminating)
                _items.VisualsChanged(ent);
        }
        visuals.Shader.Dispose();
        foreach (var shader in visuals.EquipmentShaders.Values)
            shader.Dispose();
    }

    private void OnEquipment(Entity<WashablePaintComponent> ent, ref EquipmentVisualsUpdatedEvent args) =>
        PaintLayers(ent, args.Equipee, args.RevealedLayers);

    private void OnHeld(Entity<WashablePaintComponent> ent, ref HeldVisualsUpdatedEvent args) =>
        PaintLayers(ent, args.User, args.RevealedLayers);

    private void PaintLayers(EntityUid item, EntityUid holder, HashSet<string> keys)
    {
        if (!_originals.TryGetValue(item, out var visuals) || !TryComp<SpriteComponent>(holder, out var sprite))
            return;
        foreach (var key in keys)
        {
            if (!_sprites.LayerMapTryGet((holder, sprite), key, out var index, false))
                continue;
            var layer = (SpriteComponent.Layer) sprite[index];
            // Leave map/mask layers untouched; recolour the displaced clothing texture itself.
            if (key.EndsWith("-displacement", StringComparison.Ordinal))
                continue;
            var prototype = layer.ShaderPrototype?.ToString() switch
            {
                null or "unshaded" or "WashablePaint" => "WashablePaint",
                "DisplacedDraw" or "WashablePaintDisplaced" => "WashablePaintDisplaced",
                "DisplacedStencilDraw" or "WashablePaintDisplacedStencil" => "WashablePaintDisplacedStencil",
                _ => null,
            };
            if (prototype == null)
                continue;
            var shader = visuals.Shader;
            if (prototype != "WashablePaint" && !visuals.EquipmentShaders.TryGetValue(prototype, out shader))
            {
                shader = _prototypes.Index<ShaderPrototype>(prototype).InstanceUnique();
                SetPaintColor(shader, visuals.LinearColor);
                visuals.EquipmentShaders.Add(prototype, shader);
            }
            _sprites.LayerSetColor(layer, new Color(1f, 1f, 1f, layer.Color.A));
            sprite.LayerSetShader(index, shader, prototype);
        }
    }

    private static void SetPaintColor(ShaderInstance shader, Color color) =>
        shader.SetParameter("paintColor", new Vector3(color.R, color.G, color.B));
}
