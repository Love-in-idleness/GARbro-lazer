param([string]$Directory = "bin/Release", [string]$Output = "artifacts/legacy-catalog.json")
$ErrorActionPreference = "Stop"
$base = (Resolve-Path $Directory).Path
# Historical scheme records carry old assembly versions; resolve only sibling DLLs.
$resolver=[System.ResolveEventHandler]{ param($sender,$eventArgs)
    $name=(New-Object System.Reflection.AssemblyName($eventArgs.Name)).Name
    $candidate=Join-Path $base ($name+".dll")
    if(Test-Path $candidate){return [System.Reflection.Assembly]::LoadFrom($candidate)}
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
[System.Reflection.Assembly]::LoadFrom((Join-Path $base "GameRes.dll")) | Out-Null
$catalog = [GameRes.FormatCatalog]::Instance
$rows = @($catalog.Formats | ForEach-Object {
    [ordered]@{ tag = $_.Tag; type = $_.Type; class = $_.GetType().FullName;
        assembly = $_.GetType().Assembly.GetName().Name; description = $_.Description;
        signatures = @($_.Signatures); extensions = @($_.Extensions); can_write = $_.CanWrite }
} | Sort-Object type,tag,class)
New-Item -ItemType Directory -Force (Split-Path -Parent $Output) | Out-Null
ConvertTo-Json -Depth 8 -InputObject $rows | Set-Content -Encoding UTF8 $Output
Write-Output "$($rows.Count) runtime formats exported"

$binaryPath=Join-Path $base "GameData/Formats.dat"
if(Test-Path $binaryPath) {
    $input=[IO.File]::OpenRead($binaryPath)
    try {$catalog.DeserializeScheme($input)} finally {$input.Dispose()}
    $schemePath=Join-Path (Split-Path -Parent $Output) "Formats.json"
    $stream=[IO.File]::Create($schemePath)
    try {$catalog.SerializePortableSchemeJson($stream)} finally {$stream.Dispose()}
    Write-Output "Exported portable game schemes"
}

[AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver)
