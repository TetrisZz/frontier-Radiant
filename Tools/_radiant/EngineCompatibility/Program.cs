using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// Mechanical syntax-aware migration. Back up every changed file before changing it.
var root = Path.GetFullPath(args[0]);
var backup = Path.GetFullPath(args[1]);
var changed = 0;
foreach (var directory in Directory.EnumerateDirectories(root).Where(p => Path.GetFileName(p).StartsWith("Content.") || Path.GetFileName(p) == "Pow3r"))
foreach (var path in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(root, path);
    if (relative.Split(Path.DirectorySeparatorChar).Any(p => p is "obj" or "bin")) continue;
    var bytes = File.ReadAllBytes(path);
    var hasBom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
    var text = SourceText.From(File.ReadAllText(path));
    var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview,
        preprocessorSymbols: ["TOOLS", "DEBUG", "TRACE"]));
    var syntax = tree.GetRoot();
    var edits = new Dictionary<TextSpan, string>();
    var partialTypes = new HashSet<TypeDeclarationSyntax>();
    foreach (var field in syntax.DescendantNodes().OfType<FieldDeclarationSyntax>())
    {
        if (!HasAttribute(field.AttributeLists, "Dependency")) continue;
        foreach (var token in field.Modifiers.Where(t => t.IsKind(SyntaxKind.ReadOnlyKeyword)))
            edits[TextSpan.FromBounds(token.SpanStart, token.FullSpan.End)] = "";
        if (field.Modifiers.LastOrDefault() is var last && !last.IsKind(SyntaxKind.ReadOnlyKeyword))
        {
            var gap = TextSpan.FromBounds(last.Span.End, field.Declaration.Type.SpanStart);
            var whitespace = text.ToString(gap);
            if (whitespace.Length > 1 && whitespace.All(c => c is ' ' or '\t'))
                edits[gap] = " ";
        }
        foreach (var type in field.Ancestors().OfType<TypeDeclarationSyntax>()) partialTypes.Add(type);
    }
    foreach (var type in syntax.DescendantNodes().OfType<TypeDeclarationSyntax>())
    {
        if (!HasAttribute(type.AttributeLists, "DataDefinition")) continue;
        partialTypes.Add(type);
        foreach (var outer in type.Ancestors().OfType<TypeDeclarationSyntax>()) partialTypes.Add(outer);
        foreach (var property in type.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (!HasAttribute(property.AttributeLists, "DataField") && !HasAttribute(property.AttributeLists, "IncludeDataField")) continue;
            var accessors = property.AccessorList;
            if (accessors == null || accessors.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration) || a.IsKind(SyntaxKind.InitAccessorDeclaration))) continue;
            if (accessors.Accessors.Any(a => a.Body != null || a.ExpressionBody != null)) continue;
            edits[new TextSpan(accessors.CloseBraceToken.SpanStart, 0)] = "private set; ";
        }
    }
    foreach (var type in partialTypes)
        if (!type.Modifiers.Any(t => t.IsKind(SyntaxKind.PartialKeyword)))
            edits[new TextSpan(type.Keyword.SpanStart, 0)] = "partial ";
    if (edits.Count == 0) continue;
    var result = text.WithChanges(edits.Select(e => new TextChange(e.Key, e.Value))).ToString();
    var snapshot = Path.Combine(backup, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
    File.Copy(path, snapshot, overwrite: false);
    File.WriteAllText(path, result, new UTF8Encoding(hasBom));
    changed++;
}
Console.WriteLine($"Mechanically migrated {changed} files. Backup: {backup}");

static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name)
    => lists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString() == name || a.Name.ToString() == name + "Attribute");
