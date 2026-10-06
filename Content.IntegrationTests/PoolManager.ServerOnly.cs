using System.IO;
using Content.IntegrationTests.Tests.Destructible;
using Content.IntegrationTests.Tests.DeviceNetwork;
using Content.Shared.CCVar;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.UnitTesting;

namespace Content.IntegrationTests;

public static partial class PoolManager
{
    /// <summary>
    /// Creates an isolated server for tests that do not need a graphical client.
    /// Uses the same assemblies and defaults as the RT281 test pool.
    /// The caller owns the returned instance and must dispose it.
    /// </summary>
    public static async Task<(RobustIntegrationTest.ServerIntegrationInstance, PoolTestLogHandler)> GenerateServer(
        PoolSettings settings, TextWriter output)
    {
        var log = new PoolTestLogHandler("SERVER");
        log.ActivateContext(output);
        var options = new RobustIntegrationTest.ServerIntegrationOptions
        {
            LoadTestAssembly = false,
            ContentStart = true,
            ContentAssemblies = Instance.ServerAssemblies,
            ExtraPrototypeList = Instance.TestPrototypes,
            Options = new() { LoadConfigAndUserData = false, LoadContentResources = !settings.NoLoadContent },
            OverrideLogHandler = () => log,
        };
        foreach (var (name, value) in Instance.DefaultCvars)
            options.CVarOverrides[name] = value;
        options.CVarOverrides[CCVars.GameDummyTicker.Name] = settings.DummyTicker.ToString();
        options.CVarOverrides[CCVars.GameLobbyEnabled.Name] = settings.InLobby.ToString();
        options.CVarOverrides[CCVars.GameMap.Name] = settings.Map;
        options.CVarOverrides[CCVars.AdminLogsEnabled.Name] = settings.AdminLogsEnabled.ToString();
        options.BeforeStart += () =>
        {
            var systems = IoCManager.Resolve<IEntitySystemManager>();
            systems.LoadExtraSystemType<DeviceNetworkTestSystem>();
            systems.LoadExtraSystemType<TestDestructibleListenerSystem>();
            IoCManager.Resolve<ILogManager>().GetSawmill("loc").Level = LogLevel.Error;
        };
        var server = new RobustIntegrationTest.ServerIntegrationInstance(options);
        try
        {
            await server.WaitIdleAsync();
            await server.WaitPost(() => server.CfgMan.OnValueChanged(RTCVars.FailureLogLevel,
                value => log.FailureLevel = value, true));
            return (server, log);
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }
}
