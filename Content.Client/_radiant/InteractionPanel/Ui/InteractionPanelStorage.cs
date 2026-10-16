using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client.Interaction.Panel.Ui;

public static class InteractionPanelStorage
{
    public static void Write(IWritableDirProvider directory, ResPath path, string text)
    {
        var temporary = new ResPath(path + ".tmp");
        var backup = new ResPath(path + ".bak");
        using (var writer = directory.OpenWriteText(temporary))
            writer.Write(text);
        var hadOriginal = directory.Exists(path);
        if (hadOriginal)
        {
            directory.Delete(backup);
            directory.Rename(path, backup);
        }
        try
        {
            directory.Rename(temporary, path);
        }
        catch
        {
            if (hadOriginal && !directory.Exists(path))
                directory.Rename(backup, path);
            throw;
        }
    }
}
