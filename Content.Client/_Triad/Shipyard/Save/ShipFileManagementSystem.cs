using System;
using System.Linq;
using System.Text;
using Content.Shared._Triad.Shipyard.Save;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._Triad.Shipyard.Save;

/// <summary>Keeps deployed snapshots for crash recovery and archives them after a successful replacement save.</summary>
public sealed class ShipFileManagementSystem : EntitySystem
{
    [Dependency] private readonly IResourceManager _resources = default!;
    private readonly HashSet<string> _pendingLoads = new();
    public event Action? ShipsUpdated;

    public override void Initialize()
    {
        SubscribeNetworkEvent<SendShipSaveDataClientMessage>(OnSave);
        SubscribeNetworkEvent<DeleteLocalShipFileMessage>(OnLoaded);
        SubscribeNetworkEvent<MigrateShipFileMessage>(OnMigrated);
    }

    private void OnSave(SendShipSaveDataClientMessage message)
    {
        try
        {
            _resources.UserData.CreateDir(new ResPath("/Exports"));
            var name = new StringBuilder();
            foreach (var character in message.ShipName.Take(80))
                name.Append(char.IsLetterOrDigit(character) || character == '-' ? character : '_');
            var path = new ResPath($"/Exports/{name}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.yml");
            using (var writer = _resources.UserData.OpenWriteText(path))
                writer.Write(message.ShipData);
            RaiseNetworkEvent(new ShipSaveWrittenMessage(message.RequestId, true));
            ShipsUpdated?.Invoke();
        }
        catch (Exception exception)
        {
            Log.Error($"Unable to write saved ship: {exception}");
            RaiseNetworkEvent(new ShipSaveWrittenMessage(message.RequestId, false));
        }
    }

    public List<string> GetSavedShipFiles()
    {
        try
        {
            var (files, _) = _resources.UserData.Find("*.yml", recursive: true);
            return files.Select(file => file.ToRootedPath().ToString())
                .Where(IsShipPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (NotImplementedException)
        {
            return new();
        }
    }

    private static bool IsShipPath(string path)
    {
        return path.StartsWith("/Exports/", StringComparison.Ordinal)
            && !path[9..].Contains('/') && !path.Contains('\\')
            && !path.Contains("..", StringComparison.Ordinal)
            && path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);
    }

    public string? ReadForLoad(string path)
    {
        if (!IsShipPath(path))
            return null;
        try
        {
            using var reader = _resources.UserData.OpenText(new ResPath(path));
            var data = reader.ReadToEnd();
            _pendingLoads.Add(path);
            return data;
        }
        catch (Exception exception)
        {
            Log.Error($"Unable to read saved ship: {exception}");
            return null;
        }
    }

    private void OnLoaded(DeleteLocalShipFileMessage message)
    {
        if (!_pendingLoads.Remove(message.FilePath) || !IsShipPath(message.FilePath))
            return;
        try
        {
            var source = new ResPath(message.FilePath);
            var backup = new ResPath($"/Exports/backup/{source.FilenameWithoutExtension}_{Guid.NewGuid():N}.yml");
            _resources.UserData.CreateDir(backup.Directory);
            using (var input = _resources.UserData.OpenRead(source))
            using (var output = _resources.UserData.OpenWrite(backup))
                input.CopyTo(output);
            _resources.UserData.Delete(source);
            ShipsUpdated?.Invoke();
        }
        catch (Exception exception)
        {
            Log.Error($"Unable to back up deployed ship: {exception}");
        }
    }

    private void OnMigrated(MigrateShipFileMessage message)
    {
        if (!_pendingLoads.Contains(message.TargetPath) || !IsShipPath(message.TargetPath))
            return;
        try
        {
            var source = new ResPath(message.TargetPath);
            var backup = new ResPath($"/Exports/backup/{source.FilenameWithoutExtension}_{Guid.NewGuid():N}.yml");
            _resources.UserData.CreateDir(backup.Directory);
            using (var input = _resources.UserData.OpenRead(source))
            using (var output = _resources.UserData.OpenWrite(backup))
                input.CopyTo(output);
            using (var writer = _resources.UserData.OpenWriteText(source))
                writer.Write(message.Contents);
            ShipsUpdated?.Invoke();
        }
        catch (Exception exception)
        {
            Log.Error($"Unable to migrate saved ship: {exception}");
        }
    }

    public override void Shutdown()
    {
        _pendingLoads.Clear();
        base.Shutdown();
    }
}
