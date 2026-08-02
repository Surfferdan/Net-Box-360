param(
  [string]$BaseUrl = 'http://127.0.0.1:5077'
)

$ErrorActionPreference = 'Stop'

function Invoke-JsonPost {
  param(
    [Parameter(Mandatory = $true)][string]$Uri,
    [Parameter(Mandatory = $true)]$Body,
    [hashtable]$Headers
  )

  try {
    return Invoke-RestMethod -Method Post -Uri $Uri -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8 -Compress) -Headers $Headers
  }
  catch {
    $resp = $_.Exception.Response
    if ($resp -and $resp.GetResponseStream) {
      $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
      $payload = $reader.ReadToEnd()
      throw "POST $Uri failed with HTTP $([int]$resp.StatusCode): $payload"
    }

    throw
  }
}

Write-Host '[session-smoke] Checking API diagnostics...'
$diag = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/diagnostics" -TimeoutSec 10
Write-Host "[session-smoke] API diagnostics reachable: $($diag | ConvertTo-Json -Compress)"

$username = 'flow-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$displayName = 'Flow' + (Get-Random -Minimum 1000 -Maximum 9999)
$password = 'Password123!'

Write-Host "[session-smoke] Creating account $username ..."
$null = Invoke-JsonPost -Uri "$BaseUrl/api/account/create" -Body @{ username = $username; password = $password; displayName = $displayName }

Write-Host '[session-smoke] Logging in...'
$login = Invoke-JsonPost -Uri "$BaseUrl/api/login" -Body @{ username = $username; password = $password }
if (-not $login.token) {
  throw 'Login response did not include a token.'
}
$headers = @{ Authorization = "Bearer $($login.token)" }

Write-Host '[session-smoke] Loading games list...'
$games = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/games" -Headers $headers -TimeoutSec 30
if (-not $games -or $games.Count -eq 0) {
  throw 'No games available from /api/games; cannot execute start flow.'
}
$game = $games | Select-Object -First 1
Write-Host "[session-smoke] Starting session for gameId=$($game.id) title=$($game.title)"
$start = Invoke-JsonPost -Uri "$BaseUrl/api/session/start" -Body @{ gameId = $game.id } -Headers $headers

Write-Host "[session-smoke] Start response: $($start | ConvertTo-Json -Compress)"

Write-Host '[session-smoke] Reading active session...'
$active = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/session/active" -Headers $headers -TimeoutSec 30
Write-Host "[session-smoke] Active response: $($active | ConvertTo-Json -Compress)"

if ($start.sessionId) {
  Write-Host "[session-smoke] Stopping session $($start.sessionId)..."
  $stop = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/session/$($start.sessionId)/stop" -Headers $headers -TimeoutSec 30
  Write-Host "[session-smoke] Stop response: $($stop | ConvertTo-Json -Compress)"
}

Write-Host '[session-smoke] COMPLETE'
