# Distributed as Add-AppDevPackage.ps1 inside each architecture's installer ZIP.
$ErrorActionPreference = 'Stop'
try {
    $packages = @(Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object { $_.Extension -in @('.appx', '.msix') })
    if ($packages.Count -ne 1) { throw 'Extract the complete installer ZIP into its own directory.' }
    $certificate = Join-Path $PSScriptRoot 'uwPDF.cer'
    $identity = New-Object Security.Cryptography.X509Certificates.X509Certificate2($certificate)
    if (!(Test-Path "Cert:/LocalMachine/TrustedPeople/$($identity.Thumbprint)")) {
        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'Trust uwPDF.cer in Local Computer > Trusted People first (administrator permission required), then run this installer as your normal Windows user.'
        }
        Import-Certificate -FilePath $certificate -CertStoreLocation Cert:/LocalMachine/TrustedPeople | Out-Null
    }
    $dependencies = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Dependencies') -File | Where-Object { $_.Extension -in @('.appx', '.msix') } | ForEach-Object FullName)
    if (!$dependencies.Count) { throw 'Installer dependencies are missing.' }
    Add-AppxPackage -Path $packages[0].FullName -DependencyPath $dependencies
    Write-Host 'uwPDF installed successfully.'
} catch {
    Write-Host $_ -ForegroundColor Red
    exit 1
} finally {
    if ($Host.Name -eq 'ConsoleHost') { Read-Host 'Press Enter to close' | Out-Null }
}
