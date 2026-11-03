# Only for disposable GitHub-hosted test machines, never application startup.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This desktop preparation is restricted to GitHub Actions.' }

# Windows 11 ARM images may leave first-login privacy setup over the desktop.
# Close its hosts without choosing or persisting any privacy preference.
$setupHosts = @('UserOOBEBroker', 'CloudExperienceHostBroker', 'CloudExperienceHost', 'msoobe')
Get-Process | Where-Object { $_.MainWindowHandle -ne 0 -or $_.ProcessName -in $setupHosts } |
    Select-Object ProcessName, Id, MainWindowTitle | Format-Table -AutoSize
foreach ($pass in 1..3) {
    $running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in $setupHosts }
    if (-not $running) { break }
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class GarbroDesktopProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
}
'@
$window = New-Object System.Windows.Forms.Form
$window.Text = 'GARbro CI desktop readiness'
$window.Size = New-Object System.Drawing.Size(400, 240)
$window.StartPosition = 'CenterScreen'
$window.TopMost = $true
$window.BackColor = [System.Drawing.Color]::FromArgb(18, 171, 52)
try {
    $window.Show()
    $window.Activate()
    foreach ($tick in 1..20) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 50
    }
    $screen = $window.PointToScreen((New-Object System.Drawing.Point(200, 120)))
    $point = New-Object GarbroDesktopProbe+Point
    $point.X = $screen.X; $point.Y = $screen.Y
    $visible = [GarbroDesktopProbe]::GetAncestor([GarbroDesktopProbe]::WindowFromPoint($point), 2)
    New-Item -ItemType Directory -Force artifacts/desktop-readiness | Out-Null
    & "$PSScriptRoot/capture-window.ps1" -X $window.Left -Y $window.Top -Width $window.Width -Height $window.Height -Output artifacts/desktop-readiness/window.png
    if ($visible -ne $window.Handle) {
        Get-Process | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object ProcessName, Id, MainWindowTitle | Format-Table -AutoSize
        throw "The CI desktop is still covered: expected $($window.Handle), visible $visible."
    }
    Write-Host 'The native test desktop is visible and ready.'
} finally {
    $window.Close()
    $window.Dispose()
}
