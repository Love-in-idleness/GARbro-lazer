param([string]$Output = 'artifacts', [string]$Version = '1.0.3', [ValidateSet('x64','arm64')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path (Join-Path $Output "garbro-$Version-win-$Architecture.msix")).Path
$certificate = (Resolve-Path (Join-Path $Output "garbro-$Version-win-$Architecture.cer")).Path
$older = (Resolve-Path (Join-Path $Output 'msix-test/upgrade-from.msix')).Path
$olderCertificate = (Resolve-Path (Join-Path $Output 'msix-test/upgrade-from.cer')).Path
$trusted=$olderTrust=$root=$olderRoot=$null
$identity = 'LoveInIdleness.GARbro'
try {
    $trusted = Import-Certificate -FilePath $certificate -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
    $olderTrust = Import-Certificate -FilePath $olderCertificate -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $sdk = Get-ChildItem $sdkRoot -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'x64\signtool.exe') } | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $sdk) { throw 'Windows SDK SignTool is missing.' }
    $signtool = Join-Path $sdk.FullName 'x64\signtool.exe'
    try {
        # LocalMachine root import is noninteractive in this elevated disposable test.
        # Remove it before installing: installation must work with TrustedPeople only.
        $root = Import-Certificate -FilePath $certificate -CertStoreLocation 'Cert:\LocalMachine\Root'
        $olderRoot = Import-Certificate -FilePath $olderCertificate -CertStoreLocation 'Cert:\LocalMachine\Root'
        foreach ($candidate in @($package,$older)) {
            & $signtool verify /pa /v $candidate
            if ($LASTEXITCODE -ne 0) { throw 'MSIX signature verification failed.' }
        }
    } finally {
        if ($root) { Remove-Item -LiteralPath $root.PSPath; $root=$null }
        if ($olderRoot) { Remove-Item -LiteralPath $olderRoot.PSPath; $olderRoot=$null }
    }
    Write-Output 'Installing with TrustedPeople test certificates.'
    Add-AppxPackage -Path $older
    $before = Get-AppxPackage -Name $identity
    if (-not $before) { throw 'MSIX did not install.' }
    Add-AppxPackage -Path $package
    $installed = Get-AppxPackage -Name $identity
    if ([version]$installed.Version -le [version]$before.Version) { throw 'MSIX upgrade did not advance the version.' }
    if ($installed.PackageFamilyName -ne $before.PackageFamilyName) { throw 'MSIX identity changed during upgrade.' }
    Add-Type -TypeDefinition (Get-Content -Raw (Join-Path $PSScriptRoot 'installers/WindowsAssociation.cs'))
    . (Join-Path $PSScriptRoot 'installers/WindowsActivation.ps1')
    $activationLogs=@((Join-Path $env:LOCALAPPDATA 'GARbro\desktop.log'),
        (Join-Path $env:LOCALAPPDATA ('Packages\'+$installed.PackageFamilyName+'\LocalCache\Local\GARbro\desktop.log')),
        (Join-Path $env:LOCALAPPDATA ('Packages\'+$installed.PackageFamilyName+'\LocalCache\Roaming\GARbro\desktop.log')))
    $fixtureDirectory = Join-Path ([IO.Path]::GetTempPath()) ('GARbro Русский 日本語 ' + [char]0x6d4b + [char]0x8bd5 + ' test ' + [guid]::NewGuid())
    New-Item -ItemType Directory $fixtureDirectory | Out-Null
    $retained=@()
    $scenarios=@(
        @{Extension='xfl';KeepOpen=$false},
        @{Extension='xp3';KeepOpen=$false},
        @{Extension='zip';KeepOpen=$false},
        @{Extension='dat';KeepOpen=$false},
        @{Extension='bin';KeepOpen=$false},
        @{Extension='xfl';KeepOpen=$true},
        @{Extension='zip';KeepOpen=$false}
    )
    foreach($scenario in $scenarios) {
    $extension=$scenario.Extension
    $fixture = Join-Path $fixtureDirectory ('standard.'+$extension)
    # Test the actual Open With handler, including the formerly excluded XFL/DAT/BIN/ZIP suffixes.
    $sample=if($extension -eq 'zip'){'tests/fixtures/generated/zip-stored.zip'}else{'tests/fixtures/generated/standard.xfl'}
    Copy-Item $sample $fixture
    $started=[DateTimeOffset]::UtcNow
    $archive=if($extension -eq 'zip'){'ZIP'}else{'XFL'}
    [PackageActivation]::Open($fixture,($installed.PackageFamilyName+'!GARbro'))
    $deadline = (Get-Date).AddSeconds(25)
    do {
        Start-Sleep -Milliseconds 250
        $process = Get-Process -Name GARbro.Desktop -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like ('*standard.'+$extension+'*') } | Select-Object -First 1
        $loaded=Test-AssociatedArchiveLoaded $activationLogs $started $archive
        if ($process -and $loaded) { break }
    } while ((Get-Date) -lt $deadline)
    if (-not $process -or -not $loaded -or $process.MainWindowTitle -notlike ('*standard.'+$extension+'*')) {
        Get-CimInstance Win32_Process | Where-Object { $_.Name -like '*GARbro*' } | Select-Object Name,ProcessId,SessionId,CommandLine | Format-List | Out-String | Write-Output
        Get-Process -Name GARbro.Desktop -ErrorAction SilentlyContinue | Select-Object Id,MainWindowTitle,Path | Format-List | Out-String | Write-Output
        $logs = @((Join-Path $env:LOCALAPPDATA 'GARbro\desktop.log'),
            (Join-Path $env:LOCALAPPDATA ('Packages\'+$installed.PackageFamilyName+'\LocalCache\Local\GARbro\desktop.log')),
            (Join-Path $env:LOCALAPPDATA ('Packages\'+$installed.PackageFamilyName+'\LocalCache\Roaming\GARbro\desktop.log')))
        foreach($log in $logs){if(Test-Path $log){Get-Content -Tail 30 $log | Write-Output}}
        throw 'Packaged file activation failed.'
    }
    if($scenario.KeepOpen){$retained+=$process;[PackageActivation]::Complete();continue}
    if(-not $process.CloseMainWindow()){throw 'Cannot close the associated-file window.'}
    if(-not $process.WaitForExit(10000)){throw 'Previous MSIX test process did not exit.'}
    [PackageActivation]::Complete()
    }
    foreach($retainedProcess in $retained) {
        if(-not $retainedProcess.HasExited) {
            if(-not $retainedProcess.CloseMainWindow() -or -not $retainedProcess.WaitForExit(10000)){throw 'Warm activation window did not close.'}
        }
    }
    Remove-AppxPackage -Package $installed.PackageFullName
    if (Get-AppxPackage -Name $identity) { throw 'MSIX uninstall failed.' }
    Write-Output 'MSIX install, upgrade, associated-file activation and uninstall passed.'
} finally {
    if('PackageActivation' -as [type]){[PackageActivation]::Complete()}
    Get-AppxPackage -Name $identity | Remove-AppxPackage
    if ($trusted) { Remove-Item -LiteralPath $trusted.PSPath }
    if ($root) { Remove-Item -LiteralPath $root.PSPath }
    if ($olderRoot) { Remove-Item -LiteralPath $olderRoot.PSPath }
    if ($olderTrust) { Remove-Item -LiteralPath $olderTrust.PSPath }
    if ($fixtureDirectory) { Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force }
}
