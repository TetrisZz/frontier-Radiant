using Robust.Shared.IoC;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Validation;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Serialization.TypeSerializers.Interfaces;

namespace Content.Shared._radiant.Serialization;

[TypeSerializer]
public sealed class GuidSerializer : ITypeSerializer<Guid, ValueDataNode>, ITypeCopyCreator<Guid>
{
    public Guid Read(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, SerializationHookContext hookCtx,
        ISerializationContext? context = null,
        ISerializationManager.InstantiationDelegate<Guid>? instanceProvider = null)
    {
        return Guid.Parse(node.Value);
    }

    public ValidationNode Validate(ISerializationManager serializationManager, ValueDataNode node,
        IDependencyCollection dependencies, ISerializationContext? context = null)
    {
        return Guid.TryParse(node.Value, out _)
            ? new ValidatedValueNode(node)
            : new ErrorNode(node, "Invalid GUID");
    }

    public DataNode Write(ISerializationManager serializationManager, Guid value,
        IDependencyCollection dependencies, bool alwaysWrite = false, ISerializationContext? context = null)
    {
        return new ValueDataNode(value.ToString("D"));
    }

    public Guid CreateCopy(ISerializationManager serializationManager, Guid source,
        IDependencyCollection dependencies, SerializationHookContext hookCtx,
        ISerializationContext? context = null)
    {
        return source;
    }
}
