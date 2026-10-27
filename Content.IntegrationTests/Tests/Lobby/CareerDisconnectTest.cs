using System.Collections.Generic;
using System.Linq;
using Content.Client._radiant.Careers;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Lobby;

public sealed class CareerDisconnectTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task LeavingLobbyAndReconnectingDisposesCareerControls(bool openCareerWindow)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { InLobby = true, Fresh = true, Destructive = true });
        try
        {
            CareerSystem? previousSystem = null;
            for (var connection = 0; connection < 2; connection++)
            {
                if (connection != 0)
                    await pair.Connect();

                await pair.RunTicksSync(10);
                HumanoidProfileEditor? editor = null;
                CareerWindow? window = null;
                await pair.Client.WaitAssertion(() =>
                {
                    var states = pair.Client.ResolveDependency<IStateManager>();
                    Assert.That(states.CurrentState, Is.TypeOf<LobbyState>());
                    var lobby = (LobbyState) states.CurrentState;
                    lobby.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
                    editor = Descendants(lobby.Lobby!).OfType<HumanoidProfileEditor>().Single();

                    var careers = pair.Client.EntMan.System<CareerSystem>();
                    Assert.That(careers, Is.Not.SameAs(previousSystem));
                    previousSystem = careers;
                    if (openCareerWindow)
                    {
                        careers.OpenWindow();
                        var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
                        window = Descendants(ui.RootControl).OfType<CareerWindow>().Single();
                    }
                });

                await pair.Disconnect("Career lobby disconnect regression test");
                await pair.RunTicksSync(5);
                await pair.Client.WaitAssertion(() =>
                {
                    Assert.That(pair.Client.ResolveDependency<IStateManager>().CurrentState, Is.Not.TypeOf<LobbyState>());
                    Assert.That(pair.Client.EntMan.TrySystem<CareerSystem>(out _), Is.False);
                    Assert.That(editor!.Disposed, Is.True);
                    if (openCareerWindow)
                        Assert.That(window!.Disposed, Is.True);
                });
            }
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        yield return control;
        foreach (var child in control.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}
