# Requires PowerShell 7. Uses only the local PublicApi endpoints; the application uses the plugin SDK.
param([string]$ProductHandle = 'eshop-pro')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$env:APP_PORT_BLOCK_BASE -or [int]$env:APP_PORT_BLOCK_SIZE -lt 5) {
    throw 'APP_PORT_BLOCK_BASE and APP_PORT_BLOCK_SIZE (at least 5) must identify the assigned ports.'
}
$httpsPort = [int]$env:APP_PORT_BLOCK_BASE + 3
$httpPort = [int]$env:APP_PORT_BLOCK_BASE + 4
$url = "https://localhost:$httpsPort"
foreach ($port in @($httpsPort, $httpPort)) {
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
        throw "Port $port is already in use. Stop the previous host before verifying."
    }
}
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:UseOnlyInMemoryDatabase = 'true'
dotnet build "$repo/src/PublicApi/PublicApi.csproj" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$logPrefix = Join-Path ([IO.Path]::GetTempPath()) ("eshop-subscriptions-" + [guid]::NewGuid().ToString('N'))
$hostProcess = $null
try {
    $dll = Join-Path $repo 'src/PublicApi/bin/Debug/net8.0/PublicApi.dll'
    $hostProcess = Start-Process dotnet -ArgumentList @(
        ('"' + $dll + '"'), '--urls', "$url;http://localhost:$httpPort", '--contentRoot',
        ('"' + (Join-Path $repo 'src/PublicApi') + '"')
    ) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$logPrefix.log" -RedirectStandardError "$logPrefix.err"
    $ready = $false
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($hostProcess.HasExited) { throw "PublicApi exited; inspect $logPrefix.log and $logPrefix.err" }
        try {
            $null = Invoke-WebRequest "$url/swagger/index.html" -SkipCertificateCheck -TimeoutSec 2
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$ready) { throw 'PublicApi did not become ready.' }
    $login = Invoke-RestMethod "$url/api/authenticate" -Method Post -ContentType application/json -Body (
        @{ username = 'demouser@microsoft.com'; password = 'Pass@word1' } | ConvertTo-Json
    ) -SkipCertificateCheck
    if (!$login.token) { throw 'Authentication did not return a JWT.' }
    $headers = @{ Authorization = "Bearer $($login.token)" }
    $plans = Invoke-RestMethod "$url/api/subscription-plans" -Headers $headers -SkipCertificateCheck
    $plans.plans | Format-Table handle, priceInCents, interval, intervalUnit
    if ($plans.isTruncated -or !($plans.plans | Where-Object handle -eq $ProductHandle)) {
        throw 'The requested plan is not in the complete offered catalog. Pass -ProductHandle with a returned handle.'
    }
    # Concurrent requests exercise the server's pre-write primary-key claims.
    $requestBody = @{ productHandle = $ProductHandle } | ConvertTo-Json -Compress
    $responses = 1..8 | ForEach-Object -Parallel {
        Invoke-RestMethod "$using:url/api/subscriptions" -Method Post -Headers $using:headers -ContentType application/json -Body $using:requestBody -SkipCertificateCheck
    } -ThrottleLimit 8
    $confirmed = $null
    for ($attempt = 0; $attempt -lt 15; $attempt++) {
        $result = Invoke-RestMethod "$url/api/subscriptions" -Method Post -Headers $headers -ContentType application/json -Body $requestBody -SkipCertificateCheck
        if (!$result.isPending) { $confirmed = $result.subscription; break }
        Start-Sleep -Seconds 1
    }
    if (!$confirmed) { throw 'The provider outcome remains pending; background reconciliation retains the request.' }
    $ids = @($responses | Where-Object { !$_.isPending } | ForEach-Object { $_.subscription.id }) + @($confirmed.id)
    if (@($ids | Select-Object -Unique).Count -ne 1) { throw 'Duplicate subscription IDs returned.' }
    $mine = Invoke-RestMethod "$url/api/my-subscriptions" -Headers $headers -SkipCertificateCheck
    if (@($mine.subscriptions | Where-Object planHandle -eq $ProductHandle).Count -ne 1) {
        throw 'The account did not contain exactly one subscription for the selected plan.'
    }
    $anonymous = Invoke-WebRequest "$url/api/my-subscriptions" -SkipCertificateCheck -SkipHttpErrorCheck
    if ($anonymous.StatusCode -ne 401) { throw 'Anonymous request was not rejected.' }
    $confirmed | Format-List
    Write-Output 'PASS: browse, concurrent subscribe, repeat, account, and JWT enforcement.'
} finally {
    if ($hostProcess -and !$hostProcess.HasExited) { Stop-Process -Id $hostProcess.Id }
    Write-Output "PublicApi stopped. Logs: $logPrefix.log and $logPrefix.err"
}
