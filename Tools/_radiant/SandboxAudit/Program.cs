using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var reader = pe.GetMetadataReader();
foreach (var handle in reader.MethodDefinitions)
{
    var method = reader.GetMethodDefinition(handle);
    if (method.RelativeVirtualAddress == 0) continue;
    var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
    for (var offset = 0; offset + 4 < il.Length; offset++)
    {
        if (il[offset] != 0x28) continue;
        var token = BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(offset + 1, 4));
        var table = (uint)token >> 24;
        if (table is not (0x06 or 0x2b)) continue;
        var row = token & 0xffffff;
        if (row == 0 || row > reader.GetTableRowCount(table == 0x06 ? TableIndex.MethodDef : TableIndex.MethodSpec)) continue;
        var target = MetadataTokens.EntityHandle(token);
        if (target.Kind == HandleKind.MethodSpecification)
            target = reader.GetMethodSpecification((MethodSpecificationHandle)target).Method;
        if (target.Kind != HandleKind.MethodDefinition) continue;
        var targetMethod = reader.GetMethodDefinition((MethodDefinitionHandle)target);
        var name = reader.GetString(targetMethod.Name);
        if (!name.Contains("InlineArray")) continue;
        var type = reader.GetTypeDefinition(method.GetDeclaringType());
        Console.WriteLine($"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}.{reader.GetString(method.Name)}: {name} at IL 0x{offset:x4}");
    }
}
