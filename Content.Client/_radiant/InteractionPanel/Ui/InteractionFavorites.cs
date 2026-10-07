using System.Linq;

namespace Content.Client.Interaction.Panel.Ui;

/// <summary>Ordered client-local prototype IDs; unknown IDs survive a change of server.</summary>
public sealed class InteractionFavorites
{
    private readonly List<string> _ids = new();
    public IReadOnlyList<string> Ids => _ids;
    public int IndexOf(string id) => _ids.IndexOf(id);

    public void Load(string text)
    {
        _ids.Clear();
        foreach (var line in text.Split('\n'))
        {
            var id = line.Trim();
            if (id.Length > 0 && id.Length <= 256 && !id.Any(char.IsControl))
                Add(id);
        }
    }

    public string Serialize() => string.Join("\n", _ids);
    public void Add(string id)
    {
        if (!_ids.Contains(id))
            _ids.Add(id);
    }
    public void Remove(string id) => _ids.Remove(id);
    public bool CanMove(string id, int offset)
    {
        var index = IndexOf(id);
        return index >= 0 && index + offset >= 0 && index + offset < _ids.Count;
    }
    public void Move(string id, int offset)
    {
        if (!CanMove(id, offset))
            return;
        var index = IndexOf(id);
        _ids.RemoveAt(index);
        _ids.Insert(index + offset, id);
    }
}
