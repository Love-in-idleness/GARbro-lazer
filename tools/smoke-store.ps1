param([string]$Version='1.0.3', [string]$Output='artifacts')
$ErrorActionPreference='Stop'
$identity=Get-Content -Raw (Join-Path $PSScriptRoot 'installers/store-identity.json') | ConvertFrom-Json
$source=(Resolve-Path (Join-Path $Output "garbro-$Version-store.msixbundle")).Path
$directory=Join-Path ([IO.Path]::GetTempPath()) ('GARbro-store-test-'+[guid]::NewGuid())
$certificate=$trust=$installed=$null
try {
    New-Item -ItemType Directory $directory | Out-Null
    # Only a disposable copy is signed. The public Store upload stays unsigned.
    $testBundle=Join-Path $directory 'store-test.msixbundle';Copy-Item $source $testBundle
    $certificate=New-SelfSignedCertificate -Type Custom -Subject $identity.publisher -KeyUsage DigitalSignature -FriendlyName 'GARbro disposable Store validation' -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
    $public=Join-Path $directory 'test.cer';Export-Certificate -Cert $certificate -FilePath $public | Out-Null
    $trust=Import-Certificate -FilePath $public -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople'
    $sdkRoot=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $sdk=Get-ChildItem $sdkRoot -Directory | Where-Object {Test-Path (Join-Path $_.FullName 'x64\signtool.exe')} | Sort-Object Name -Descending | Select-Object -First 1
    & (Join-Path $sdk.FullName 'x64\signtool.exe') sign /sha1 $certificate.Thumbprint /s My /fd SHA256 $testBundle
    if($LASTEXITCODE -ne 0){throw 'Signing disposable Store bundle failed.'}
    Add-AppxPackage -Path $testBundle
    $installed=Get-AppxPackage -Name $identity.name
    if(-not $installed -or $installed.PackageFamilyName -ne $identity.packageFamilyName){throw 'Installed Store PFN differs from the registered identity.'}
    Add-Type -TypeDefinition (Get-Content -Raw (Join-Path $PSScriptRoot 'installers/WindowsAssociation.cs'))
    foreach($extension in @('xfl','zip','dat','bin')) {
        $file=Join-Path $directory ('Русский 日本語 文件.'+$extension)
        $sample=if($extension -eq 'zip'){'tests/fixtures/generated/zip-stored.zip'}else{'tests/fixtures/generated/standard.xfl'}
        Copy-Item $sample $file
        Write-Output ('Opening through Store Shell handler: .'+$extension)
        [PackageActivation]::Open($file)
        $deadline=(Get-Date).AddSeconds(25);$process=$null
        do {
            Start-Sleep -Milliseconds 200
            $process=Get-Process GARbro.Desktop -ErrorAction SilentlyContinue | Where-Object {$_.MainWindowTitle -like ('*.'+$extension)} | Select-Object -First 1
        } while(-not $process -and (Get-Date) -lt $deadline)
        if(-not $process){
            Get-Process GARbro.Desktop -ErrorAction SilentlyContinue | Select-Object Id,MainWindowTitle,Path | Format-List | Out-String | Write-Output
            foreach($base in @($env:LOCALAPPDATA,(Join-Path $env:LOCALAPPDATA ('Packages\'+$installed.PackageFamilyName+'\LocalCache\Local')),(Join-Path $env:LOCALAPPDATA ('Packages\'+$installed.PackageFamilyName+'\LocalCache\Roaming')))) {
                $log=Join-Path $base 'GARbro\desktop.log';if(Test-Path $log){Get-Content -Tail 30 $log | Write-Output}
            }
            throw "Store Open With did not load .$extension"
        }
        Stop-Process -Id $process.Id
        if(-not $process.WaitForExit(10000)){throw 'Previous Store test process did not exit.'}
    }
    Write-Output 'Registered Store identity, self-contained installation and actual Shell file activation passed.'
} finally {
    Get-Process GARbro.Desktop -ErrorAction SilentlyContinue | Stop-Process -ErrorAction SilentlyContinue
    Get-AppxPackage -Name $identity.name | Remove-AppxPackage
    if($trust){Remove-Item -LiteralPath $trust.PSPath}
    if($certificate){Remove-Item -LiteralPath $certificate.PSPath}
    if(Test-Path $directory){Remove-Item -Recurse -Force $directory}
}
