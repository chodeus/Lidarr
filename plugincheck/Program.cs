using Mono.Cecil;

// Usage: plugincheck <plugin.dll> <lidarr-bin-dir>. Lists every Lidarr type or member the plugin uses that the build lacks,
// and every Lidarr interface or abstract member a plugin type no longer implements.
var plugin = args[0];
var lib = args[1];
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(lib);
resolver.AddSearchDirectory(Path.GetDirectoryName(plugin));
resolver.AddSearchDirectory(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
var module = ModuleDefinition.ReadModule(plugin, new ReaderParameters { AssemblyResolver = resolver });

var self = module.Assembly.Name.Name;
bool IsLidarr(IMetadataScope scope) => scope is AssemblyNameReference a && a.Name.StartsWith("Lidarr.") && a.Name != self;
static TypeReference Outer(TypeReference t) { while (t.DeclaringType != null) t = t.DeclaringType; return t; }
static TypeReference Element(TypeReference t) => t is TypeSpecification s ? Element(s.ElementType) : t;

var problems = new SortedSet<string>();
int types = 0, members = 0, impls = 0;

foreach (var tr in module.GetTypeReferences())
{
    if (!IsLidarr(Outer(tr).Scope)) continue;
    types++;
    if (tr.Resolve() == null) problems.Add($"missing type   {tr.FullName}  ({Outer(tr).Scope.Name})");
}

foreach (var mr in module.GetMemberReferences())
{
    var declaring = Element(mr.DeclaringType);
    if (!IsLidarr(Outer(declaring).Scope)) continue;
    members++;
    var resolved = mr switch { MethodReference m => (IMemberDefinition)m.Resolve(), FieldReference f => f.Resolve(), _ => null };
    if (resolved == null) problems.Add($"missing member {mr.FullName}");
}

IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts) => ts.SelectMany(t => new[] { t }.Concat(All(t.NestedTypes)));

bool Implements(TypeDefinition type, MethodDefinition required)
{
    for (var t = type; t != null; t = t.BaseType?.Resolve())
    {
        if (t.Methods.Any(m => !m.IsAbstract && (m.Name == required.Name || m.Name.EndsWith("." + required.Name)) && m.Parameters.Count == required.Parameters.Count)) return true;
        if (t.Methods.Any(m => m.Overrides.Any(o => o.Name == required.Name && o.Parameters.Count == required.Parameters.Count))) return true;
    }
    return false;
}

foreach (var type in All(module.Types).Where(t => !t.IsInterface && !t.IsAbstract))
{
    var lidarrBases = new List<TypeDefinition>();
    for (var b = type.BaseType; b != null && Element(b).Resolve() is TypeDefinition bd; b = bd.BaseType) if (IsLidarr(Outer(Element(b)).Scope)) lidarrBases.Add(bd);
    var interfaces = new List<TypeDefinition>();
    void Collect(TypeDefinition t) { if (t == null) return; foreach (var i in t.Interfaces) { var d = Element(i.InterfaceType).Resolve(); if (d != null) { if (IsLidarr(Outer(Element(i.InterfaceType)).Scope)) interfaces.Add(d); Collect(d); } } Collect(t.BaseType == null ? null : Element(t.BaseType).Resolve()); }
    Collect(type);

    foreach (var required in lidarrBases.SelectMany(b => b.Methods).Concat(interfaces.SelectMany(i => i.Methods)).Where(m => m.IsAbstract && !m.IsStatic).Distinct())
    {
        impls++;
        if (!Implements(type, required)) problems.Add($"unimplemented  {type.FullName} lacks {required.DeclaringType.FullName}.{required.Name}({required.Parameters.Count})");
    }
}

Console.WriteLine($"checked {types} Lidarr type refs, {members} member refs, {impls} required members against {lib}");
foreach (var p in problems) Console.WriteLine(p);
Console.WriteLine(problems.Count == 0 ? "RESULT: compatible" : $"RESULT: {problems.Count} problem(s)");
return problems.Count == 0 ? 0 : 1;
