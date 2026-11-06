<#
.SYNOPSIS
  Allows PhoneBridge through Windows Firewall on PRIVATE networks only (SPEC §9).
.DESCRIPTION
  Inbound TCP 47821 (HTTPS) and 47822 (HTTP for iOS Shortcuts), scoped to the PhoneBridge executable and to the
  local subnet. Public and Domain profiles stay closed. mDNS (UDP 5353) is answered by the Windows DNS client
  service, which already has its own built-in rule. Run elevated (the app asks via UAC).
.EXAMPLE
  .\firewall.ps1 -Program "C:\Apps\PhoneBridge\PhoneBridge.exe"
  .\firewall.ps1 -Remove
#>
param(
    [string]$Program,
    [switch]$Remove
)
$ErrorActionPreference = 'Stop'
$group = 'PhoneBridge'

Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if ($Remove) { Write-Host 'PhoneBridge firewall rules removed.'; return }

$common = @{
    Group         = $group
    Direction     = 'Inbound'
    Action        = 'Allow'
    Protocol      = 'TCP'
    Profile       = 'Private'
    RemoteAddress = 'LocalSubnet'
}
if ($Program) { $common.Program = $Program }

New-NetFirewallRule @common -DisplayName 'PhoneBridge HTTPS (47821)' -LocalPort 47821 | Out-Null
New-NetFirewallRule @common -DisplayName 'PhoneBridge Shortcuts HTTP (47822)' -LocalPort 47822 | Out-Null
Write-Host 'PhoneBridge firewall rules created (Private profile, local subnet only).'

$profile = (Get-NetConnectionProfile | Select-Object -First 1).NetworkCategory
if ($profile -ne 'Private') {
    Write-Warning "Your current network is '$profile'. Set it to Private in Settings > Network & Internet for the iPhone to connect."
}
