namespace GARbro.Domain

open System
open System.Globalization

[<Struct>]
type BrowseRow =
    { Index: int; Name: string; Kind: string; Size: int64; Directory: bool }

/// Pure ordering and filtering, evaluated on a background thread by the UI.
[<AbstractClass; Sealed>]
type BrowsePlanning private () =
    static member Visible(rows: BrowseRow[], query: string, sort: string, descending: bool, culture: string) =
        let compareText = CultureInfo.GetCultureInfo(culture).CompareInfo
        let direction = if descending then -1 else 1
        let filtered = rows |> Array.filter (fun row -> row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        if sort <> "none" then
            Array.sortInPlaceWith (fun left right ->
                let directory = compare right.Directory left.Directory
                if directory <> 0 then directory else
                let ordered =
                    match sort with
                    | "size" -> compare left.Size right.Size
                    | "type" ->
                        let kind = StringComparer.Ordinal.Compare(left.Kind, right.Kind)
                        if kind <> 0 then kind else compareText.Compare(left.Name,right.Name,CompareOptions.IgnoreCase)
                    | _ -> compareText.Compare(left.Name,right.Name,CompareOptions.IgnoreCase)
                let ordered = if ordered = 0 then compare left.Index right.Index else ordered
                direction * ordered) filtered
        filtered |> Array.map (fun row -> row.Index)

[<AbstractClass; Sealed>]
type FrameTiming private () =
    static member IsDue(timestamp: double, clock: double) = timestamp <= clock + 0.015
    static member IsLate(timestamp: double, clock: double, frameDuration: double) = timestamp + max 0.1 (2.0 * frameDuration) < clock
