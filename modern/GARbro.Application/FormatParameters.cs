using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameRes;

namespace GARbro.Application;

/// <summary>Select trusted bundled scheme objects without activating JSON-specified types.</summary>
public static class FormatParameters
{
    static IEnumerable<MemberInfo> Members(object value) => value.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m is FieldInfo || m is PropertyInfo p && p.CanRead && p.GetIndexParameters().Length == 0);
    static object Read(MemberInfo member, object value) => member is FieldInfo f ? f.GetValue(value) : ((PropertyInfo)member).GetValue(value);
    static Type Kind(MemberInfo member) => member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
    static bool Writable(MemberInfo member) => member is FieldInfo f ? !f.IsInitOnly : ((PropertyInfo)member).CanWrite;
    static void Write(MemberInfo member, object target, object value)
    { if (member is FieldInfo f) f.SetValue(target, value); else ((PropertyInfo)member).SetValue(target, value); }
    static IEnumerable<(string Name, object Value)> Values(IResource format)
    {
        var schema = format.Scheme;
        if (schema == null) yield break;
        foreach (var member in Members(schema).Where(m => m.Name.StartsWith("Known", StringComparison.Ordinal)))
        {
            object collection = Read(member, schema);
            if (collection is IDictionary map)
            {
                foreach (DictionaryEntry entry in map)
                    if (entry.Key is string name && entry.Value != null) yield return (name, entry.Value);
            }
            else if (collection is IEnumerable list && collection is not string)
                foreach (object entry in list)
                    if (entry != null && Members(entry).FirstOrDefault(m => m.Name == "Name") is MemberInfo label && Read(label, entry) is string name)
                        yield return (name, entry);
        }
    }
    static MemberInfo Target(ResourceOptions options, object value) => Members(options).Where(Writable)
        .FirstOrDefault(m => m.Name == "Scheme" && Kind(m) == typeof(string)) ??
        Members(options).Where(Writable).OrderBy(m => m.Name is "Scheme" or "EncryptionInfo" or "Password" or "Key" ? 0 : 1)
            .FirstOrDefault(m => Kind(m).IsInstanceOfType(value));
    public static string[] Names(IResource format)
    {
        var options = format.GetDefaultOptions(); if (options == null) return Array.Empty<string>();
        return Values(format).Where(p => Target(options, p.Value) != null).Select(p => p.Name).Distinct().Order().ToArray();
    }
    public static void Apply(IResource format, ResourceOptions options, string name)
    {
        var candidate = Values(format).FirstOrDefault(p => p.Name == name);
        var target = candidate.Value == null ? null : Target(options, candidate.Value);
        if (target == null) throw new MissingOptionsException("Unknown or incompatible scheme for " + format.Tag + ": " + name);
        Write(target, options, target.Name == "Scheme" && Kind(target) == typeof(string) ? name : candidate.Value);
    }
}
