namespace GARbro.Domain

open System
open System.Collections.Generic

[<CLIMutable>]
type CliInvocation =
    { Command: string
      Inputs: string[]
      Values: Dictionary<string, string>
      Flags: string[]
      Help: bool }

/// Parsing has no decoder or filesystem dependencies. Options never consume file
/// arguments accidentally, and `--` protects literal filenames beginning with '-'.
[<AbstractClass; Sealed>]
type CliArguments private () =
    static member Parse(args: string[]) : CliInvocation =
        let commands = set [ "formats"; "schemes"; "probe"; "list"; "extract"; "convert" ]
        let flags = set [ "--json"; "--recursive"; "--nested"; "--discard-child-archives"; "--discard-original-media"; "--help"; "-h"; "--version" ]
        let options = set [ "--output"; "--format"; "--type"; "--to"; "--entry"; "--overwrite"; "--options"; "--scheme"; "--schemes"; "--max-depth"; "--max-entry"; "--max-total"; "--max-ratio"; "--timeout"; "--max-memory"; "--max-pixels"; "--encoding"; "--image-format"; "--audio-format"; "--video-format" ]
        let values = Dictionary<string,string>(StringComparer.Ordinal)
        let enabled = HashSet<string>(StringComparer.Ordinal)
        let inputs = ResizeArray<string>()
        let mutable command = ""
        let mutable literal = false
        let mutable help = false
        let mutable index = 0
        while index < args.Length do
            let value = args[index]
            if not literal && value = "--" then literal <- true
            elif not literal && value.StartsWith("-", StringComparison.Ordinal) then
                let pair = value.Split('=', 2)
                let key = if pair[0] = "-o" then "--output" else pair[0]
                if flags.Contains key && pair.Length = 1 then
                    enabled.Add key |> ignore
                    if key = "--help" || key = "-h" then help <- true
                elif options.Contains key then
                    let argument =
                        if pair.Length = 2 then pair[1]
                        else
                            if index + 1 >= args.Length || args[index+1] = "--" ||
                               args[index+1].StartsWith("--", StringComparison.Ordinal) || args[index+1] = "-o" || args[index+1] = "-h" then
                                raise (ArgumentException("Invalid or incomplete option: " + key))
                            index <- index + 1
                            args[index]
                    if String.IsNullOrWhiteSpace argument then raise (ArgumentException("Invalid or incomplete option: " + key))
                    if not (values.TryAdd(key, argument)) then raise (ArgumentException("Option specified more than once: " + key))
                else raise (ArgumentException("Invalid or incomplete option: " + key))
            elif command = "" then
                if value = "help" then command <- "help"; help <- true
                elif commands.Contains value then command <- value
                else raise (ArgumentException("Unknown command: " + value))
            else inputs.Add value
            index <- index + 1
        if command = "help" then
            if inputs.Count > 1 then raise (ArgumentException("Unknown command: " + String.Join(" ", inputs)))
            command <- if inputs.Count = 0 then "" else inputs[0]
            inputs.Clear()
            if command <> "" && not (commands.Contains command) then raise (ArgumentException("Unknown command: " + command))
        if command = "" then
            if not help && not (enabled.Contains "--version") &&
               ((values.Keys |> Seq.exists (fun k -> k <> "--timeout" && k <> "--max-memory")) ||
                (enabled |> Seq.exists (fun k -> k <> "--json"))) then
                raise (ArgumentException("A command is required."))
            help <- true
        if not help && not (enabled.Contains "--version") then
            let globals = set [ "--json"; "--timeout"; "--max-memory" ]
            let decoder = set [ "--format"; "--options"; "--scheme"; "--schemes"; "--encoding" ]
            let disk = set [ "--recursive"; "--max-depth" ]
            let allowed =
                match command with
                | "formats" -> set [ "--type" ]
                | "schemes" -> Set.empty
                | "probe" | "list" -> Set.union decoder disk
                | "convert" -> Set.union decoder (set [ "--output"; "--to"; "--entry"; "--overwrite"; "--max-entry"; "--max-pixels" ])
                | "extract" -> Set.unionMany [ decoder; disk; set [ "--output"; "--overwrite"; "--nested"; "--max-entry"; "--max-total"; "--max-ratio"; "--image-format"; "--audio-format"; "--video-format"; "--discard-child-archives"; "--discard-original-media" ] ]
                | _ -> Set.empty
            for key in Seq.append values.Keys enabled do
                if not (globals.Contains key || allowed.Contains key) then
                    raise (ArgumentException("Option is not supported by this command: " + key))
            if enabled.Contains "--discard-child-archives" && not (enabled.Contains "--nested") then
                raise (ArgumentException("Discarding child archives requires --nested."))
            if enabled.Contains "--discard-original-media" && not (values.ContainsKey "--image-format" || values.ContainsKey "--audio-format" || values.ContainsKey "--video-format") then
                raise (ArgumentException("Discarding originals requires a conversion format."))
            if command = "formats" && inputs.Count <> 0 then raise (ArgumentException("formats does not accept input files."))
        { Command = command; Inputs = inputs.ToArray(); Values = values; Flags = enabled |> Seq.toArray; Help = help }
