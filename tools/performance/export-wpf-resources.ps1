param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $Directory 'GARbro.GUI.exe'))
$name=@($assembly.GetManifestResourceNames() | Where-Object {$_ -like '*.g.resources'})
if($name.Count -ne 1){throw 'Expected the original WPF UI resource bundle.'}
# Absolute pack URIs use the entry assembly. Carry the unchanged original resource
# bytes in the instrumentation entry point; production GUI assemblies stay intact.
$input=$assembly.GetManifestResourceStream($name[0])
$output=[IO.File]::Create((Join-Path $Directory 'benchmark-ui.g.resources'))
try {$input.CopyTo($output)} finally {$input.Dispose();$output.Dispose()}
