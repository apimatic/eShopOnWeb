# End-to-end verification of the PayPal integration against the sandbox.
# Requires PublicApi running on http://localhost:19664 with UseOnlyInMemoryDatabase=true.
param(
    [string]$BaseUrl = "http://localhost:19664"
)

$ErrorActionPreference = 'Stop'
$script:Failed = 0

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$Token = $null
    )
    $args = @('-s', '-o', '-', '-w', "`n%{http_code}", '-X', $Method, "$BaseUrl$Path")
    $tmp = $null
    if ($null -ne $Body) {
        $tmp = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($tmp, ($Body | ConvertTo-Json -Depth 10 -Compress))
        $args += @('-H', 'Content-Type: application/json', '-d', "@$tmp")
    }
    if ($Token) { $args += @('-H', "Authorization: Bearer $Token") }
    try {
        $raw = & curl.exe @args
    } finally {
        if ($tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
    }
    $lines = $raw -split "`n"
    $status = [int]$lines[-1].Trim()
    $json = ($lines[0..($lines.Count - 2)] -join "`n").Trim()
    return @{ Status = $status; Body = $json }
}

function Check {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    if ($Ok) { Write-Host "PASS: $Name" -ForegroundColor Green }
    else { Write-Host "FAIL: $Name  $Detail" -ForegroundColor Red; $script:Failed++ }
}

function Get-Token {
    param([string]$User, [string]$Pass)
    $r = Invoke-Api -Method POST -Path '/api/authenticate' -Body @{ username = $User; password = $Pass }
    if ($r.Status -ne 200) { throw "authenticate failed for $User : $($r.Status) $($r.Body)" }
    return ($r.Body | ConvertFrom-Json).token
}

$card = @{
    number = '4111111111111111'
    expiry = '2030-12'
    securityCode = '123'
    name = 'Test Shopper'
    billingAddress = @{ addressLine1 = '1 Main St'; city = 'San Jose'; state = 'CA'; postalCode = '95131'; countryCode = 'US' }
}

Write-Host '== Authenticating =='
$shopper = Get-Token -User 'demouser@microsoft.com' -Pass 'Pass@word1'
$admin = Get-Token -User 'admin@microsoft.com' -Pass 'Pass@word1'
Write-Host 'tokens acquired'

Write-Host '== Flow 1: order, pay (raw card), fulfil, refund =='
$r = Invoke-Api -Method POST -Path '/api/orders' -Token $shopper -Body @{
    items = @(@{ catalogItemId = 1; quantity = 2 })
    shipToAddress = @{ street = '1 Main St'; city = 'Seattle'; state = 'WA'; country = 'US'; zipCode = '98101' }
}
Check 'POST /api/orders returns orderId' (($r.Status -eq 200 -or $r.Status -eq 201) -and ($r.Body | ConvertFrom-Json).orderId) "$($r.Status) $($r.Body)"
$orderId = ($r.Body | ConvertFrom-Json).orderId

# Sandbox risk filters occasionally refuse card authorizations; retry patiently.
$payResp = $null
for ($i = 1; $i -le 40; $i++) {
    $r = Invoke-Api -Method POST -Path "/api/orders/$orderId/pay" -Token $shopper -Body @{ card = $card }
    if ($r.Status -eq 200) { $payResp = $r.Body | ConvertFrom-Json; break }
    Write-Host "pay attempt $i -> $($r.Status)"
    Start-Sleep -Seconds 30
}
Check 'POST /api/orders/{id}/pay authorizes' ($null -ne $payResp -and $payResp.paymentStatus -eq 'Authorized') "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method GET -Path '/api/my-orders' -Token $shopper
$myOrders = ($r.Body | ConvertFrom-Json).orders
$mine = $myOrders | Where-Object { $_.orderId -eq $orderId }
Check 'GET /api/my-orders shows Authorized' ($r.Status -eq 200 -and $mine.status -eq 'Authorized' -and $mine.payment.status -eq 'Authorized') "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method POST -Path "/api/orders/$orderId/fulfil" -Token $admin
$fulfil = $r.Body | ConvertFrom-Json
Check 'POST /api/orders/{id}/fulfil captures with fee+net' ($r.Status -eq 200 -and $fulfil.paymentStatus -eq 'Captured' -and $fulfil.capturedAmount -gt 0 -and $null -ne $fulfil.paypalFee -and $null -ne $fulfil.netAmount) "$($r.Status) $($r.Body)"
Write-Host ("  captured={0} fee={1} net={2}" -f $fulfil.capturedAmount, $fulfil.paypalFee, $fulfil.netAmount)

$key = "e2e-$([Guid]::NewGuid().ToString('N'))"
$r = Invoke-Api -Method POST -Path "/api/orders/$orderId/refunds" -Token $shopper -Body @{ amount = 1.00; idempotencyKey = $key }
$refund = $r.Body | ConvertFrom-Json
Check 'POST refunds (partial) returns refundId' ($r.Status -eq 200 -and $refund.refundId) "$($r.Status) $($r.Body)"

$r2 = Invoke-Api -Method POST -Path "/api/orders/$orderId/refunds" -Token $shopper -Body @{ amount = 1.00; idempotencyKey = $key }
$refund2 = $r2.Body | ConvertFrom-Json
Check 'same idempotency key does not refund twice' ($r2.Status -eq 200 -and $refund2.refundId -eq $refund.refundId) "$($r2.Status) $($r2.Body)"

$r = Invoke-Api -Method GET -Path '/api/my-orders' -Token $shopper
$mine = ($r.Body | ConvertFrom-Json).orders | Where-Object { $_.orderId -eq $orderId }
Check 'order shows PartiallyRefunded' ($mine.payment.status -eq 'PartiallyRefunded') "$($r.Body)"

Write-Host '== Flow 2: saved card =='
$r = Invoke-Api -Method POST -Path '/api/payment-methods' -Token $shopper -Body @{ card = $card }
$pm = $r.Body | ConvertFrom-Json
Check 'POST /api/payment-methods returns paymentMethodId + safe display' (($r.Status -eq 200 -or $r.Status -eq 201) -and $pm.paymentMethodId -and $pm.lastDigits -eq '1111') "$($r.Status) $($r.Body)"
$pmId = $pm.paymentMethodId

$r = Invoke-Api -Method GET -Path '/api/payment-methods' -Token $shopper
Check 'GET /api/payment-methods lists saved card' ((($r.Body | ConvertFrom-Json).paymentMethods).paymentMethodId -contains $pmId) "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method POST -Path '/api/orders' -Token $shopper -Body @{
    items = @(@{ catalogItemId = 2; quantity = 1 })
    shipToAddress = @{ street = '1 Main St'; city = 'Seattle'; state = 'WA'; country = 'US'; zipCode = '98101' }
}
$orderId2 = ($r.Body | ConvertFrom-Json).orderId
Check 'second order placed' ($null -ne $orderId2) "$($r.Status) $($r.Body)"

$payResp2 = $null
for ($i = 1; $i -le 40; $i++) {
    $r = Invoke-Api -Method POST -Path "/api/orders/$orderId2/pay" -Token $shopper -Body @{ savedCardId = $pmId }
    if ($r.Status -eq 200) { $payResp2 = $r.Body | ConvertFrom-Json; break }
    Write-Host "pay(saved) attempt $i -> $($r.Status)"
    Start-Sleep -Seconds 30
}
Check 'saved card pays second order' ($null -ne $payResp2 -and $payResp2.paymentStatus -eq 'Authorized') "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method POST -Path "/api/orders/$orderId2/cancel" -Token $admin
Check 'cancel releases the hold' ($r.Status -eq 200 -and ($r.Body | ConvertFrom-Json).status -eq 'Cancelled') "$($r.Status) $($r.Body)"

$r = Invoke-Api -Method DELETE -Path "/api/payment-methods/$pmId" -Token $shopper
Check 'DELETE /api/payment-methods/{id}' ($r.Status -eq 200 -or $r.Status -eq 204) "$($r.Status) $($r.Body)"
$r = Invoke-Api -Method GET -Path '/api/payment-methods' -Token $shopper
Check 'deleted card no longer listed' (-not ((($r.Body | ConvertFrom-Json).paymentMethods).paymentMethodId -contains $pmId)) "$($r.Body)"

Write-Host '== Authorization boundaries =='
$r = Invoke-Api -Method POST -Path "/api/orders/$orderId/fulfil" -Token $shopper
Check 'shopper cannot fulfil (403)' ($r.Status -eq 403) "$($r.Status)"
$r = Invoke-Api -Method GET -Path '/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T23:59:59Z' -Token $shopper
Check 'shopper cannot reconcile (403)' ($r.Status -eq 403) "$($r.Status)"

Write-Host '== Reconciliation (admin) =='
$from = (Get-Date).ToUniversalTime().AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
$to = (Get-Date).ToUniversalTime().AddHours(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
$r = Invoke-Api -Method GET -Path "/api/reconciliation?from=$from&to=$to" -Token $admin
Check 'GET /api/reconciliation returns report' ($r.Status -eq 200) "$($r.Status) $($r.Body)"
Write-Host "  report: $($r.Body)"

Write-Host ''
if ($script:Failed -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green } else { Write-Host "$($script:Failed) CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
