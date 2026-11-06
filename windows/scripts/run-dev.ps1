<#
.SYNOPSIS
  Builds the UI and runs the PhoneBridge host (dev).
.EXAMPLE
  .\run-dev.ps1            # build windows/ui/dist, then run the host
  .\run-dev.ps1 -Vite      # start the Vite dev server (hot reload) and point the host at it
  .\run-dev.ps1 -Smoke     # init everything, exit 0/1 (no window)
#>
param([switch]$Vite, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
$windows = Split-Path $PSScriptRoot -Parent
$ui = Join-Path $windows 'ui'
$hostProject = Join-Path $windows 'src/PhoneBridge.Host/PhoneBridge.Host.csproj'

Push-Location $ui
try {
    if (-not (Test-Path node_modules)) { npm ci; if ($LASTEXITCODE) { throw 'npm ci failed' } }
    if ($Vite) {
        $vite = Start-Process npm -ArgumentList 'run', 'dev', '--', '--port', '5173', '--strictPort' -PassThru -WindowStyle Minimized
        $env:PHONEBRIDGE_UI_URL = 'http://localhost:5173/'
        # The host prefers dist/ when present; hide it for this session so the dev URL is used.
        if (Test-Path dist) { Rename-Item dist dist.off }
    } else {
        npm run build; if ($LASTEXITCODE) { throw 'UI build failed' }
    }
} finally { Pop-Location }

try {
    $hostArgs = @()
    if ($Smoke) { $hostArgs += '--smoke' }
    dotnet run --project $hostProject -- @hostArgs
    exit $LASTEXITCODE
} finally {
    if ($Vite) {
        Stop-Process -Id $vite.Id -ErrorAction SilentlyContinue
        if (Test-Path (Join-Path $ui 'dist.off')) { Rename-Item (Join-Path $ui 'dist.off') dist }
    }
}
