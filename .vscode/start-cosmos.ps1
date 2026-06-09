# Auto-starts the Azure Cosmos DB Emulator for local development, mirroring the `azurite` task.
# Used as a prelaunch dependency of the func host tasks (see tasks.json). It is resilient: it never
# fails the launch (always exits 0) and no-ops when the emulator is already running or when the app
# is configured for the in-memory store. The emulator persists its data to disk across restarts.
$ErrorActionPreference = 'SilentlyContinue'

# Skip entirely when the app is configured to use the ephemeral in-memory store.
$settings = Join-Path $PSScriptRoot '..\src\GoalFueled.Api\local.settings.json'
if (Test-Path $settings) {
    $json = Get-Content $settings -Raw | ConvertFrom-Json
    if ("$($json.Values.'GoalFueled:UseInMemoryStore')" -ieq 'true') {
        Write-Host 'GoalFueled:UseInMemoryStore=true - skipping Cosmos emulator.'
        exit 0
    }
}

$explorer = 'https://localhost:8081/_explorer/index.html'
function Test-Cosmos {
    try { Invoke-WebRequest -Uri $explorer -SkipCertificateCheck -TimeoutSec 3 -UseBasicParsing | Out-Null; $true }
    catch { $false }
}

if (Test-Cosmos) {
    Write-Host 'Cosmos DB emulator already running, continuing.'
    exit 0
}

$exe = 'C:\Program Files\Azure Cosmos DB Emulator\CosmosDB.Emulator.exe'
if (-not (Test-Path $exe)) {
    Write-Warning "Cosmos emulator not found at '$exe'. Install it (winget install Microsoft.Azure.CosmosEmulator) or set GoalFueled:UseInMemoryStore=true. Continuing without it."
    exit 0
}

Write-Host 'Starting Cosmos DB emulator (first start can take ~30-60s)...'
Start-Process -FilePath $exe

for ($i = 0; $i -lt 90; $i++) {
    if (Test-Cosmos) {
        Write-Host 'Cosmos DB emulator ready.'
        exit 0
    }
    Start-Sleep -Seconds 2
}

Write-Warning 'Cosmos emulator did not become ready in time. Continuing; the API will retry on the next start.'
exit 0
