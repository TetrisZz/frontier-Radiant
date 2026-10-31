using System.Collections.Generic;
using System.Linq;
using Content.Shared._EE.Contractors.Components;
using Content.Shared._radiant.Passports;
using Robust.Shared.GameObjects;

namespace Content.Server._radiant.Storage;

/// <summary>Identity documents are issued for the current shift, never restored from personal storage.</summary>
public static class IdentityDocumentPersistence
{
    public static bool IsDocument(IEntityManager entities, EntityUid uid)
        => entities.HasComponent<PassportComponent>(uid) || entities.HasComponent<ServiceCredentialComponent>(uid);

    public static IEnumerable<EntityUid> FindDocuments(IEntityManager entities, EntityUid root)
    {
        var pending = new Stack<EntityUid>();
        var visited = new HashSet<EntityUid>();
        pending.Push(root);
        while (pending.TryPop(out var uid))
        {
            if (!visited.Add(uid)) continue;
            if (IsDocument(entities, uid)) yield return uid;
            if (!entities.TryGetComponent<TransformComponent>(uid, out var transform)) continue;
            var children = transform.ChildEnumerator;
            while (children.MoveNext(out var child)) pending.Push(child);
        }
    }

    public static void RemoveDocuments(IEntityManager entities, EntityUid root)
    {
        foreach (var document in FindDocuments(entities, root).ToArray())
            if (entities.EntityExists(document)) entities.DeleteEntity(document);
    }
}
