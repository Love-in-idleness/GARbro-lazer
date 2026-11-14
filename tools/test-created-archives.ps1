param([string]$Directory='artifacts/legacy', [Parameter(Mandatory=$true)][string]$Manifest)
$ErrorActionPreference='Stop'
$base=(Resolve-Path $Directory).Path
[Reflection.Assembly]::LoadFrom((Join-Path $base 'GameRes.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $base 'Newtonsoft.Json.dll')) | Out-Null
$catalog=[GameRes.FormatCatalog]::Instance
$script:parameters=$null
$handler=[GameRes.ParametersRequestEventHandler]{param($source,$eventArgs)
    $options=$source.GetDefaultOptions()
    if($script:parameters -and $script:parameters -ne 'null') {
        [Newtonsoft.Json.JsonConvert]::PopulateObject($script:parameters,$options)
    }
    $eventArgs.Options=$options;$eventArgs.InputResult=$true
}
$catalog.add_ParametersRequest($handler)
$manifestPath=(Resolve-Path $Manifest).Path
$fixtures=Get-Content -Raw -Encoding UTF8 $manifestPath | ConvertFrom-Json
if($fixtures.Count -ne 22){throw 'Expected creation outputs from all 22 writable archive providers.'}
$sha=[Security.Cryptography.SHA256]::Create()
try {
    foreach($fixture in $fixtures) {
        $script:parameters=$fixture.options
        $path=Join-Path (Split-Path $manifestPath) $fixture.file
        $arc=[GameRes.ArcFile]::TryOpen($path)
        if($null -eq $arc){throw "Legacy reader rejected created $($fixture.tag): $($catalog.LastError)"}
        try {
            if($arc.Tag -ne $fixture.tag){throw "Unexpected reader: $($arc.Tag) for $($fixture.tag)"}
            $entries=@($arc.Dir | Where-Object {$_.Name -ceq $fixture.entry})
            if($entries.Count -ne 1){throw "Missing or duplicated entry in created $($fixture.tag)"}
            $input=$arc.OpenEntry($entries[0]);$copy=[IO.MemoryStream]::new()
            try {
                $buffer=New-Object byte[] 81920
                while(($n=$input.Read($buffer,0,$buffer.Length)) -gt 0) {
                    if($copy.Length+$n -gt 16777216){throw 'Unexpectedly large creation test output.'}
                    $copy.Write($buffer,0,$n)
                }
                $digest=([BitConverter]::ToString($sha.ComputeHash($copy.ToArray()))).Replace('-','').ToLowerInvariant()
                if($copy.Length -ne $fixture.size -or $digest -cne $fixture.sha256){throw "Legacy read-back hash mismatch for $($fixture.tag)"}
            } finally {$input.Dispose();$copy.Dispose()}
            Write-Output "PASS created $($fixture.tag): legacy reader agrees with original bytes"
        } finally {$arc.Dispose()}
    }
} finally {$sha.Dispose();$catalog.remove_ParametersRequest($handler)}
