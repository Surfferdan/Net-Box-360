param(
  [Parameter(Mandatory = $true)]
  [ValidateSet('provision', 'release', 'status', 'cleanup')]
  [string]$action,

  [string]$session,
  [string]$title,
  [string]$display,
  [string]$baseUrl = $env:NETBOX_VDISPLAY_BASEURL
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($baseUrl)) {
  $baseUrl = 'http://127.0.0.1:47990'
}

function Write-Json([object]$obj) {
  $obj | ConvertTo-Json -Depth 8 -Compress
}

switch ($action) {
  'provision' {
    if ([string]::IsNullOrWhiteSpace($session)) {
      throw 'session is required for provision'
    }

    $payload = @{ sessionId = $session; gameTitle = $title }
    $result = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/displays/provision" -ContentType 'application/json' -Body (Write-Json $payload)
    if (-not $result.displayId) {
      throw 'service returned no displayId'
    }

    Write-Json @{ displayId = [string]$result.displayId; status = 'active' }
    break
  }
  'release' {
    if ([string]::IsNullOrWhiteSpace($display)) {
      throw 'display is required for release'
    }

    $payload = @{ sessionId = $session; displayId = $display }
    $null = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/displays/release" -ContentType 'application/json' -Body (Write-Json $payload)
    Write-Json @{ displayId = $display; status = 'released' }
    break
  }
  'status' {
    if ([string]::IsNullOrWhiteSpace($display)) {
      throw 'display is required for status'
    }

    $result = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/displays/$display/status"
    $status = if ($result.status) { [string]$result.status } else { 'unknown' }
    Write-Json @{ displayId = $display; status = $status }
    break
  }
  'cleanup' {
    $null = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/displays/cleanup"
    Write-Json @{ status = 'ok' }
    break
  }
}
