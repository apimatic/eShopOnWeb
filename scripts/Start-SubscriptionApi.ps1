[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'src/PublicApi/PublicApi.csproj'
$portBase = 0
$portSize = 0
if (-not [int]::TryParse($env:APP_PORT_BLOCK_BASE, [ref]$portBase) -or
    -not [int]::TryParse($env:APP_PORT_BLOCK_SIZE, [ref]$portSize) -or $portSize -lt 5) {
    throw 'APP_PORT_BLOCK_BASE and APP_PORT_BLOCK_SIZE (at least 5) are required.'
}
$runtimeDirectory = Join-Path ([IO.Path]::GetTempPath()) "eshop-subscriptions-$portBase"
New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
$pidFile = Join-Path $runtimeDirectory 'api.pid'
if (Test-Path -LiteralPath $pidFile) {
    $existing = Get-Process -Id ([int](Get-Content -LiteralPath $pidFile)) -ErrorAction SilentlyContinue
    if ($existing) { throw "A previous instance is recorded at $pidFile. Stop it before starting another." }
}
foreach ($offset in @(3, 4)) {
    if (Get-NetTCPConnection -LocalPort ($portBase + $offset) -State Listen -ErrorAction SilentlyContinue) {
        throw "Assigned API port $($portBase + $offset) is already in use."
    }
}

# Values go directly from memory to the user-secret store, never to a repository file or a command argument.
$secrets = @{}
foreach ($mapping in @(
    @('MAXIO_API_KEY', 'Maxio:ApiKey'),
    @('MAXIO_SITE_SUBDOMAIN', 'Maxio:Subdomain'),
    @('MAXIO_DEFAULT_PRODUCT_FAMILY', 'Maxio:ProductFamilyHandle'))) {
    $value = [Environment]::GetEnvironmentVariable($mapping[0])
    if ([string]::IsNullOrWhiteSpace($value)) { throw "$($mapping[0]) is required." }
    $secrets[$mapping[1]] = $value
}
$env:DOTNET_ROLL_FORWARD = 'Major'
$secrets | ConvertTo-Json -Compress | dotnet user-secrets set --project $project
if ($LASTEXITCODE -ne 0) { throw 'Could not configure user-secrets.' }
dotnet build $project --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'PublicApi build failed.' }
dotnet dev-certs https --check --trust
if ($LASTEXITCODE -ne 0) { throw 'A trusted HTTPS developer certificate is required.' }
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:UseOnlyInMemoryDatabase = 'true'
$env:ASPNETCORE_URLS = "https://localhost:$($portBase + 3);http://localhost:$($portBase + 4)"
$dll = Join-Path $repoRoot 'src/PublicApi/bin/Debug/net8.0/PublicApi.dll'
$process = Start-Process dotnet -ArgumentList ('"' + $dll + '"') -WorkingDirectory (Join-Path $repoRoot 'src/PublicApi') -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $runtimeDirectory 'stdout.log') -RedirectStandardError (Join-Path $runtimeDirectory 'stderr.log')
$process.Id | Set-Content -LiteralPath $pidFile
$api = "https://localhost:$($portBase + 3)"
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw "API exited. See logs in $runtimeDirectory." }
        try {
            $response = Invoke-WebRequest "$api/swagger/v1/swagger.json" -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "API did not become ready. See logs in $runtimeDirectory." }
    Write-Output "PublicApi ready: $api/swagger"
    Write-Output "PID: $($process.Id); logs: $runtimeDirectory"
    Write-Output "Stop: Stop-Process -Id $($process.Id)"
} catch {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw
}
