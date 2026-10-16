param(
    [string]$ProductHandle,
    [string]$Username = 'demouser@microsoft.com',
    [string]$Password = 'Pass@word1',
    [string]$MaxioSdkRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
Push-Location $repoRoot
$apiProcess = $null
$savedEnvironment = @{}
foreach ($name in @('DOTNET_ROLL_FORWARD', 'ASPNETCORE_ENVIRONMENT', 'UseOnlyInMemoryDatabase')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    if (-not $env:APP_PORT_BLOCK_BASE -or -not $env:APP_PORT_BLOCK_SIZE -or [int]$env:APP_PORT_BLOCK_SIZE -lt 4) {
        throw 'Set APP_PORT_BLOCK_BASE and APP_PORT_BLOCK_SIZE to your assigned port block (at least four ports).'
    }
    $apiPort = [int]$env:APP_PORT_BLOCK_BASE + 3
    if (Get-NetTCPConnection -LocalPort $apiPort -State Listen -ErrorAction SilentlyContinue) {
        throw "Port $apiPort is already occupied. Stop your previous instance before verifying."
    }
    $env:DOTNET_ROLL_FORWARD = 'Major'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:UseOnlyInMemoryDatabase = 'true'

    # Pipe JSON directly to user-secrets; no credential values are written into the repository.
    if ($env:MAXIO_API_KEY -and $env:MAXIO_SITE_SUBDOMAIN -and $env:MAXIO_DEFAULT_PRODUCT_FAMILY) {
        @{
            'Maxio:ApiKey' = $env:MAXIO_API_KEY
            'Maxio:Subdomain' = $env:MAXIO_SITE_SUBDOMAIN
            'Maxio:ProductFamilyHandle' = $env:MAXIO_DEFAULT_PRODUCT_FAMILY
        } | ConvertTo-Json -Compress | dotnet user-secrets set --project src/PublicApi/PublicApi.csproj
        if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize user-secrets.' }
    }
    dotnet dev-certs https --check --trust
    if ($LASTEXITCODE -ne 0) { throw 'A trusted HTTPS development certificate is required.' }
    $buildArguments = @('build', 'src/PublicApi/PublicApi.csproj', '--nologo', '-v', 'quiet')
    if ($MaxioSdkRoot) { $buildArguments += "-p:MaxioSdkRoot=$MaxioSdkRoot" }
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'PublicApi build failed.' }

    $apiUrl = "https://localhost:$apiPort"
    $logPrefix = Join-Path ([IO.Path]::GetTempPath()) ('eshop-billing-' + [Guid]::NewGuid().ToString('N'))
    $apiProcess = Start-Process dotnet -ArgumentList @('bin/Debug/net8.0/PublicApi.dll', '--urls', $apiUrl) `
        -WorkingDirectory (Join-Path $repoRoot 'src/PublicApi') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$logPrefix.out.log" -RedirectStandardError "$logPrefix.err.log"
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($apiProcess.HasExited) { throw "PublicApi exited. Inspect $logPrefix.err.log and $logPrefix.out.log" }
        try {
            $response = Invoke-WebRequest "$apiUrl/swagger/v1/swagger.json" -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ready) { throw "PublicApi did not become ready. Inspect $logPrefix.out.log" }

    $auth = Invoke-RestMethod "$apiUrl/api/authenticate" -Method Post -ContentType application/json `
        -Body (@{ username = $Username; password = $Password } | ConvertTo-Json)
    if (-not $auth.token) { throw 'Authentication failed.' }
    $headers = @{ Authorization = "Bearer $($auth.token)" }
    $plans = @(Invoke-RestMethod "$apiUrl/api/subscription-plans" -Headers $headers)
    if (-not $plans.Count) { throw 'The configured family has no eligible plans.' }
    $plans | Format-Table handle, name, priceInCents, currency, interval, intervalUnit
    if (-not $ProductHandle) { $ProductHandle = ($plans | Sort-Object priceInCents -Descending | Select-Object -First 1).handle }
    if ($ProductHandle -notin $plans.handle) { throw 'ProductHandle must be one of the offered handles.' }
    $body = @{ productHandle = $ProductHandle } | ConvertTo-Json
    $first = Invoke-WebRequest "$apiUrl/api/subscriptions" -Headers $headers -Method Post -ContentType application/json -Body $body -TimeoutSec 90
    $result = $first.Content | ConvertFrom-Json
    if ($first.StatusCode -eq 202) { throw 'Enrollment remains pending; inspect billing claims and provider availability before sending another write.' }
    Write-Output "Subscribe: HTTP $($first.StatusCode)"
    $result.subscription | Format-List

    $repeat = Invoke-RestMethod "$apiUrl/api/subscriptions" -Headers $headers -Method Post -ContentType application/json -Body $body
    if ($repeat.subscription.id -ne $result.subscription.id) { throw 'Repeat request did not return the same subscription.' }
    $mine = Invoke-RestMethod "$apiUrl/api/my-subscriptions" -Headers $headers
    if ($result.subscription.id -notin $mine.subscriptions.id -or $mine.hasPendingOperations) { throw 'Account subscription verification failed.' }
    # Assert JWT protection without following a challenge or printing the token.
    try {
        Invoke-WebRequest "$apiUrl/api/my-subscriptions" | Out-Null
        throw 'Anonymous account read unexpectedly succeeded.'
    } catch {
        if ([int]$_.Exception.Response.StatusCode -ne 401) { throw }
    }
    Write-Output 'Verified: catalog, enrollment, same-ID repeat, account read, and anonymous 401.'
    Write-Output "Logs: $logPrefix.out.log"
} finally {
    if ($apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id }
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
    Pop-Location
}
