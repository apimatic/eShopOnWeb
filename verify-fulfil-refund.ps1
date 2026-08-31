# Verify fulfil(capture)+refund using a saved card (raw-card path is sandbox-risk-filtered right now).
param([string]$BaseUrl = "http://localhost:19664")
$ErrorActionPreference = 'Stop'
$script:Failed = 0

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
function Check { param([string]$Name, [bool]$Ok, [string]$Detail = '')
    if ($Ok) { Write-Host "PASS: $Name" -ForegroundColor Green } else { Write-Host "FAIL: $Name  $Detail" -ForegroundColor Red; $script:Failed++ } }

$card = @{ number = '4111111111111111'; expiry = '2030-12'; securityCode = '123'; name = 'Test Shopper'
          billingAddress = @{ addressLine1 = '1 Main St'; city = 'San Jose'; state = 'CA'; postalCode = '95131'; countryCode = 'US' } }

$shopper = ((Invoke-Api -Method POST -Path '/api/authenticate' -Body @{ username = 'demouser@microsoft.com'; password = 'Pass@word1' }).Body | ConvertFrom-Json).token
$admin = ((Invoke-Api -Method POST -Path '/api/authenticate' -Body @{ username = 'admin@microsoft.com'; password = 'Pass@word1' }).Body | ConvertFrom-Json).token

# Save a fresh card
$r = Invoke-Api -Method POST -Path '/api/payment-methods' -Token $shopper -Body @{ card = $card }
$pmId = ($r.Body | ConvertFrom-Json).paymentMethodId
Check 'save card' ($null -ne $pmId) "$($r.Status) $($r.Body)"

# Order A: pay with saved card, fulfil, refund
$r = Invoke-Api -Method POST -Path '/api/orders' -Token $shopper -Body @{
    items = @(@{ catalogItemId = 1; quantity = 2 })
    shipToAddress = @{ street = '1 Main St'; city = 'Seattle'; state = 'WA'; country = 'US'; zipCode = '98101' } }
$orderA = ($r.Body | ConvertFrom-Json).orderId
Write-Host "order A = $orderA (total 39.00)"

$payA = $null
for ($i = 1; $i -le 20; $i++) {
    $r = Invoke-Api -Method POST -Path "/api/orders/$orderA/pay" -Token $shopper -Body @{ savedCardId = $pmId }
    if ($r.Status -eq 200) { $payA = $r.Body | ConvertFrom-Json; break }
    Write-Host "payA attempt $i -> $($r.Status)"
    Start-Sleep -Seconds 30
}
Check 'order A authorized via saved card' ($null -ne $payA -and $payA.paymentStatus -eq 'Authorized') "$($r.Status) $($r.Body)"

# Double-click: paying again must not create a second hold
$r = Invoke-Api -Method POST -Path "/api/orders/$orderA/pay" -Token $shopper -Body @{ savedCardId = $pmId }
$payAgain = $r.Body | ConvertFrom-Json
Check 're-pay is idempotent (same authorization)' ($r.Status -eq 200 -and $payAgain.authorizationId -eq $payA.authorizationId) "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method POST -Path "/api/orders/$orderA/fulfil" -Token $admin
$fulfil = $r.Body | ConvertFrom-Json
Check 'fulfil captures with fee+net' ($r.Status -eq 200 -and $fulfil.paymentStatus -eq 'Captured' -and $fulfil.capturedAmount -eq 39.00 -and $fulfil.paypalFee -gt 0 -and $fulfil.netAmount -eq ($fulfil.capturedAmount - $fulfil.paypalFee)) "$($r.Status) $($r.Body)"
Write-Host ("  captured={0} fee={1} net={2} captureId={3}" -f $fulfil.capturedAmount, $fulfil.paypalFee, $fulfil.netAmount, $fulfil.captureId)

# Double-click fulfil must not capture twice
$r = Invoke-Api -Method POST -Path "/api/orders/$orderA/fulfil" -Token $admin
$fulfil2 = $r.Body | ConvertFrom-Json
Check 're-fulfil is idempotent (same capture)' ($r.Status -eq 200 -and $fulfil2.captureId -eq $fulfil.captureId) "$($r.Status) $($r.Body)"

$key = "e2e-$([Guid]::NewGuid().ToString('N'))"
$r = Invoke-Api -Method POST -Path "/api/orders/$orderA/refunds" -Token $shopper -Body @{ amount = 5.00; idempotencyKey = $key }
$ref1 = $r.Body | ConvertFrom-Json
Check 'partial refund returns refundId' ($r.Status -eq 200 -and $ref1.refundId -and $ref1.amount -eq 5.00) "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method POST -Path "/api/orders/$orderA/refunds" -Token $shopper -Body @{ amount = 5.00; idempotencyKey = $key }
$ref2 = $r.Body | ConvertFrom-Json
Check 'same idempotency key does not refund twice' ($r.Status -eq 200 -and $ref2.refundId -eq $ref1.refundId) "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method POST -Path "/api/orders/$orderA/refunds" -Token $shopper -Body @{ amount = 2.00; idempotencyKey = "e2e-$([Guid]::NewGuid().ToString('N'))" }
Check 'second distinct partial refund goes through' ($r.Status -eq 200) "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method POST -Path "/api/orders/$orderA/refunds" -Token $shopper -Body @{ amount = 33.00; idempotencyKey = "e2e-$([Guid]::NewGuid().ToString('N'))" }
Check 'refund beyond captured remainder is rejected (409)' ($r.Status -eq 409) "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method GET -Path '/api/my-orders' -Token $shopper
$mine = ($r.Body | ConvertFrom-Json).orders | Where-Object { $_.orderId -eq $orderA }
Check 'order A shows PartiallyRefunded with 2 refunds' ($mine.payment.status -eq 'PartiallyRefunded' -and $mine.payment.refunds.Count -eq 2) "$($r.Body)"

$r = Invoke-Api -Method POST -Path "/api/orders/$orderA/refunds" -Token $shopper -Body @{ idempotencyKey = "e2e-$([Guid]::NewGuid().ToString('N'))" }
$refFull = $r.Body | ConvertFrom-Json
Check 'full remainder refund (amount omitted)' ($r.Status -eq 200 -and $refFull.amount -eq 32.00) "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method GET -Path '/api/my-orders' -Token $shopper
$mine = ($r.Body | ConvertFrom-Json).orders | Where-Object { $_.orderId -eq $orderA }
Check 'order A now Refunded' ($mine.payment.status -eq 'Refunded') "$($r.Body)"

# Cross-shopper isolation: admin token (different user) must not see shopper's order via my-orders
$r = Invoke-Api -Method GET -Path '/api/my-orders' -Token $admin
$adminOrders = ($r.Body | ConvertFrom-Json).orders
Check 'admin my-orders does not include shopper orders' (-not ($adminOrders | Where-Object { $_.orderId -eq $orderA })) "$($r.Body)"

Write-Host ''
if ($script:Failed -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green } else { Write-Host "$($script:Failed) CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
