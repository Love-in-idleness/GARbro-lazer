using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using GARbro.Domain;

namespace GARbro.Application;

/// Shared application-owned strings. Protocol identifiers and decoder details stay intact.
public sealed class ResourceText
{
    static readonly IReadOnlyDictionary<string, Dictionary<string, string>> catalogs =
        LanguageCatalog.Codes.ToDictionary(code => code, code =>
        {
            using var stream = typeof(ResourceText).Assembly.GetManifestResourceStream("GARbro.Locales." + code + ".json")
                ?? throw new InvalidDataException("Missing language resource: " + code);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        }, StringComparer.Ordinal);
    static readonly string[] errorKeys = catalogs["en-US"].Keys.Where(k => k.StartsWith("Error.", StringComparison.Ordinal))
        .OrderByDescending(k => k.Length).ToArray();
    public string Language { get; }
    public ResourceText(string language = "auto") => Language = LanguageCatalog.Resolve(language, CultureInfo.CurrentUICulture.Name);
    public IEnumerable<string> Keys => catalogs["en-US"].Keys;
    public bool Contains(string key) => catalogs["en-US"].ContainsKey(key);
    public string this[string key] => catalogs[Language].GetValueOrDefault(key) ?? catalogs["en-US"].GetValueOrDefault(key) ?? key;
    public string Format(string key, params object[] args) => string.Format(CultureInfo.GetCultureInfo(Language), this[key], args);
    public string ErrorMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;
        foreach (string key in errorKeys)
            if (message.StartsWith(key[6..], StringComparison.Ordinal)) return this[key] + message[(key.Length - 6)..];
        return message;
    }
}
