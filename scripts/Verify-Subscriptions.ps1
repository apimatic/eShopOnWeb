# Requires PowerShell 7. Starts a sandbox API, verifies it, and stops only that process.
[CmdletBinding()]
param(
    [string]$ProductHandle,
    [string]$AlternateProductHandle,
    [string]$Username = 'demouser@microsoft.com',
    [string]$Password = 'Pass@word1'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src/PublicApi/PublicApi.csproj'
$workDir = Split-Path $project -Parent
$logRoot = Join-Path ([IO.Path]::GetTempPath()) ('eshop-billing-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($logRoot) | Out-Null
$service = $null
$savedEnvironment = @{}
foreach ($name in @('DOTNET_ROLL_FORWARD', 'ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'UseOnlyInMemoryDatabase')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $settings = @{}
    $sources = @{ 'Maxio:ApiKey' = 'MAXIO_API_KEY'; 'Maxio:Subdomain' = 'MAXIO_SITE_SUBDOMAIN';
        'Maxio:ProductFamilyHandle' = 'MAXIO_DEFAULT_PRODUCT_FAMILY'; 'Maxio:Environment' = 'MAXIO_ENVIRONMENT' }
    foreach ($entry in $sources.GetEnumerator()) {
        $value = [Environment]::GetEnvironmentVariable($entry.Value, 'Process')
        if (![string]::IsNullOrWhiteSpace($value)) { $settings[$entry.Key] = $value }
    }
    if ($settings.Count) {
        $settings | ConvertTo-Json -Compress | dotnet user-secrets set --project $project
        if ($LASTEXITCODE) { throw 'Could not load Maxio user-secrets.' }
    }
    if (!$env:APP_PORT_BLOCK_BASE -or !$env:APP_PORT_BLOCK_SIZE) { throw 'Assigned APP_PORT_BLOCK_BASE and APP_PORT_BLOCK_SIZE are required.' }
    $offset = [Math]::Min(3, [int]$env:APP_PORT_BLOCK_SIZE - 1)
    if ($offset -lt 0) { throw 'The assigned port block is empty.' }
    $port = [int]$env:APP_PORT_BLOCK_BASE + $offset
    $base = "https://localhost:$port"
    $probe = [Net.Sockets.TcpClient]::new()
    try { $probe.Connect('localhost', $port); throw "Assigned port $port is in use. Stop the previous API instance first." }
    catch [Net.Sockets.SocketException] { }
    finally { $probe.Dispose() }
    $env:DOTNET_ROLL_FORWARD = 'Major'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:UseOnlyInMemoryDatabase = 'true'
    $env:ASPNETCORE_URLS = $base
    & dotnet build $project --nologo --verbosity quiet *> (Join-Path $logRoot 'build.log')
    if ($LASTEXITCODE) { throw "Build failed. See $logRoot/build.log" }
    $dll = Join-Path $workDir 'bin/Debug/net8.0/PublicApi.dll'
    $start = @{ FilePath = 'dotnet'; ArgumentList = ('"' + $dll + '"'); WorkingDirectory = $workDir;
        RedirectStandardOutput = (Join-Path $logRoot 'api.log'); RedirectStandardError = (Join-Path $logRoot 'api-errors.log'); PassThru = $true }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    $service = Start-Process @start
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($service.HasExited) { throw "API startup failed. See $logRoot" }
        try {
            $response = Invoke-WebRequest "$base/api/subscription-plans" -SkipHttpErrorCheck
            if ($response.StatusCode -eq 401) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (!$ready) { throw "API was not ready within 30 seconds. See $logRoot" }
    $auth = Invoke-RestMethod "$base/api/authenticate" -Method Post -ContentType application/json -Body (@{ username = $Username; password = $Password } | ConvertTo-Json)
    if (!$auth.result -or !$auth.token) { throw 'Demo authentication failed.' }
    $headers = @{ Authorization = "Bearer $($auth.token)" }
    $plans = @(Invoke-RestMethod "$base/api/subscription-plans" -Headers $headers)
    if (!$plans.Count) { throw 'No recurring plans were offered by the configured family.' }
    if (!$ProductHandle) { $ProductHandle = ($plans | Sort-Object PriceInCents -Descending | Select-Object -First 1).handle }
    if ($ProductHandle -notin $plans.handle) { throw 'Requested product handle is not in the offered plans.' }
    $body = @{ productHandle = $ProductHandle; firstName = 'Demo'; lastName = 'Shopper' } | ConvertTo-Json
    $enrollment = Invoke-RestMethod "$base/api/subscriptions" -Method Post -ContentType application/json -Headers $headers -Body $body
    for ($attempt = 0; $enrollment.status -eq 'pending' -and $attempt -lt 10; $attempt++) {
        Start-Sleep -Milliseconds 500
        $enrollment = Invoke-RestMethod "$base/api/subscriptions" -Method Post -ContentType application/json -Headers $headers -Body $body
    }
    if ($enrollment.status -ne 'confirmed') { throw 'Enrollment is still awaiting provider reconciliation.' }
    $duplicate = Invoke-RestMethod "$base/api/subscriptions" -Method Post -ContentType application/json -Headers $headers -Body $body
    if ($duplicate.subscription.id -ne $enrollment.subscription.id) { throw 'Duplicate POST did not return the same subscription.' }
    if ($AlternateProductHandle) {
        $alternateBody = @{ productHandle = $AlternateProductHandle; firstName = 'Demo'; lastName = 'Shopper' } | ConvertTo-Json
        $alternate = Invoke-RestMethod "$base/api/subscriptions" -Method Post -ContentType application/json -Headers $headers -Body $alternateBody
        if ($alternate.status -ne 'confirmed') { throw 'Alternate enrollment was not confirmed.' }
    }
    $mine = Invoke-RestMethod "$base/api/my-subscriptions" -Headers $headers
    if (@($mine.subscriptions | Where-Object id -eq $enrollment.subscription.id).Count -ne 1) { throw 'Account did not show exactly one matching subscription.' }
    $plans | Format-Table handle, priceInCents, interval, intervalUnit
    $mine.subscriptions | Format-Table id, productHandle, priceInCents, currency, state, nextBillingDate
    Write-Host "PASS: JWT authentication, configured-family plans, enrollment, duplicate protection, and account read. Logs: $logRoot"
}
finally {
    if ($service -and !$service.HasExited) { Stop-Process -Id $service.Id -Force }
    foreach ($entry in $savedEnvironment.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process') }
}
