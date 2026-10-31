using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Media;

[RegisterComponent]
public sealed partial class NewsroomComponent : Component
{
    [DataField] public bool NeedsPower = true;
    [DataField] public string Channel = "";
    public Guid? Selected;
    public byte[]? AttachedPhoto;
    public TimeSpan NextPublish;
    public TimeSpan NextPrint;
    public string Status = "";
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class NewsEditionArticle
{
    [DataField] public Guid Id;
    [DataField] public string Title = "";
    [DataField] public string Channel = "";
    [DataField] public string Author = "";
    [DataField] public string Content = "";
    [DataField] public string Time = "";
    [DataField] public byte[]? Photo;
}

[RegisterComponent]
public sealed partial class NewsEditionComponent : Component
{
    [DataField] public List<NewsEditionArticle> Articles = new();
    [DataField] public string Edition = "";
}

[Serializable, NetSerializable]
public enum NewsroomUiKey : byte { Key }
[Serializable, NetSerializable]
public enum NewsEditionUiKey : byte { Key }
[Serializable, NetSerializable]
public enum NewsroomAction : byte { Select, Publish, Print, ClearPhoto, Channel }

[Serializable, NetSerializable]
public sealed class NewsroomActionMessage(NewsroomAction action, Guid article = default, string channel = "", string title = "", string content = "") : BoundUserInterfaceMessage
{
    public NewsroomAction Action = action;
    public Guid Article = article;
    public string Channel = channel;
    public string Title = title;
    public string Content = content;
}

[Serializable, NetSerializable]
public sealed class NewsroomUiState(List<NewsEditionArticle> articles, NewsEditionArticle? selected, string channel,
    bool photo, int paper, bool ready, bool canPublish, bool canPrint, string status) : BoundUserInterfaceState
{
    public List<NewsEditionArticle> Articles = articles;
    public NewsEditionArticle? Selected = selected;
    public string Channel = channel;
    public bool HasPhoto = photo;
    public int Paper = paper;
    public bool Ready = ready;
    public bool CanPublish = canPublish;
    public bool CanPrint = canPrint;
    public string Status = status;
}

[Serializable, NetSerializable]
public sealed class NewsEditionUiState(string title, List<NewsEditionArticle> articles) : BoundUserInterfaceState
{
    public string Title = title;
    public List<NewsEditionArticle> Articles = articles;
}
