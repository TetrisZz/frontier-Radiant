using Content.Server.CartridgeLoader;
using Content.Server._radint.CartridgeLoader.Cartridges;
using Content.Server._NF.RoundNotifications.Events;
using Content.Shared._radint.Cargo.BUI;
using Content.Shared._radint.Cargo.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Robust.Shared.Random;
using System.Globalization;

namespace Content.Server._radint.Cargo.Systems;

public sealed class DynamicCargoMarketSystem : EntitySystem
{
    private static readonly TimeSpan MarketUpdateInterval = TimeSpan.FromHours(1); // radiant: hourly market refresh

    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;

    private TimeSpan _timeUntilUpdate;
    private bool _roundStarted;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<DynamicCargoMarketCartridgeComponent, CartridgeUiReadyEvent>(OnCartridgeUiReady);
        SubscribeLocalEvent<DynamicCargoMarketComponent, MapInitEvent>(OnMarketMapInit);
        SubscribeLocalEvent<DynamicCargoMarketComponent, ExaminedEvent>(OnMarketExamined);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_roundStarted)
            return;

        _timeUntilUpdate += TimeSpan.FromSeconds(frameTime);
        while (_timeUntilUpdate >= MarketUpdateInterval)
        {
            _timeUntilUpdate -= MarketUpdateInterval;
            UpdateMarketRates();
        }
    }

    private void OnRoundStarted(RoundStartedEvent args)
    {
        _roundStarted = true;
        _timeUntilUpdate = TimeSpan.Zero;
        UpdateMarketRates();
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _roundStarted = false;
        _timeUntilUpdate = TimeSpan.Zero;
    }

    private void UpdateMarketRates()
    {
        var grids = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<DynamicCargoMarketComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } gridUid)
                grids.Add(gridUid);
        }

        foreach (var gridUid in grids)
            SetGridMarketRate(gridUid, GetRandomMultiplier());

        UpdateCartridges();
    }

    private void OnCartridgeUiReady(Entity<DynamicCargoMarketCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        _cartridgeLoader.UpdateCartridgeUiState(args.Loader, GetUiState());
    }

    private void OnMarketMapInit(Entity<DynamicCargoMarketComponent> ent, ref MapInitEvent args)
    {
        if (Transform(ent).GridUid is { } gridUid)
        {
            EnsureGridMarketRate(gridUid);
            UpdateCartridges();
        }
    }

    private void OnMarketExamined(Entity<DynamicCargoMarketComponent> ent, ref ExaminedEvent args)
    {
        var modifier = Transform(ent).GridUid is { } gridUid &&
                       TryComp<DynamicCargoMarketGridComponent>(gridUid, out var gridMarket)
            ? gridMarket.CurrentMultiplier
            : 1f;
        var level = modifier >= 1f ? "high" : "low";
        args.PushMarkup(Loc.GetString($"market-modifier-sell-{level}",
            ("mod", modifier.ToString("0.00", CultureInfo.InvariantCulture))));
    }

    private float GetRandomMultiplier()
    {
        return MathF.Round(_random.NextFloat(1f, 1.5f), 2);
    }

    private void EnsureGridMarketRate(EntityUid gridUid)
    {
        var gridMarket = EnsureComp<DynamicCargoMarketGridComponent>(gridUid);
        if (gridMarket.HasMarketRate)
            return;

        gridMarket.CurrentMultiplier = GetRandomMultiplier();
        gridMarket.HasMarketRate = true;
    }

    private void SetGridMarketRate(EntityUid gridUid, float multiplier)
    {
        var gridMarket = EnsureComp<DynamicCargoMarketGridComponent>(gridUid);
        gridMarket.CurrentMultiplier = multiplier;
        gridMarket.HasMarketRate = true;
    }

    private void UpdateCartridges()
    {
        var state = GetUiState();
        var query = EntityQueryEnumerator<DynamicCargoMarketCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out _, out _, out var cartridge))
        {
            if (cartridge.LoaderUid is { } loader)
                _cartridgeLoader.UpdateCartridgeUiState(loader, state);
        }
    }

    private CargoMarketRatesUiState GetUiState()
    {
        var gridRates = new Dictionary<EntityUid, float>();
        var query = EntityQueryEnumerator<DynamicCargoMarketComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid is { } gridUid)
            {
                EnsureGridMarketRate(gridUid);
                if (TryComp<DynamicCargoMarketGridComponent>(gridUid, out var market))
                    gridRates.TryAdd(gridUid, market.CurrentMultiplier);
            }
        }

        var rates = new List<CargoMarketRate>(gridRates.Count);
        foreach (var (gridUid, multiplier) in gridRates)
            rates.Add(new CargoMarketRate(MetaData(gridUid).EntityName, multiplier));

        rates.Sort((left, right) => string.Compare(left.MarketName, right.MarketName, StringComparison.Ordinal));
        return new CargoMarketRatesUiState(rates);
    }
}