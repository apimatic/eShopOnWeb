param([switch]$Stop)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src/PublicApi/PublicApi.csproj'
$taskKey = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($repo))).Substring(0, 12)
$state = Join-Path ([IO.Path]::GetTempPath()) "eshop-subscriptions-$taskKey"
$pidFile = "$state.pid"
if (Test-Path $pidFile) {
    $prior = Get-Process -Id ([int](Get-Content $pidFile)) -ErrorAction SilentlyContinue
    if ($prior) {
        if (!$Stop) { throw "PublicApi is already running (PID $($prior.Id)). Stop it with this script -Stop first." }
        $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $($prior.Id)"
        if ($processInfo.CommandLine -notlike "*$(Join-Path $repo 'src/PublicApi/bin/Debug/net8.0/PublicApi.dll')*") {
            throw 'PID belongs to another process; refusing to stop it.'
        }
        Stop-Process -Id $prior.Id
    }
    Remove-Item -LiteralPath $pidFile
}
if ($Stop) { return }

$settings = @{
    'Maxio:ApiKey' = $env:MAXIO_API_KEY
    'Maxio:Subdomain' = $env:MAXIO_SITE_SUBDOMAIN
    'Maxio:ProductFamilyHandle' = $env:MAXIO_DEFAULT_PRODUCT_FAMILY
}
foreach ($key in $settings.Keys) {
    if ([string]::IsNullOrWhiteSpace($settings[$key])) { throw "Environment value for $key is missing." }
}
if ([string]::IsNullOrWhiteSpace($env:MAXIO_ENVIRONMENT)) { throw 'MAXIO_ENVIRONMENT must identify the supplied sandbox region.' }
# SDK regions are US/EU; sandbox selection is the site. Credentials must belong to the intended sandbox.
if ($env:MAXIO_ENVIRONMENT -ne 'US' -and [string]::IsNullOrWhiteSpace($env:Maxio__BaseUrl)) {
    throw 'For a non-US site set Maxio__BaseUrl to the intended sandbox API base URL.'
}
$settings | ConvertTo-Json | & dotnet user-secrets set --project $project
if ($LASTEXITCODE -ne 0) { throw 'User-secrets import failed.' }
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:UseOnlyInMemoryDatabase = 'true'
if (!$env:APP_PORT_BLOCK_BASE -or [int]$env:APP_PORT_BLOCK_SIZE -lt 2) { throw 'An assigned port block of at least two ports is required.' }
$httpsPort = [int]$env:APP_PORT_BLOCK_BASE
$httpPort = $httpsPort + 1
$env:ASPNETCORE_URLS = "https://localhost:$httpsPort;http://localhost:$httpPort"
& dotnet build $project --nologo -v:q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$dll = Join-Path $repo 'src/PublicApi/bin/Debug/net8.0/PublicApi.dll'
$process = Start-Process dotnet -ArgumentList @('"' + $dll + '"') -WorkingDirectory (Split-Path $project) -WindowStyle Hidden -RedirectStandardOutput "$state.out.log" -RedirectStandardError "$state.err.log" -PassThru
$process.Id | Set-Content $pidFile
try {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw "PublicApi exited. See $state.err.log" }
        try {
            $null = Invoke-WebRequest "https://localhost:$httpsPort/swagger/index.html" -SkipCertificateCheck -TimeoutSec 2
            Write-Output "PublicApi ready: https://localhost:$httpsPort/swagger (PID $($process.Id))"
            Write-Output "Logs: $state.out.log and $state.err.log"
            return
        } catch { Start-Sleep -Milliseconds 500 }
    }
    throw 'PublicApi did not become ready within 30 seconds.'
} catch {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $pidFile -ErrorAction SilentlyContinue
    throw
}
