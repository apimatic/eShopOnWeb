[CmdletBinding()]
param(
    [string]$ProductHandle,
    [string]$Username = 'demouser@microsoft.com',
    [string]$Password = 'Pass@word1'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$env:APP_PORT_BLOCK_BASE -or !$env:APP_PORT_BLOCK_SIZE) { throw 'The assigned APP_PORT_BLOCK_BASE and APP_PORT_BLOCK_SIZE are required.' }
$port = [int]$env:APP_PORT_BLOCK_BASE
if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) { throw "Assigned port $port is already in use. Stop your previous instance first." }

Push-Location $repo
$hostProcess = $null
$http = $null
$oldEnvironment = @{}
foreach ($name in @('UseOnlyInMemoryDatabase', 'ASPNETCORE_ENVIRONMENT', 'DOTNET_ROLL_FORWARD')) {
    $oldEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
$logBase = Join-Path ([IO.Path]::GetTempPath()) ('eshop-subscription-' + [Guid]::NewGuid().ToString('N'))
try {
    $env:UseOnlyInMemoryDatabase = 'true'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ROLL_FORWARD = 'Major'
    dotnet build src/PublicApi/PublicApi.csproj --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'PublicApi build failed.' }
    dotnet dev-certs https --check --trust
    if ($LASTEXITCODE -ne 0) { throw 'A trusted HTTPS development certificate is required.' }
    $url = "https://localhost:$port"
    $hostProcess = Start-Process dotnet -ArgumentList @('bin/Debug/net8.0/PublicApi.dll', '--urls', $url) `
        -WorkingDirectory (Join-Path $repo 'src/PublicApi') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput ($logBase + '.out.log') -RedirectStandardError ($logBase + '.err.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($hostProcess.HasExited) { throw "PublicApi exited. Inspect $logBase.err.log and $logBase.out.log" }
        try {
            Invoke-WebRequest "$url/swagger/v1/swagger.json" -TimeoutSec 2 | Out-Null
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (!$ready) { throw "PublicApi did not become ready. Inspect $logBase.out.log" }

    $auth = Invoke-RestMethod "$url/api/authenticate" -Method Post -ContentType 'application/json' `
        -Body (@{ username = $Username; password = $Password } | ConvertTo-Json -Compress)
    if (!$auth.result -or !$auth.token) { throw 'JWT authentication failed.' }
    $headers = @{ Authorization = "Bearer $($auth.token)" }
    $plans = Invoke-RestMethod "$url/api/subscription-plans" -Headers $headers
    if ($plans.isPartial -or !$plans.items.Count) { throw 'The complete plan catalog is unavailable.' }
    $plans.items | Format-Table handle, name, priceInCents, interval, intervalUnit
    if (!$ProductHandle) { $ProductHandle = ($plans.items | Sort-Object priceInCents -Descending | Select-Object -First 1).handle }
    if ($ProductHandle -notin $plans.items.handle) { throw 'ProductHandle must be one of the plans listed above.' }

    # Start two requests before waiting for either, exercising double-click protection.
    $http = [System.Net.Http.HttpClient]::new()
    $http.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $auth.token)
    $json = @{ productHandle = $ProductHandle } | ConvertTo-Json -Compress
    $first = $http.PostAsync("$url/api/subscriptions", [System.Net.Http.StringContent]::new($json, [Text.Encoding]::UTF8, 'application/json'))
    $second = $http.PostAsync("$url/api/subscriptions", [System.Net.Http.StringContent]::new($json, [Text.Encoding]::UTF8, 'application/json'))
    foreach ($task in @($first, $second)) {
        $response = $task.GetAwaiter().GetResult()
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (!$response.IsSuccessStatusCode) { throw "Enrollment returned $([int]$response.StatusCode): $body" }
        Write-Output "Enrollment HTTP $([int]$response.StatusCode): $body"
        $response.Dispose()
    }
    $mine = $null
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $mine = Invoke-RestMethod "$url/api/my-subscriptions" -Headers $headers
        $matching = @($mine.subscriptions.items | Where-Object planHandle -eq $ProductHandle)
        if ($matching.Count -gt 0) { break }
        Start-Sleep -Seconds 1
    }
    if ($matching.Count -ne 1) { throw 'Expected exactly one subscription for the chosen plan.' }
    if (!$matching[0].nextBillingDate) { throw 'Expected a next billing date for this recurring sandbox subscription.' }
    $repeat = Invoke-WebRequest "$url/api/subscriptions" -Method Post -Headers $headers -ContentType 'application/json' -Body $json
    if ($repeat.StatusCode -ne 200) { throw 'Repeat enrollment should return HTTP 200 with the existing subscription.' }
    $mine | ConvertTo-Json -Depth 6
    Write-Output "Verified JWT authentication, plans, concurrent enrollment, account details, and repeat enrollment. Logs: $logBase.out.log"
} finally {
    if ($http) { $http.Dispose() }
    if ($hostProcess -and !$hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id -Force }
    foreach ($name in $oldEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $oldEnvironment[$name]) }
    Pop-Location
}
