<#
  Signs a release of Claude Multi-Account Launcher with a code-signing certificate
  that is already installed on this PC (cloud signing service or hardware token).

  Usage:
    .\scripts\sign.ps1 -Exe .\build\ClaudeMultiAccountLauncher.exe -Thumbprint 0123ABCD...
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Thumbprint,
    [string]$TimestampUrl = "http://time.certum.pl"
)
$ErrorActionPreference = 'Stop'

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if (-not $signtool) { throw "signtool.exe not found. Install the Windows SDK (Signing Tools for Desktop Apps)." }

& $signtool sign /sha1 $Thumbprint /fd sha256 /tr $TimestampUrl /td sha256 /d "Claude Multi-Account Launcher" $Exe
if ($LASTEXITCODE -ne 0) { throw "Signing failed." }

& $signtool verify /pa /v $Exe
$hash = (Get-FileHash $Exe -Algorithm SHA256).Hash
Write-Host "Signed. SHA-256: $hash" -ForegroundColor Green
