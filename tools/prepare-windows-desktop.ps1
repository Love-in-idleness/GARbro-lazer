# Only for disposable GitHub-hosted test machines, never application startup.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This desktop preparation is restricted to GitHub Actions.' }

# Windows 11 ARM images may leave first-login privacy setup over the desktop.
# Close its hosts without choosing or persisting any privacy preference.
$setupHosts = @('UserOOBEBroker', 'CloudExperienceHostBroker', 'CloudExperienceHost', 'msoobe', 'WWAHost', 'AccountsControlHost')
Get-Process | Where-Object { $_.ProcessName -in $setupHosts } |
    Select-Object ProcessName, Id, SessionId | Format-Table -AutoSize
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
using System.Text;
public static class GarbroDesktopProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder text, int size);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll")] public static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, uint size, out uint needed);
    public static string ObjectName(IntPtr handle) {
        var text=new StringBuilder(256);uint needed;
        return GetUserObjectInformation(handle,2,text,512,out needed)?text.ToString():"unavailable";
    }
    public static string Describe(IntPtr window) {
        uint pid;GetWindowThreadProcessId(window,out pid);
        var title=new StringBuilder(256);var type=new StringBuilder(256);
        GetWindowText(window,title,256);GetClassName(window,type,256);
        string process="unknown";
        try {process=System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;}catch(ArgumentException){}
        return "HWND="+window+" PID="+pid+" process="+process+" class="+type+" title="+title;
    }
    public static int OwnerPid(IntPtr window) {
        uint pid;GetWindowThreadProcessId(window,out pid);return (int)pid;
    }
}
'@
Write-Host "Test process session: $([System.Diagnostics.Process]::GetCurrentProcess().SessionId)"
Write-Host "Window station: $([GarbroDesktopProbe]::ObjectName([GarbroDesktopProbe]::GetProcessWindowStation()))"
Write-Host "Thread desktop: $([GarbroDesktopProbe]::ObjectName([GarbroDesktopProbe]::GetThreadDesktop([GarbroDesktopProbe]::GetCurrentThreadId())))"
$inputDesktop = [GarbroDesktopProbe]::OpenInputDesktop(0, $false, 1)
Write-Host "Input desktop: $([GarbroDesktopProbe]::ObjectName($inputDesktop))"
if ($inputDesktop -ne [IntPtr]::Zero) { [GarbroDesktopProbe]::CloseDesktop($inputDesktop) | Out-Null }
$window = New-Object System.Windows.Forms.Form
$window.Text = 'GARbro CI desktop readiness'
$window.Size = New-Object System.Drawing.Size(400, 240)
$window.StartPosition = 'CenterScreen'
$window.TopMost = $true
$window.BackColor = [System.Drawing.Color]::FromArgb(18, 171, 52)
try {
    $window.Show()
    $screen = $window.PointToScreen((New-Object System.Drawing.Point(200, 120)))
    $point = New-Object GarbroDesktopProbe+Point
    $point.X = $screen.X; $point.Y = $screen.Y
    foreach ($attempt in 1..4) {
        $window.TopMost = $false
        $window.TopMost = $true
        $window.BringToFront()
        $window.Activate()
        foreach ($tick in 1..20) {
            [System.Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 50
        }
        $visible = [GarbroDesktopProbe]::GetAncestor([GarbroDesktopProbe]::WindowFromPoint($point), 2)
        if ($visible -eq $window.Handle) { break }
        Write-Host "Covering shell surface: $([GarbroDesktopProbe]::Describe($visible))"
        $cover = Get-Process -Id ([GarbroDesktopProbe]::OwnerPid($visible)) -ErrorAction SilentlyContinue
        # The shell can open Start after its first-login web host exits.
        if ($cover -and $cover.ProcessName -in ($setupHosts + @('StartMenuExperienceHost', 'SearchHost', 'SearchApp'))) {
            $cover | Stop-Process -Force
            Start-Sleep -Milliseconds 500
        } else { break }
    }
    Write-Host "Test window: $([GarbroDesktopProbe]::Describe($window.Handle)); sample=($($screen.X),$($screen.Y))"
    Write-Host "Visible window: $([GarbroDesktopProbe]::Describe($visible))"
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
