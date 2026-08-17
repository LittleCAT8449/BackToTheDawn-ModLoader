using Mono.Cecil;

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: AssemblyInspector <assembly-path> <type-name> [type-name ...]");
    return 1;
}

var assemblyPath = Path.GetFullPath(args[0]);
if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Assembly not found: {assemblyPath}");
    return 2;
}

using var assembly = AssemblyDefinition.ReadAssembly(
    assemblyPath,
    new ReaderParameters { ReadingMode = ReadingMode.Deferred });

var allTypes = assembly.MainModule.Types.SelectMany(Flatten).ToArray();

if (args[1].Equals("--find", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: AssemblyInspector <assembly-path> --find <text> [text ...]");
        return 1;
    }

    var patterns = args.Skip(2).ToArray();
    foreach (var type in allTypes
                 .Where(type => patterns.Any(pattern =>
                     type.FullName.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                 .OrderBy(type => type.FullName))
    {
        Console.WriteLine(type.FullName);
    }

    return 0;
}

var requestedNames = args
    .Skip(1)
    .ToHashSet(StringComparer.OrdinalIgnoreCase);

var types = allTypes
    .Where(type => requestedNames.Contains(type.Name) || requestedNames.Contains(type.FullName))
    .OrderBy(type => type.FullName)
    .ToArray();

foreach (var type in types)
{
    Console.WriteLine($"TYPE {type.FullName}");
    Console.WriteLine($"  Base: {type.BaseType?.FullName ?? "<none>"}");

    Console.WriteLine("  FIELDS");
    foreach (var field in type.Fields.OrderBy(field => field.Name))
    {
        Console.WriteLine(
            $"    {FieldVisibility(field)}{Static(field.IsStatic)}{field.FieldType.FullName} {field.Name}");
    }

    Console.WriteLine("  PROPERTIES");
    foreach (var property in type.Properties.OrderBy(property => property.Name))
    {
        Console.WriteLine($"    {property.PropertyType.FullName} {property.Name}");
    }

    Console.WriteLine("  METHODS");
    foreach (var method in type.Methods.OrderBy(method => method.Name).ThenBy(method => method.Parameters.Count))
    {
        var parameters = string.Join(
            ", ",
            method.Parameters.Select(parameter => $"{parameter.ParameterType.FullName} {parameter.Name}"));
        Console.WriteLine(
            $"    {MethodVisibility(method)}{Static(method.IsStatic)}{method.ReturnType.FullName} " +
            $"{method.Name}({parameters})");
    }

    Console.WriteLine();
}

var missing = requestedNames
    .Where(name => types.All(type =>
        !type.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
        !type.FullName.Equals(name, StringComparison.OrdinalIgnoreCase)))
    .ToArray();

foreach (var name in missing)
{
    Console.Error.WriteLine($"Type not found: {name}");
}

return missing.Length == 0 ? 0 : 3;

static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
{
    yield return type;
    foreach (var nested in type.NestedTypes.SelectMany(Flatten))
    {
        yield return nested;
    }
}

static string FieldVisibility(FieldDefinition field) =>
    field.IsPublic ? "public " : field.IsFamily ? "protected " : field.IsPrivate ? "private " : "internal ";

static string MethodVisibility(MethodDefinition method) =>
    method.IsPublic ? "public " : method.IsFamily ? "protected " : method.IsPrivate ? "private " : "internal ";

static string Static(bool isStatic) => isStatic ? "static " : string.Empty;
