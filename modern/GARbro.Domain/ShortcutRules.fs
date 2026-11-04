namespace GARbro.Domain

open System
open System.Collections.Generic

[<CLIMutable>]
type ShortcutConflict = { Gesture: string; FirstCommand: string; SecondCommand: string }

/// Stable storage and conflict rules without a dependency on a window toolkit.
[<AbstractClass; Sealed>]
type ShortcutRules private () =
    static member Normalize(gesture: string) =
        let invalid () = raise (ArgumentException("Invalid shortcut."))
        if String.IsNullOrWhiteSpace gesture then invalid ()
        let tokens = gesture.Split('+') |> Array.map (fun s -> s.Trim())
        let modifier = function
            | "ctrl" | "control" -> "Ctrl"
            | "alt" -> "Alt"
            | "shift" -> "Shift"
            | "meta" | "cmd" | "command" | "win" -> "Meta"
            | _ -> ""
        let key = tokens[tokens.Length - 1]
        if key.Length = 0 || modifier (key.ToLowerInvariant()) <> "" then invalid ()
        let mods = tokens |> Array.take (tokens.Length - 1) |> Array.map (fun s -> modifier (s.ToLowerInvariant()))
        if Array.contains "" mods || (Array.distinct mods).Length <> mods.Length then invalid ()
        let ordered = [| "Ctrl"; "Alt"; "Shift"; "Meta" |] |> Array.filter (fun m -> Array.contains m mods)
        String.Join("+", Array.append ordered [| key |])

    static member Conflicts(bindings: IReadOnlyDictionary<string, string array>) =
        let seen = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        [| for pair in bindings do
               for value in pair.Value do
                   let gesture = ShortcutRules.Normalize value
                   match seen.TryGetValue gesture with
                   | true, first when first <> pair.Key ->
                       yield { Gesture = gesture; FirstCommand = first; SecondCommand = pair.Key }
                   | _ -> seen[gesture] <- pair.Key |]
