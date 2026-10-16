using System.Linq;
using System.Security.Cryptography;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using Robust.Shared.Player;
using Content.Server.GameTicking;

namespace Content.Server._Triad.Shipyard;

/// <summary>Persists the signing key and accepts only this server's signed ship files.</summary>
public sealed class ShipSaveSigningSystem : EntitySystem
{
    [Dependency] private readonly IResourceManager _resources = default!;

    private static readonly ResPath KeyPath = new("/Shipyard/signing-key.bin");
    private bool _ready;
    private ShipSaveAvailabilityStore _availability = default!;

    public override void Initialize()
    {
        base.Initialize();
        _availability = new ShipSaveAvailabilityStore(_resources.UserData, Guid.NewGuid());
        SubscribeLocalEvent<DeployedShipSaveComponent, EntityTerminatingEvent>(OnDeployedShipDeleted);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        if (args.New != GameRunLevel.PreRoundLobby)
            return;

        // This runs before players/maps are cleaned up, including a restart while still in the lobby.
        var deployments = EntityQueryEnumerator<DeployedShipSaveComponent>();
        var released = new List<EntityUid>();
        while (deployments.MoveNext(out var uid, out var deployment))
        {
            // A sale or replacement save already committed must not be undone by a round restart.
            if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                continue;
            _availability.MakeAvailable(deployment.Hash);
            released.Add(uid);
        }

        foreach (var uid in released)
            RemComp<DeployedShipSaveComponent>(uid);
    }

    private void OnDeployedShipDeleted(Entity<DeployedShipSaveComponent> ent, ref EntityTerminatingEvent args)
    {
        // Shutdown flushes the world too; only gameplay deletion revokes crash recovery.
        if (((EntityManager) EntityManager).ShuttingDown)
            return;
        _availability.Revoke(ent.Comp.Hash);
    }

    private void EnsureKey()
    {
        if (_ready)
            return;

        if (_resources.UserData.Exists(KeyPath))
        {
            using var stream = _resources.UserData.OpenRead(KeyPath);
            using var buffer = new System.IO.MemoryStream();
            stream.CopyTo(buffer);
            AuthenticatedShipFile.SetStaticKeyInfo(buffer.ToArray());
        }
        else
        {
            using var rsa = RSA.Create(3072);
            var key = rsa.ExportRSAPrivateKey();
            _resources.UserData.CreateDir(KeyPath.Directory);
            using (var stream = _resources.UserData.OpenWrite(KeyPath))
                stream.Write(key);
            AuthenticatedShipFile.SetStaticKeyInfo(key);
        }

        _ready = true;
    }

    public AuthenticatedShipFile Sign(string yaml, int appraisal)
    {
        EnsureKey();
        var file = AuthenticatedShipFile.FromShipData(yaml);
        file.Appraisal = appraisal;
        file.SignShip();
        return file;
    }

    public bool IsTrusted(AuthenticatedShipFile file)
    {
        EnsureKey();
        return file.GetInstancePublicKeyInfo() is { } key
            && key.SequenceEqual(AuthenticatedShipFile.GetStaticPublicKeyInfo())
            && file.IsShipSigned();
    }

    private static string Hash(AuthenticatedShipFile file) => Convert.ToHexString(file.GetHash());

    public bool IsAvailable(AuthenticatedShipFile file)
    {
        return _availability.IsAvailable(Hash(file));
    }

    public bool IsKnown(AuthenticatedShipFile file) => _availability.IsKnown(Hash(file));

    public void MakeAvailable(AuthenticatedShipFile file)
    {
        _availability.MakeAvailable(Hash(file));
    }

    public void Deploy(AuthenticatedShipFile file, EntityUid grid, ICommonSession session, string? sourceFilePath)
    {
        var deployed = EnsureComp<DeployedShipSaveComponent>(grid);
        deployed.Hash = Hash(file);
        deployed.Loader = session.UserId;
        deployed.SourceFilePath = sourceFilePath ?? string.Empty;
        try
        {
            _availability.Deploy(deployed.Hash);
        }
        catch
        {
            RemComp<DeployedShipSaveComponent>(grid);
            throw;
        }
    }

    public void Consume(AuthenticatedShipFile file) => _availability.Revoke(Hash(file));
}
