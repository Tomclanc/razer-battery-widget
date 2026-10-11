param([switch]$Elevated)
$ErrorActionPreference = 'Stop'
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $installerArguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Elevated'
    Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList $installerArguments -Wait
    exit
}
try {
    Import-Certificate -FilePath (Join-Path $PSScriptRoot 'RazerBatteryWidget.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Add-AppxPackage -Path (Join-Path $PSScriptRoot 'RazerBatteryWidget.msix')
    'Installed successfully. Win+W > Add widgets > Razer Battery Widget' | Set-Content (Join-Path $PSScriptRoot 'installation-result.txt') -Encoding UTF8
} catch {
    $_.ToString() | Set-Content (Join-Path $PSScriptRoot 'installation-result.txt') -Encoding UTF8
    throw
}
