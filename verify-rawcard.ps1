# Patiently verify the one-off raw-card path: fresh order each attempt (avoids PayPal's
# per-order payment-attempts cap), then fulfil + refund it once an authorization lands.
param([string]$BaseUrl = "http://localhost:19664", [int]$MaxAttempts = 90)
$ErrorActionPreference = 'Stop'

function Invoke-Api {
    param([string]$Method, [string]$Path, [object]$Body = $null, [string]$Token = $null)
    $args = @('-s', '-o', '-', '-w', "`n%{http_code}", '-X', $Method, "$BaseUrl$Path")
    $tmp = $null
    if ($null -ne $Body) {
        $tmp = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($tmp, ($Body | ConvertTo-Json -Depth 10 -Compress))
        $args += @('-H', 'Content-Type: application/json', '-d', "@$tmp")
    }
    if ($Token) { $args += @('-H', "Authorization: Bearer $Token") }
    try { $raw = & curl.exe @args } finally { if ($tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue } }
    $lines = $raw -split "`n"
    return @{ Status = [int]$lines[-1].Trim(); Body = ($lines[0..($lines.Count - 2)] -join "`n").Trim() }
}

$card = @{ number = '4111111111111111'; expiry = '2030-12'; securityCode = '123'; name = 'Test Shopper'
          billingAddress = @{ addressLine1 = '1 Main St'; city = 'San Jose'; state = 'CA'; postalCode = '95131'; countryCode = 'US' } }

$shopper = ((Invoke-Api -Method POST -Path '/api/authenticate' -Body @{ username = 'demouser@microsoft.com'; password = 'Pass@word1' }).Body | ConvertFrom-Json).token
$admin = ((Invoke-Api -Method POST -Path '/api/authenticate' -Body @{ username = 'admin@microsoft.com'; password = 'Pass@word1' }).Body | ConvertFrom-Json).token

for ($i = 1; $i -le $MaxAttempts; $i++) {
    $r = Invoke-Api -Method POST -Path '/api/orders' -Token $shopper -Body @{
        items = @(@{ catalogItemId = 3; quantity = 1 })
        shipToAddress = @{ street = '1 Main St'; city = 'Seattle'; state = 'WA'; country = 'US'; zipCode = '98101' } }
    $oid = ($r.Body | ConvertFrom-Json).orderId
    $p = Invoke-Api -Method POST -Path "/api/orders/$oid/pay" -Token $shopper -Body @{ card = $card }
    Write-Host "attempt $i (order $oid): pay -> $($p.Status)"
    if ($p.Status -eq 200) {
        $pay = $p.Body | ConvertFrom-Json
        Write-Host "RAW-CARD AUTHORIZED: authId=$($pay.authorizationId) amount=$($pay.authorizedAmount) $($pay.currency)"
        $f = Invoke-Api -Method POST -Path "/api/orders/$oid/fulfil" -Token $admin
        $fo = $f.Body | ConvertFrom-Json
        Write-Host "fulfil -> $($f.Status) captured=$($fo.capturedAmount) fee=$($fo.paypalFee) net=$($fo.netAmount) captureId=$($fo.captureId)"
        $key = "raw-$([Guid]::NewGuid().ToString('N'))"
        $rf = Invoke-Api -Method POST -Path "/api/orders/$oid/refunds" -Token $shopper -Body @{ amount = 1.00; idempotencyKey = $key }
        Write-Host "refund -> $($rf.Status) $($rf.Body)"
        if ($f.Status -eq 200 -and $fo.paymentStatus -eq 'Captured' -and $rf.Status -eq 200) {
            Write-Host 'RAW-CARD FLOW PASSED' -ForegroundColor Green
            exit 0
        }
        Write-Host 'RAW-CARD FLOW FAILED at fulfil/refund' -ForegroundColor Red
        exit 1
    }
    Start-Sleep -Seconds 60
}
Write-Host 'RAW-CARD FLOW NOT VERIFIED: sandbox kept refusing' -ForegroundColor Yellow
exit 2
