namespace GARbro.Domain

open System.Collections.Generic
open System.Collections.ObjectModel
open MaterialColorUtilities.Palettes
open MaterialColorUtilities.Schemes

/// Pure seed-to-role mapping. Platform colors and UI resources are applied by the host.
[<AbstractClass; Sealed>]
type ThemeColors private () =
    static member Create(seed: uint32, dark: bool) : IReadOnlyDictionary<string, uint32> =
        let palette = CorePalette.Of(seed)
        let scheme = if dark then DarkSchemeMapper().Map(palette) else LightSchemeMapper().Map(palette)
        let roles =
            [ "Primary", scheme.Primary; "OnPrimary", scheme.OnPrimary
              "PrimaryContainer", scheme.PrimaryContainer; "OnPrimaryContainer", scheme.OnPrimaryContainer
              "SecondaryContainer", scheme.SecondaryContainer; "OnSecondaryContainer", scheme.OnSecondaryContainer
              "Surface", scheme.Surface; "OnSurface", scheme.OnSurface; "OnSurfaceVariant", scheme.OnSurfaceVariant
              "SurfaceContainer", scheme.SurfaceContainer; "SurfaceContainerLow", scheme.SurfaceContainerLow
              "SurfaceContainerHigh", scheme.SurfaceContainerHigh; "Outline", scheme.Outline
              "OutlineVariant", scheme.OutlineVariant; "Error", scheme.Error
              "InverseSurface", scheme.InverseSurface; "InverseOnSurface", scheme.InverseOnSurface ]
        ReadOnlyDictionary<string, uint32>(Dictionary<string, uint32>(dict roles)) :> IReadOnlyDictionary<string, uint32>
