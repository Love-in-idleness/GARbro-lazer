namespace GARbro.Domain

open System

[<Struct>]
type WindowExtent =
    { Width: double; Height: double; MinimumWidth: double; MinimumHeight: double }

/// Window sizes are logical units; the available work area excludes taskbars and docks.
[<AbstractClass; Sealed>]
type WindowGeometry private () =
    static member Fit(width, height, minimumWidth, minimumHeight, workWidth, workHeight, frameWidth, frameHeight) =
        let fit requested minimum available frame =
            let maximum = max 1.0 (available - max 0.0 frame)
            let minimum = Math.Clamp(minimum, 1.0, maximum)
            let requested = if Double.IsFinite(requested) then requested else minimum
            Math.Clamp(requested, minimum, maximum), minimum
        let width, minWidth = fit width minimumWidth workWidth frameWidth
        let height, minHeight = fit height minimumHeight workHeight frameHeight
        { Width = width; Height = height; MinimumWidth = minWidth; MinimumHeight = minHeight }

    static member ConstrainOrigin(position: int, workOrigin: int, workExtent: int, windowExtent: int) =
        Math.Clamp(position, workOrigin, workOrigin + max 0 (workExtent - windowExtent))
