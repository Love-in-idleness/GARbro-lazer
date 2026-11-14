# Compare independently generated, resource-only PE32/PE32+ files against the
# original Win32 resource and version accessors. No executable code is loaded.
$ErrorActionPreference='Stop'
$unicodeName=[string]::Concat([char[]]@(0x65E5,0x672C,0x8A9E))
[void][Reflection.Assembly]::LoadFrom((Resolve-Path 'bin/Release/GameRes.dll'))
[void][Reflection.Assembly]::LoadFrom((Resolve-Path 'bin/Release/ArcFormats.dll'))
foreach($name in @('resources32.exe','resources64.exe')) {
    $path=(Resolve-Path "tests/fixtures/generated/$name").Path
    $resources=[GameRes.Formats.ExeFile+ResourceAccessor]::new($path)
    try {
        if((@($resources.EnumTypes()) -join ',') -cne 'CUSTOM,#10,#16'){throw "$name resource types differ"}
        if((@($resources.EnumNames('CUSTOM')) -join ',') -cne $unicodeName){throw "$name resource names differ"}
        if([Text.Encoding]::UTF8.GetString($resources.GetResource($unicodeName,'CUSTOM')) -cne "Unicode resource`n") {throw "$name named data differs"}
        if([Text.Encoding]::UTF8.GetString($resources.GetResource('#1','#10')) -cne "Hello, PE resources!`n") {throw "$name numeric data differs"}
        if($resources.GetResourceSize('#2','#10') -ne 0){throw "$name empty resource differs"}
    } finally {$resources.Dispose()}
    $version=[GameRes.Formats.ExeFile+VersionAccessor]::new($path)
    if(($version.FileVersions -join '.') -cne '1.2.3.4' -or ($version.ProductVersions -join '.') -cne '5.6.7.8') {
        throw "$name fixed versions differ"
    }
    Write-Output "PASS $name resources and versions"
}
