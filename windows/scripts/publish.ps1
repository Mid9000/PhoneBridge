<#
.SYNOPSIS
  Publishes PhoneBridge as a self-contained folder (no .NET install needed): windows/publish/PhoneBridge.
.EXAMPLE
  .\publish.ps1
  .\publish.ps1 -Output D:\Apps\PhoneBridge
#>
param([string]$Output = (Join-Path (Split-Path $PSScriptRoot -Parent) 'publish/PhoneBridge'))
$ErrorActionPreference = 'Stop'
$windows = Split-Path $PSScriptRoot -Parent
$ui = Join-Path $windows 'ui'

Push-Location $ui
try {
    if (-not (Test-Path node_modules)) { npm ci; if ($LASTEXITCODE) { throw 'npm ci failed' } }
    npm run build; if ($LASTEXITCODE) { throw 'UI build failed' }
} finally { Pop-Location }

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
dotnet publish (Join-Path $windows 'src/PhoneBridge.Host/PhoneBridge.Host.csproj') -c Release -r win-x64 --self-contained true -o $Output
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# The host looks for <exe dir>\ui\index.html first.
Copy-Item (Join-Path $ui 'dist') (Join-Path $Output 'ui') -Recurse
Write-Host "Published to $Output (run PhoneBridge.exe)"
