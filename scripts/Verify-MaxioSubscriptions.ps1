param([string]$ProductHandle, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$api = Join-Path $repo 'src/PublicApi'
if (!$env:APP_PORT_BLOCK_BASE -or [int]$env:APP_PORT_BLOCK_SIZE -lt 2) {
    throw 'APP_PORT_BLOCK_BASE and APP_PORT_BLOCK_SIZE (at least 2) are required.'
}
$httpsPort = [int]$env:APP_PORT_BLOCK_BASE + 2
$httpPort = [int]$env:APP_PORT_BLOCK_BASE + 3
if ([int]$env:APP_PORT_BLOCK_SIZE -lt 4) {
    $httpsPort = [int]$env:APP_PORT_BLOCK_BASE
    $httpPort = $httpsPort + 1
}
$env:DOTNET_ROLL_FORWARD = 'Major'
if (!$SkipBuild) {
    & dotnet build (Join-Path $api 'PublicApi.csproj') -m:1 -v:q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:UseOnlyInMemoryDatabase = 'true'
$env:ASPNETCORE_URLS = "https://localhost:$httpsPort;http://localhost:$httpPort"
$log = Join-Path ([IO.Path]::GetTempPath()) ('eshop-billing-' + [Guid]::NewGuid().ToString('N'))
$hostProcess = Start-Process -FilePath 'dotnet' -ArgumentList @('bin/Debug/net8.0/PublicApi.dll') `
    -WorkingDirectory $api -WindowStyle Hidden -PassThru -RedirectStandardOutput "$log.out" -RedirectStandardError "$log.err"
try {
    $base = "https://localhost:$httpsPort"
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($hostProcess.HasExited) { throw "PublicApi exited. Logs: $log.out and $log.err" }
        try {
            $response = Invoke-WebRequest "$base/api/subscription-plans" -SkipCertificateCheck -SkipHttpErrorCheck -TimeoutSec 2
            if ($response.StatusCode -eq 401) { $ready = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    if (!$ready) { throw "PublicApi did not become ready. Logs: $log.out and $log.err" }
    # Existing eShopOnWeb development seed account; this is not a Maxio credential.
    $auth = Invoke-RestMethod "$base/api/authenticate" -Method Post -ContentType 'application/json' `
        -Body (@{ username = 'demouser@microsoft.com'; password = 'Pass@word1' } | ConvertTo-Json) -SkipCertificateCheck
    if (!$auth.token) { throw 'JWT authentication failed.' }
    $headers = @{ Authorization = 'Bearer ' + $auth.token }
    $plans = Invoke-RestMethod "$base/api/subscription-plans" -Headers $headers -SkipCertificateCheck
    if (!$plans.Count) { throw 'No plans were returned.' }
    if (!$ProductHandle) { $ProductHandle = ($plans | Sort-Object priceInCents -Descending | Select-Object -First 1).handle }
    if ($ProductHandle -notin $plans.handle) { throw 'Requested product is not offered.' }
    $body = @{ productHandle = $ProductHandle } | ConvertTo-Json
    $result = Invoke-RestMethod "$base/api/subscriptions" -Method Post -ContentType 'application/json' -Body $body -Headers $headers -SkipCertificateCheck
    $repeat = Invoke-RestMethod "$base/api/subscriptions" -Method Post -ContentType 'application/json' -Body $body -Headers $headers -SkipCertificateCheck
    if ($result.status -ne 'complete' -or $repeat.subscription.id -ne $result.subscription.id) { throw 'Enrollment or idempotent replay did not complete.' }
    $account = Invoke-RestMethod "$base/api/my-subscriptions" -Headers $headers -SkipCertificateCheck
    if ($result.subscription.id -notin $account.subscriptions.id) { throw 'Subscription is missing from account.' }
    $admin = Invoke-RestMethod "$base/api/authenticate" -Method Post -ContentType 'application/json' `
        -Body (@{ username = 'admin@microsoft.com'; password = 'Pass@word1' } | ConvertTo-Json) -SkipCertificateCheck
    $other = Invoke-RestMethod "$base/api/my-subscriptions" -Headers @{ Authorization = 'Bearer ' + $admin.token } -SkipCertificateCheck
    if ($result.subscription.id -in $other.subscriptions.id) { throw 'Cross-account subscription leak.' }
    [pscustomobject]@{ Plans = $plans.Count; Subscription = $result.subscription; ReplayMatches = $true; AccountMatches = $true; OwnershipVerified = $true } | ConvertTo-Json -Depth 5
    Write-Host "Verification passed. PublicApi logs: $log.out and $log.err"
}
finally {
    if (!$hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id }
}
