using System.IO;
using System.Reflection;
using Content.Client.IoC;
using Content.Client.Parallax.Managers;
using Robust.Client;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.UnitTesting;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class SandboxTest
{
    [Test]
    public async Task AssembliesWithoutContentStartup()
    {
        // Keep assembly safety checks independent of unrelated prototype or sprite failures.
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            // Use content-repository paths, but load neither content assemblies nor content prototypes at startup.
            ContentStart = true,
            Options = new GameControllerOptions { LoadConfigAndUserData = false, LoadContentResources = false },
        };
        options.BeforeStart += () =>
        {
            var factory = IoCManager.Resolve<IComponentFactory>();
            factory.DoAutoRegistrations();
            factory.GenerateNetIds();
        };
        using var client = new RobustIntegrationTest.ClientIntegrationInstance(options);
        await client.WaitIdleAsync();
        await CheckContentAssembly(client, typeof(Shared.IoC.SharedContentIoC).Assembly);
        await CheckContentAssembly(client, typeof(Client.Entry.EntryPoint).Assembly);
    }

    // Optional path lets this same check verify the actual Release/FullRelease DLLs,
    // rather than only the Debug assemblies linked to the test project.
    private static async Task CheckContentAssembly(RobustIntegrationTest.ClientIntegrationInstance client,
        Assembly assembly)
    {
        var directory = Environment.GetEnvironmentVariable("RADIANT_SANDBOX_ASSEMBLY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            await client.CheckSandboxed(assembly);
            return;
        }

        var path = Path.Combine(directory, assembly.GetName().Name + ".dll");
        Assert.That(File.Exists(path), Is.True, $"Missing packaged assembly: {path}");
        await client.CheckSandboxed(Assembly.LoadFile(Path.GetFullPath(path)));
    }

    [Test]
    public async Task Test()
    {
        // Not using PoolManager.GetServerClient() because we want to avoid having to unnecessarily create & destroy a
        // server. This all becomes unnecessary if ever the test becomes non-destructive or the no-server option
        // actually creates a pair without a server.

        var logHandler = new PoolTestLogHandler("CLIENT");
        logHandler.ActivateContext(TestContext.Out);
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            OverrideLogHandler = () => logHandler,
            ContentAssemblies = new[]
            {
                typeof(Shared.Entry.EntryPoint).Assembly,
                typeof(Client.Entry.EntryPoint).Assembly
            },
            Options = new GameControllerOptions { LoadConfigAndUserData = false }
        };

        options.BeforeStart += () =>
        {
            IoCManager.Resolve<IModLoader>().SetModuleBaseCallbacks(new ClientModuleTestingCallbacks
            {
                ClientBeforeIoC = () =>
                {
                    IoCManager.Register<IParallaxManager, DummyParallaxManager>(true);
                    IoCManager.Resolve<ILogManager>().GetSawmill("loc").Level = LogLevel.Error;
                    IoCManager.Resolve<IConfigurationManager>()
                        .OnValueChanged(RTCVars.FailureLogLevel, value => logHandler.FailureLevel = value, true);
                }
            });
        };

        using var client = new RobustIntegrationTest.ClientIntegrationInstance(options);
        await client.WaitIdleAsync();
        await client.CheckSandboxed(typeof(Client.Entry.EntryPoint).Assembly);
        await client.CheckSandboxed(typeof(Shared.IoC.SharedContentIoC).Assembly);
    }
}
