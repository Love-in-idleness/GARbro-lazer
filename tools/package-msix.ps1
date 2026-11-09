param([Parameter(Mandatory=$true)][string]$Stage,
      [Parameter(Mandatory=$true)][string]$Output,
      [Parameter(Mandatory=$true)][string]$Version,
      [ValidateSet('x64','arm64')][string]$Architecture = 'x64',
      [switch]$PrepareUpgradeTest)
$ErrorActionPreference = 'Stop'
# Python can inherit PowerShell 7's module path. Load the certificate provider
# from this Windows PowerShell installation rather than relying on autoload.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -Force -ErrorAction Stop
if (-not (Get-PSDrive -Name Cert -ErrorAction SilentlyContinue)) { throw 'Windows certificate provider did not create its Cert drive.' }
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$sdk = Get-ChildItem $sdkRoot -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'x64\makeappx.exe') } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Install the Windows SDK including MakeAppx and SignTool.' }
$makeappx = Join-Path $sdk.FullName 'x64\makeappx.exe'
$signtool = Join-Path $sdk.FullName 'x64\signtool.exe'
$package = Join-Path $Output "garbro-$Version-win-$Architecture.msix"
$publicCertificate = Join-Path $Output "garbro-$Version-win-$Architecture.cer"
& $makeappx pack /d $Stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx package validation failed.' }
$certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=GARbro-no-wpf-Test' -FriendlyName 'GARbro preview test signing' `
    -CertStoreLocation 'Cert:\CurrentUser\My' -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 `
    -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(1) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
$olderCertificate = $null
try {
    Export-Certificate -Cert $certificate -FilePath $publicCertificate | Out-Null
    & $signtool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $package
    if ($LASTEXITCODE -ne 0) { throw 'MSIX signing failed.' }
    if ($PrepareUpgradeTest) {
        # A lower version with the same identity and a different test certificate is used only for upgrade tests.
        $testDirectory = Join-Path $Output 'msix-test'
        New-Item -ItemType Directory -Force $testDirectory | Out-Null
        $older = Join-Path $testDirectory 'upgrade-from.msix'
        $manifestPath = Join-Path $Stage 'AppxManifest.xml'
        $manifestText = Get-Content -Raw $manifestPath
        try {
            [xml]$manifest = $manifestText
            $parts = $manifest.Package.Identity.Version.Split('.')
            $parts[3] = [string]([int]$parts[3] - 1)
            $manifest.Package.Identity.Version = $parts -join '.'
            $manifest.Save($manifestPath)
            & $makeappx pack /d $Stage /p $older /o
            if ($LASTEXITCODE -ne 0) { throw 'Upgrade test package validation failed.' }
            $olderCertificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=GARbro-no-wpf-Test' `
                -CertStoreLocation 'Cert:\CurrentUser\My' -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 `
                -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(1) `
                -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
            Export-Certificate -Cert $olderCertificate -FilePath (Join-Path $testDirectory 'upgrade-from.cer') | Out-Null
            & $signtool sign /fd SHA256 /sha1 $olderCertificate.Thumbprint /s My $older
            if ($LASTEXITCODE -ne 0) { throw 'Upgrade test signing failed.' }
        } finally { [IO.File]::WriteAllText($manifestPath, $manifestText) }
    }
    # Verification and temporary trust belong to the elevated installation test.
    # Building a package does not change the machine's certificate trust.
} finally {
    Remove-Item -LiteralPath $certificate.PSPath -DeleteKey
    if ($olderCertificate) { Remove-Item -LiteralPath $olderCertificate.PSPath -DeleteKey }
}
Write-Output $package
