using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

if (args.Length != 3) throw new ArgumentException("Expected original DLL, patched DLL and runtime reference directory.");
var original = AssemblyName.GetAssemblyName(args[0]);
var patched = AssemblyName.GetAssemblyName(args[1]);
if (original.FullName != patched.FullName) throw new InvalidDataException("Backend assembly identity changed.");
var before = Describe(args[0]);
var after = Describe(args[1]);
if (!before.SequenceEqual(after))
    throw new InvalidDataException("Backend public API changed:\n" + string.Join('\n', before.Except(after).Select(x => "- " + x)
        .Concat(after.Except(before).Select(x => "+ " + x))));
Console.WriteLine("Preserved " + original.FullName);
Console.WriteLine($"Verified {before.Length} public/protected API records; SHA-256 {Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', before))))}");

string[] Describe(string path)
{
    var context = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
    context.Resolving += (_, name) =>
    {
        string candidate = Path.Combine(args[2], name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(Path.GetFullPath(candidate)) : null;
    };
    try
    {
        var records = new List<string>();
        foreach (var type in context.LoadFromAssemblyPath(Path.GetFullPath(path)).GetExportedTypes())
        {
            records.Add(type.FullName + ":" + type.Attributes + ":" + type.BaseType + ":" + string.Join(',', type.GetInterfaces().Select(x => x.ToString()).Order()));
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                bool visible = member switch
                {
                    MethodBase m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly,
                    FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
                    PropertyInfo p => p.GetAccessors(true).Any(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly),
                    EventInfo e => e.AddMethod is { } m && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly),
                    Type t => t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem,
                    _ => false
                };
                if (visible) records.Add(type.FullName + "/" + member.MemberType + ":" + member);
            }
        }
        return records.Order(StringComparer.Ordinal).ToArray();
    }
    finally { context.Unload(); }
}
