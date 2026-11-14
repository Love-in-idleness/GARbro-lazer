module Garbro.Build.RuntimeCompactor

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.Diagnostics
open System.IO
open System.Reflection.Metadata
open System.Reflection.Metadata.Ecma335
open System.Reflection.PortableExecutable
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Text.Json.Serialization

let digest (bytes: byte[]) = Convert.ToHexString(SHA256.HashData bytes)
let hashRows rows = rows |> Seq.sortWith (fun a b -> String.CompareOrdinal(a,b)) |> String.concat "\n" |> Encoding.UTF8.GetBytes |> digest
let qualify ns name = if String.IsNullOrEmpty ns then name else ns + "." + name
let rec definitionName (reader: MetadataReader) handle =
    let value = reader.GetTypeDefinition handle
    let parent = value.GetDeclaringType()
    if parent.IsNil then qualify (reader.GetString value.Namespace) (reader.GetString value.Name)
    else definitionName reader parent + "+" + reader.GetString value.Name
let rec referenceName (reader: MetadataReader) handle =
    let value = reader.GetTypeReference handle
    if value.ResolutionScope.Kind = HandleKind.TypeReference then
        referenceName reader (MetadataTokens.TypeReferenceHandle(MetadataTokens.GetRowNumber value.ResolutionScope)) + "+" + reader.GetString value.Name
    else qualify (reader.GetString value.Namespace) (reader.GetString value.Name)
let methodSignature (signature: MethodSignature<string>) =
    let parameters = String.Join(",", signature.ParameterTypes)
    $"{signature.Header.RawValue}:{signature.GenericParameterCount}:{signature.RequiredParameterCount}:{signature.ReturnType}({parameters})"

type SignatureNames() as self =
    interface ISignatureTypeProvider<string, unit> with
        member _.GetPrimitiveType code = "System." + string code
        member _.GetTypeFromDefinition(reader, handle, _) = definitionName reader handle
        member _.GetTypeFromReference(reader, handle, _) = referenceName reader handle
        member _.GetTypeFromSpecification(reader, context, handle, _) = reader.GetTypeSpecification(handle).DecodeSignature(self, context)
        member _.GetSZArrayType element = element + "[]"
        member _.GetArrayType(element, shape) = element + "[" + string shape.Rank + ";" + String.Join(",",shape.Sizes) + ";" + String.Join(",",shape.LowerBounds) + "]"
        member _.GetByReferenceType element = element + "&"
        member _.GetPointerType element = element + "*"
        member _.GetPinnedType element = element + " pinned"
        member _.GetGenericTypeParameter(_, index) = "!" + string index
        member _.GetGenericMethodParameter(_, index) = "!!" + string index
        member _.GetGenericInstantiation(name, arguments) = name + "<" + String.Join(",", arguments) + ">"
        member _.GetModifiedType(modifier, element, required) = $"{element} mod({required},{modifier})"
        member _.GetFunctionPointerType signature = "fn " + methodSignature signature

let serializedTypeName (name: string) =
    let rec normalize (value: TypeName) =
        if value.IsConstructedGenericType then
            value.GetGenericTypeDefinition().FullName + "<" + (value.GetGenericArguments() |> Seq.map normalize |> String.concat ",") + ">"
        elif value.IsSZArray then normalize (value.GetElementType()) + "[]"
        elif value.IsArray then normalize (value.GetElementType()) + "[" + String.replicate (value.GetArrayRank() - 1) "," + "*]"
        elif value.IsPointer then normalize (value.GetElementType()) + "*"
        elif value.IsByRef then normalize (value.GetElementType()) + "&"
        else value.FullName
    normalize (TypeName.Parse(name.AsSpan(), null))

let enumTypes paths =
    let result = Dictionary<string, PrimitiveTypeCode>(StringComparer.Ordinal)
    for path in paths do
        use stream = File.OpenRead path
        use pe = new PEReader(stream)
        if pe.HasMetadata then
            let reader = pe.GetMetadataReader()
            for handle in reader.TypeDefinitions do
                let value = reader.GetTypeDefinition handle
                for field in value.GetFields() do
                    let field = reader.GetFieldDefinition field
                    if reader.GetString field.Name = "value__" then
                        let name = field.DecodeSignature(SignatureNames(), ())
                        result[definitionName reader handle] <- Enum.Parse<PrimitiveTypeCode>(name.Substring("System.".Length))
    result

type AttributeNames(enums: IReadOnlyDictionary<string, PrimitiveTypeCode>) =
    interface ICustomAttributeTypeProvider<string> with
        member _.GetPrimitiveType code = "System." + string code
        member _.GetTypeFromDefinition(reader, handle, _) = definitionName reader handle
        member _.GetTypeFromReference(reader, handle, _) = referenceName reader handle
        member _.GetSZArrayType element = element + "[]"
        member _.GetSystemType() = "System.Type"
        member _.IsSystemType name = name = "System.Type"
        member _.GetTypeFromSerializedName name = serializedTypeName name
        member _.GetUnderlyingEnumType name = enums[name]

let rec attributeValue (value: obj) =
    match value with
    | :? ImmutableArray<CustomAttributeTypedArgument<string>> as values ->
        "[" + (values |> Seq.map attributeArgument |> String.concat ",") + "]"
    | _ -> JsonSerializer.Serialize value
and attributeArgument (value: CustomAttributeTypedArgument<string>) = value.Type + ":" + attributeValue value.Value

type Inventory = {
    File: string
    Identity: string
    Bytes: int64
    ReadyToRun: bool
    Contract: string
    Resources: string
    [<JsonIgnore>] ContractRows: string[]
}

let inspect enums path =
    use stream = File.OpenRead path
    use pe = new PEReader(stream)
    if not pe.HasMetadata then None else
    let reader = pe.GetMetadataReader()
    let names = SignatureNames()
    let typeName (handle: EntityHandle) =
        if handle.IsNil then "" else
            match handle.Kind with
            | HandleKind.TypeDefinition -> definitionName reader (MetadataTokens.TypeDefinitionHandle(MetadataTokens.GetRowNumber handle))
            | HandleKind.TypeReference -> referenceName reader (MetadataTokens.TypeReferenceHandle(MetadataTokens.GetRowNumber handle))
            | HandleKind.TypeSpecification -> reader.GetTypeSpecification(MetadataTokens.TypeSpecificationHandle(MetadataTokens.GetRowNumber handle)).DecodeSignature(names, ())
            | _ -> string handle.Kind
    let attributes handles =
        handles |> Seq.map (fun handle ->
            let value = reader.GetCustomAttribute handle
            let owner =
                if value.Constructor.Kind = HandleKind.MethodDefinition then
                    reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(MetadataTokens.GetRowNumber value.Constructor)).GetDeclaringType() |> definitionName reader
                else reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(MetadataTokens.GetRowNumber value.Constructor)).Parent |> typeName
            // Cecil resolves forwarded enum names to CoreLib; compare their types and values, not serialized assembly scopes.
            let decoded = value.DecodeValue(AttributeNames(enums))
            let fixedArgs = decoded.FixedArguments |> Seq.map attributeArgument |> String.concat ","
            let namedArgs = decoded.NamedArguments |> Seq.map (fun arg -> $"{arg.Kind}:{arg.Name}:{arg.Type}:{attributeValue arg.Value}") |> Seq.sort |> String.concat ","
            owner + ":" + fixedArgs + ":" + namedArgs) |> Seq.sort |> String.concat ";"
    let constant (handle: ConstantHandle) =
        if handle.IsNil then "" else
        let value = reader.GetConstant handle
        string value.TypeCode + ":" + Convert.ToHexString(reader.GetBlobBytes value.Value)
    let rows = seq {
        let assembly = reader.GetAssemblyDefinition()
        yield "A:" + attributes (assembly.GetCustomAttributes())
        for handle in reader.TypeDefinitions do
            let value = reader.GetTypeDefinition handle
            let owner = definitionName reader handle
            let layout = value.GetLayout()
            yield $"T:{owner}:{value.Attributes}:{layout.PackingSize}:{layout.Size}:{typeName value.BaseType}:{attributes (value.GetCustomAttributes())}"
            for iface in value.GetInterfaceImplementations() do
                yield "I:" + owner + ":" + typeName (reader.GetInterfaceImplementation(iface).Interface)
            for field in value.GetFields() do
                let field = reader.GetFieldDefinition field
                yield $"F:{owner}:{reader.GetString field.Name}:{field.Attributes}:{field.GetOffset()}:{field.DecodeSignature(names,())}:{constant (field.GetDefaultValue())}:{attributes (field.GetCustomAttributes())}"
            for method in value.GetMethods() do
                let method = reader.GetMethodDefinition method
                let name = owner + ":" + reader.GetString method.Name + ":" + methodSignature (method.DecodeSignature(names,()))
                let il = if method.RelativeVirtualAddress = 0 then 0 else pe.GetMethodBody(method.RelativeVirtualAddress).GetILContent().Length
                yield $"M:{name}:{method.Attributes}:{method.ImplAttributes}:{il}:{attributes (method.GetCustomAttributes())}"
                for parameter in method.GetParameters() do
                    let parameter = reader.GetParameter parameter
                    yield $"P:{name}:{parameter.SequenceNumber}:{reader.GetString parameter.Name}:{parameter.Attributes}:{constant (parameter.GetDefaultValue())}:{attributes (parameter.GetCustomAttributes())}"
            for property in value.GetProperties() do
                let property = reader.GetPropertyDefinition property
                yield $"R:{owner}:{reader.GetString property.Name}:{property.Attributes}:{methodSignature (property.DecodeSignature(names,()))}:{constant (property.GetDefaultValue())}:{attributes (property.GetCustomAttributes())}"
            for event in value.GetEvents() do
                let event = reader.GetEventDefinition event
                yield $"E:{owner}:{reader.GetString event.Name}:{event.Attributes}:{typeName event.Type}:{attributes (event.GetCustomAttributes())}"
    }
    let resources = seq {
        for handle in reader.ManifestResources do
            let value = reader.GetManifestResource handle
            let content =
                if value.Implementation.IsNil then
                    let mutable blob = pe.GetSectionData(pe.PEHeaders.CorHeader.ResourcesDirectory.RelativeVirtualAddress).GetReader(int value.Offset, pe.PEHeaders.CorHeader.ResourcesDirectory.Size - int value.Offset)
                    let length = blob.ReadInt32()
                    blob.ReadBytes(length) |> digest
                else string value.Implementation.Kind
            yield $"{reader.GetString value.Name}:{value.Attributes}:{content}"
    }
    let assembly = reader.GetAssemblyDefinition()
    let contractRows = rows |> Seq.toArray
    Some { File = Path.GetFileName path
           Identity = $"{reader.GetString assembly.Name}/{assembly.Version}/{Convert.ToHexString(reader.GetBlobBytes assembly.PublicKey)}"
           Bytes = stream.Length
           ReadyToRun = pe.PEHeaders.CorHeader.ManagedNativeHeaderDirectory.Size > 0
           Contract = hashRows contractRows
           Resources = hashRows resources
           ContractRows = contractRows }

// Keep the precompiled startup paths; stripping all CoreLib code made cold CLI loads much slower.
let startupLibraries = Set.ofList [
    "System.Private.CoreLib.dll"; "System.Private.Xml.dll"; "System.Linq.Expressions.dll"
    "System.Text.Json.dll"; "System.Linq.dll"
]

// ILLink changes this library's static-initialization flags even with optimizations disabled.
let compatibilityLibraries = Set.ofList ["System.Private.DataContractSerialization.dll"]

let preservesInventory original candidate =
    not candidate.ReadyToRun && original.Identity = candidate.Identity && original.Contract = candidate.Contract && original.Resources = candidate.Resources

let responseArgument (value: string) =
    let result = StringBuilder("\"")
    let mutable slashes = 0
    for character in value do
        if character = '\\' then slashes <- slashes + 1
        else
            result.Append('\\', if character = '"' then 2 * slashes + 1 else slashes).Append(character) |> ignore
            slashes <- 0
    result.Append('\\', 2 * slashes).Append('"').ToString()

[<EntryPoint>]
let main arguments =
    try
        if arguments.Length <> 2 then invalidArg "arguments" "Expected application directory and report path."
        let app, report = Path.GetFullPath arguments[0], Path.GetFullPath arguments[1]
        let paths = Directory.GetFiles(app,"*.dll")
        let enums = enumTypes paths
        let assemblies = paths |> Array.choose (inspect enums)
        let retained = Set.union startupLibraries compatibilityLibraries
        let selected = assemblies |> Array.filter (fun file -> file.ReadyToRun && not (retained.Contains file.File))
        let temporary = Path.Combine(Path.GetTempPath(), "garbro-runtime-" + Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory temporary |> ignore
        try
            let linker = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"illink-path.txt")).Trim()
            if not (File.Exists linker) then raise (FileNotFoundException("The pinned SDK linker was not restored.",linker))
            let start = ProcessStartInfo("dotnet", UseShellExecute = false)
            let linkerArguments = ResizeArray<string>()
            let add values = linkerArguments.AddRange values
            add ["--action"; "copy"; "--trim-mode"; "copy"; "--ignore-substitutions"; "--keep-metadata"; "all"
                 "--keep-compilers-resources"; "true"; "--keep-dep-attributes"; "true"; "--strip-security"; "false"
                 "--strip-descriptors"; "false"; "--strip-substitutions"; "false"; "--strip-link-attributes"; "false"
                 "--deterministic"; "--singlewarn"; "-out"; temporary]
            for flag in ["beforefieldinit"; "overrideremoval"; "unreachablebodies"; "unusedinterfaces"; "unusedtypechecks"; "ipconstprop"; "sealer"; "substitutefeatureguards"] do
                add ["--disable-opt"; flag]
            for file in assemblies do add ["-reference"; Path.Combine(app,file.File)]
            for file in selected do add ["--action"; "link"; Path.GetFileNameWithoutExtension file.File; "-a"; Path.Combine(app,file.File); "all"]
            if selected.Length > 0 then
                // A shared GUI/CLI payload exceeds Windows' command-line limit.
                let response = Path.Combine(temporary,"linker.rsp")
                File.WriteAllLines(response, linkerArguments |> Seq.map responseArgument)
                start.ArgumentList.Add linker
                start.ArgumentList.Add ("@" + response)
                use child = Process.Start start
                child.WaitForExit()
                if child.ExitCode <> 0 then failwith "Runtime rewriting failed; original libraries were kept."
            let rewritten = selected |> Array.map (fun original ->
                let path = Path.Combine(temporary,original.File)
                let candidate = inspect enums path |> Option.defaultWith (fun () -> failwith ("Rewritten assembly is missing: " + original.File))
                if not (preservesInventory original candidate) then
                    let before, after = Set.ofArray original.ContractRows, Set.ofArray candidate.ContractRows
                    Directory.CreateDirectory(Path.GetDirectoryName report) |> ignore
                    let details = {| file = original.File
                                     before = original
                                     after = candidate
                                     missing = Set.difference before after |> Set.toArray
                                     added = Set.difference after before |> Set.toArray |}
                    File.WriteAllText(report, JsonSerializer.Serialize(details, JsonSerializerOptions(WriteIndented=true)))
                    failwith ("Runtime metadata/resource validation failed; original libraries were kept: " + original.File)
                candidate)
            for file in rewritten do File.Copy(Path.Combine(temporary,file.File), Path.Combine(app,file.File),true)
            let bytesSaved = Array.sumBy (fun (file: Inventory) -> file.Bytes) selected - Array.sumBy (fun file -> file.Bytes) rewritten
            let result = {| beforeBytes = Array.sumBy (fun (file: Inventory) -> file.Bytes) assemblies
                            bytesSaved = bytesSaved
                            rewritten = rewritten
                            retainedStartupLibraries = startupLibraries |> Set.toArray
                            retainedCompatibilityLibraries = compatibilityLibraries |> Set.toArray
                            metadataAndResourcesPreserved = true |}
            Directory.CreateDirectory(Path.GetDirectoryName report) |> ignore
            File.WriteAllText(report, JsonSerializer.Serialize(result, JsonSerializerOptions(WriteIndented=true)))
            printfn "Runtime compaction: %d bytes saved; metadata and resources verified." bytesSaved
            0
        finally Directory.Delete(temporary,true)
    with error -> eprintfn "%s" error.Message; 1
