using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace GameRes
{
    public sealed record SchemeImportReport(int Version, string[] Applied, string[] Unported, Dictionary<string, string> Failed);
    public sealed partial class FormatCatalog
    {
        /// <summary>Load exported JSON. Only serializable types from registered providers are permitted.</summary>
        public SchemeImportReport DeserializePortableSchemeJson(Stream input)
        {
            using var reader = new StreamReader(input, System.Text.Encoding.UTF8, true, 4096, true);
            using var json = new JsonTextReader(reader)
            {
                MaxDepth = 128
            };
            var root = JObject.Load(json);
            int schema = (int?)root["PortableSchemaVersion"] ?? 1;
            if (schema is < 1 or > 2)
                throw new InvalidDataException("Unsupported portable scheme schema version.");
            int version = (int? )root["Version"] ?? 0;
            var map = root["SchemeMap"] as JObject ?? throw new InvalidDataException("SchemeMap is missing.");
            var serializer = JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = new PortableFieldsContractResolver(schema == 2), TypeNameHandling = TypeNameHandling.Auto, SerializationBinder = new ProviderSchemeBinder(Formats.Select(f => f.GetType().Assembly).Append(typeof(ResourceScheme).Assembly).Distinct()), MaxDepth = 128, MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead, });
            var applied = new List<string>();
            var unported = new List<string>();
            var failed = new Dictionary<string, string>();
            foreach (var pair in map)
            {
                var format = Formats.FirstOrDefault(f => f.Tag == pair.Key && f.Scheme != null);
                if (format == null)
                {
                    unported.Add(pair.Key);
                    continue;
                }

                try
                {
                    var scheme = pair.Value.ToObject<ResourceScheme>(serializer);
                    if (scheme == null || !format.Scheme.GetType().IsInstanceOfType(scheme))
                        throw new InvalidDataException("Scheme type does not match its format.");
                    format.Scheme = scheme;
                    applied.Add(pair.Key);
                }
                catch (Exception e)
                {
                    var cause = e.GetBaseException();
                    failed[pair.Key] = e.GetType().Name + ": " + e.Message
                        + (cause == e ? "" : " (" + cause.GetType().Name + ": " + cause.Message + ")");
                }
            }

            if (root["GameMap"] is JObject games)
                m_game_map = games.ToObject<Dictionary<string, string>>();
            if (failed.Count == 0)
                CurrentSchemeVersion = version;
            return new(version, applied.ToArray(), unported.ToArray(), failed);
        }

        // Scheme JSON represents serialized fields, not constructor arguments.
        // Legacy ciphers often have only a parameterized constructor (e.g. CxScheme).
        sealed class PortableFieldsContractResolver : IncludeFieldsContractResolver
        {
            readonly bool fieldOnly;
            public PortableFieldsContractResolver(bool fieldOnly) => this.fieldOnly = fieldOnly;

            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
                => fieldOnly ? GetPortableSchemeFields(objectType) : base.GetSerializableMembers(objectType);

            protected override IList<JsonProperty> CreateProperties(Type objectType, MemberSerialization memberSerialization)
                => base.CreateProperties(objectType, fieldOnly ? MemberSerialization.Fields : memberSerialization);

            protected override JsonObjectContract CreateObjectContract(Type objectType)
            {
                var contract = base.CreateObjectContract(objectType);
                if (objectType.IsDefined(typeof(SerializableAttribute), false) && !objectType.IsAbstract)
                {
                    contract.DefaultCreator = () => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(objectType);
                    contract.DefaultCreatorNonPublic = false;
                    contract.OverrideCreator = null;
                }

                return contract;
            }
        }

        sealed class ProviderSchemeBinder : ISerializationBinder
        {
            readonly Dictionary<string, Type> types = new(StringComparer.Ordinal);
            readonly HashSet<string> providerAssemblies = new(StringComparer.Ordinal)
            {
                "GameRes",
                "ArcFormats",
                "ArcLegacy",
                "ArcExperimental",
                "ArcExtra"
            };
            static readonly Type[] builtins =
            {
                typeof(string),
                typeof(bool),
                typeof(byte),
                typeof(sbyte),
                typeof(short),
                typeof(ushort),
                typeof(int),
                typeof(uint),
                typeof(long),
                typeof(ulong),
                typeof(float),
                typeof(double),
                typeof(decimal),
                typeof(char),
                typeof(object),
                typeof(Dictionary<, >),
                typeof(List<>),
                typeof(HashSet<>),
                typeof(KeyValuePair<, >),
                typeof(Tuple<, >),
                typeof(Tuple<,, >),
                typeof(SortedDictionary<, >)
            };
            public ProviderSchemeBinder(IEnumerable<Assembly> assemblies)
            {
                foreach (var assembly in assemblies)
                {
                    providerAssemblies.Add(assembly.GetName().Name);
                    foreach (var type in assembly.GetTypes())
                        if (type.IsEnum || type.IsDefined(typeof(SerializableAttribute), false))
                            types[type.FullName] = type;
                }

                foreach (var type in builtins)
                    types[type.FullName] = type;
            }

            public Type BindToType(string assemblyName, string typeName)
            {
                Type Resolve(Assembly assembly, string name, bool ignoreCase) => types.TryGetValue(name, out var type) ? type : throw new JsonSerializationException("Unregistered scheme type: " + name);
                Assembly Load(AssemblyName name)
                {
                    if (name.Name is "mscorlib" or "System.Private.CoreLib" or "System.Collections" or "System.Core" or "System")
                        return typeof(object).Assembly;
                    if (providerAssemblies.Contains(name.Name))
                        return typeof(ResourceScheme).Assembly;
                    throw new JsonSerializationException("Unregistered scheme assembly: " + name.Name);
                }

                var result = Type.GetType(typeName, Load, Resolve, true);
                if (result == null)
                    throw new JsonSerializationException("Unregistered scheme type: " + typeName);
                return result;
            }

            public void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                assemblyName = serializedType.Assembly.GetName().Name;
                typeName = serializedType.FullName;
            }
        }
    }
}
