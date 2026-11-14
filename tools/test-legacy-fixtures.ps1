param([string]$Directory="bin/Release",[string]$Fixtures="tests/fixtures/generated")
$ErrorActionPreference="Stop"
$base=(Resolve-Path $Directory).Path
[System.Reflection.Assembly]::LoadFrom((Join-Path $base "GameRes.dll")) | Out-Null
$catalog=[GameRes.FormatCatalog]::Instance
$handler=[GameRes.ParametersRequestEventHandler]{ param($source,$eventArgs)
    $eventArgs.Options=$source.GetDefaultOptions();$eventArgs.InputResult=$true
}
$catalog.add_ParametersRequest($handler)
$manifest=Get-Content (Join-Path $Fixtures "manifest.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$sha=[System.Security.Cryptography.SHA256]::Create()
try {
    foreach($fixture in $manifest.archives) {
        if($fixture.file -in @("unsafe.xfl","collision.xfl")){continue}
        $path=(Resolve-Path (Join-Path $Fixtures $fixture.file)).Path
        $arc=[GameRes.ArcFile]::TryOpen($path)
        if($null -eq $arc){throw "Legacy cannot open $($fixture.file): $($catalog.LastError)"}
        try {
            if($arc.Tag -ne $fixture.format){throw "Wrong format for $($fixture.file): $($arc.Tag)"}
            if($arc.Dir.Count -ne $fixture.entries.Count){throw "Wrong entry count for $($fixture.file)"}
            foreach($expected in $fixture.entries) {
                $entry=@($arc.Dir | Where-Object { $_.Name.Replace('\','/') -ceq $expected.path })
                if($entry.Count -ne 1){throw "Missing or duplicate $($expected.path) in $($fixture.file); actual names: $((@($arc.Dir | Select-Object -First 10 | ForEach-Object {$_.Name})) -join ', ')"}
                $input=$arc.OpenEntry($entry[0]);$copy=New-Object System.IO.MemoryStream
                try {
                    $input.CopyTo($copy)
                    $actual=([BitConverter]::ToString($sha.ComputeHash($copy.ToArray()))).Replace('-','').ToLowerInvariant()
                    if($copy.Length -ne $expected.size -or $actual -ne $expected.sha256){throw "Digest mismatch for $($fixture.file)/$($expected.path)"}
                } finally {$input.Dispose();$copy.Dispose()}
            }
            Write-Output "PASS $($fixture.file)"
        } finally {$arc.Dispose()}
    }
    foreach($fixture in $manifest.images) {
        $path=(Resolve-Path (Join-Path $Fixtures $fixture.file)).Path
        $input=[GameRes.VFS]::OpenBinaryStream($path)
        try {$image=[GameRes.ImageFormat]::Read($input)} finally {$input.Dispose()}
        if($null -eq $image -or $image.Width -ne $fixture.width -or $image.Height -ne $fixture.height -or
           $image.OffsetX -ne $fixture.offset_x -or $image.OffsetY -ne $fixture.offset_y) {
            throw "Image metadata mismatch for $($fixture.file)"
        }
        $bitmap=[System.Windows.Media.Imaging.FormatConvertedBitmap]::new(
            $image.Bitmap,[System.Windows.Media.PixelFormats]::Bgra32,$null,0)
        $pixels=New-Object byte[] ($fixture.width*$fixture.height*4)
        $bitmap.CopyPixels($pixels,$fixture.width*4,0)
        if([Convert]::ToBase64String($pixels) -cne [Convert]::ToBase64String([byte[]]$fixture.bgra32)) {
            throw "Image pixels mismatch for $($fixture.file)"
        }
        Write-Output "PASS $($fixture.file) pixels and metadata"
    }
} finally {$sha.Dispose();$catalog.remove_ParametersRequest($handler)}
