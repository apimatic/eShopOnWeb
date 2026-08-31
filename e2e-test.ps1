# End-to-end verification of the PayPal payment integration against the sandbox.
# Requires PublicApi running on https://localhost:21303 with UseOnlyInMemoryDatabase=true.
param(
    [string]$BaseUrl = "https://localhost:21303"
)

$ErrorActionPreference = "Stop"
$script:failures = 0

function Invoke-Api {
    param([string]$Method, [string]$Path, [string]$Token, $Body)
    $headers = @{}
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }
    $params = @{
        Method = $Method
        Uri = "$BaseUrl$Path"
        Headers = $headers
        SkipCertificateCheck = $true
        TimeoutSec = 60
    }
    if ($null -ne $Body) {
        $params["Body"] = ($Body | ConvertTo-Json -Depth 10)
        $params["ContentType"] = "application/json"
    }
    try {
        $resp = Invoke-RestMethod @params
        return @{ Status = 200; Data = $resp }
    }
    catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        return @{ Status = $code; Data = $null; Error = $_.ErrorDetails.Message }
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Name, [string]$Detail = "")
    if ($Condition) {
        Write-Host "PASS: $Name" -ForegroundColor Green
    } else {
        $script:failures++
        Write-Host "FAIL: $Name $Detail" -ForegroundColor Red
    }
}

$card = @{
    number = "4111111111111111"
    expiry = "2030-11"
    securityCode = "123"
    name = "Test Shopper"
    billingAddress = @{
        street = "1 Main St"; city = "San Jose"; state = "CA"; country = "US"; zipCode = "95131"
    }
}

Write-Host "== Authenticate ==" -ForegroundColor Cyan
$shopperAuth = Invoke-Api -Method Post -Path "/api/authenticate" -Body @{ username = "demouser@microsoft.com"; password = "Pass@word1" }
$adminAuth = Invoke-Api -Method Post -Path "/api/authenticate" -Body @{ username = "admin@microsoft.com"; password = "Pass@word1" }
Assert-True ($null -ne $shopperAuth.Data.token) "shopper token issued"
Assert-True ($null -ne $adminAuth.Data.token) "admin token issued"
$shopper = $shopperAuth.Data.token
$admin = $adminAuth.Data.token

Write-Host "== Catalog prices ==" -ForegroundColor Cyan
$catalog = Invoke-Api -Method Get -Path "/api/catalog-items?pageIndex=0&pageSize=5" -Token $shopper
$i0 = $catalog.Data.catalogItems[0]
$i1 = $catalog.Data.catalogItems[1]
$expectedTotal = [decimal]$i0.price * 2 + [decimal]$i1.price
Write-Host "items: $($i0.id)@$($i0.price) x2 + $($i1.id)@$($i1.price) x1 = $expectedTotal"

Write-Host "== Flow 1: order + pay (raw card) ==" -ForegroundColor Cyan
$order = Invoke-Api -Method Post -Path "/api/orders" -Token $shopper -Body @{
    items = @(
        @{ catalogItemId = $i0.id; quantity = 2 },
        @{ catalogItemId = $i1.id; quantity = 1 }
    )
    shipToAddress = @{ street = "1 Main St"; city = "San Jose"; state = "CA"; country = "US"; zipCode = "95131" }
}
Assert-True ($order.Data.orderId -gt 0) "order created" "status=$($order.Status) err=$($order.Error)"
Assert-True ($order.Data.status -eq "PendingPayment") "order starts PendingPayment" "got $($order.Data.status)"
Assert-True ([decimal]$order.Data.total -eq $expectedTotal) "order total matches catalog" "got $($order.Data.total) expected $expectedTotal"
$orderId = $order.Data.orderId

$pay = Invoke-Api -Method Post -Path "/api/orders/$orderId/pay" -Token $shopper -Body @{ card = $card }
Assert-True ($pay.Status -eq 200) "pay succeeded" "status=$($pay.Status) err=$($pay.Error)"
$p = $pay.Data.payment
Assert-True ($p.status -eq "Authorized") "payment Authorized" "got $($p.status)"
Assert-True ([decimal]$p.amount -eq $expectedTotal) "held amount equals order total to the cent" "got $($p.amount)"
Assert-True (-not [string]::IsNullOrEmpty($p.authorizationId)) "authorization id present"
$authId = $p.authorizationId

$payAgain = Invoke-Api -Method Post -Path "/api/orders/$orderId/pay" -Token $shopper -Body @{ card = $card }
Assert-True (-not [string]::IsNullOrEmpty($payAgain.Data.payment.authorizationId) -and $payAgain.Data.payment.authorizationId -eq $authId) "double-pay is idempotent (same authorization)" "got $($payAgain.Data.payment.authorizationId)"

Write-Host "== Authorization enforcement ==" -ForegroundColor Cyan
$shopperFulfil = Invoke-Api -Method Post -Path "/api/orders/$orderId/fulfil" -Token $shopper
Assert-True ($shopperFulfil.Status -eq 403) "shopper cannot fulfil (403)" "got $($shopperFulfil.Status)"
$anonOrders = Invoke-Api -Method Get -Path "/api/my-orders"
Assert-True ($anonOrders.Status -eq 401) "anonymous rejected (401)" "got $($anonOrders.Status)"

Write-Host "== Fulfil (capture) ==" -ForegroundColor Cyan
$fulfil = Invoke-Api -Method Post -Path "/api/orders/$orderId/fulfil" -Token $admin
Assert-True ($fulfil.Status -eq 200) "fulfil succeeded" "status=$($fulfil.Status) err=$($fulfil.Error)"
$f = $fulfil.Data.payment
Assert-True ($f.status -eq "Captured") "payment Captured" "got $($f.status)"
Assert-True (-not [string]::IsNullOrEmpty($f.captureId)) "capture id present"
Assert-True ([decimal]$f.capturedAmount -eq $expectedTotal) "captured amount equals order total" "got $($f.capturedAmount) expected $expectedTotal"
Assert-True ($null -ne $f.sellerFee) "PayPal fee reported"
Assert-True ($null -ne $f.netAmount) "net proceeds reported"
Write-Host "captured=$($f.capturedAmount) fee=$($f.sellerFee) net=$($f.netAmount)"

Write-Host "== Refunds ==" -ForegroundColor Cyan
$r1 = Invoke-Api -Method Post -Path "/api/orders/$orderId/refunds" -Token $admin -Body @{ amount = 5.00; idempotencyKey = "rf-key-1" }
Assert-True ($r1.Status -eq 200 -and -not [string]::IsNullOrEmpty($r1.Data.refundId)) "partial refund created" "status=$($r1.Status) err=$($r1.Error)"
$refundId1 = $r1.Data.refundId

$r1repeat = Invoke-Api -Method Post -Path "/api/orders/$orderId/refunds" -Token $admin -Body @{ amount = 5.00; idempotencyKey = "rf-key-1" }
Assert-True ($r1repeat.Data.refundId -eq $refundId1) "same idempotency key returns same refund (no double refund)" "got $($r1repeat.Data.refundId)"

$r2 = Invoke-Api -Method Post -Path "/api/orders/$orderId/refunds" -Token $admin -Body @{ amount = 3.00; idempotencyKey = "rf-key-2" }
Assert-True ($r2.Status -eq 200 -and $r2.Data.refundId -ne $refundId1) "second distinct partial refund allowed" "status=$($r2.Status) err=$($r2.Error)"

$over = Invoke-Api -Method Post -Path "/api/orders/$orderId/refunds" -Token $admin -Body @{ amount = 999.00; idempotencyKey = "rf-key-3" }
Assert-True ($over.Status -eq 409) "refund beyond captured remainder rejected (409)" "got $($over.Status)"

$myOrders = Invoke-Api -Method Get -Path "/api/my-orders" -Token $shopper
$mo = $myOrders.Data.orders | Where-Object { $_.orderId -eq $orderId }
Assert-True ($null -ne $mo) "my-orders contains the order"
if ($mo) {
    Assert-True ([decimal]$mo.payment.totalRefunded -eq 8.00) "total refunded = 8.00" "got $($mo.payment.totalRefunded)"
    Assert-True ([decimal]$mo.payment.refundableAmount -eq ($expectedTotal - 8.00)) "refundable remainder correct" "got $($mo.payment.refundableAmount)"
}

Write-Host "== Flow 2: saved cards ==" -ForegroundColor Cyan
$save = Invoke-Api -Method Post -Path "/api/payment-methods" -Token $shopper -Body @{ card = $card }
Assert-True ($save.Data.paymentMethodId -gt 0) "card saved" "status=$($save.Status) err=$($save.Error)"
$pmId = $save.Data.paymentMethodId
Assert-True (-not [string]::IsNullOrEmpty($save.Data.lastDigits) -and $save.Data.lastDigits -ne $card.number) "only safe card descriptor returned"
$saveRaw = $save.Data | ConvertTo-Json -Compress
Assert-True ($saveRaw -notmatch "4111111111111111") "full card number never in response"

$list = Invoke-Api -Method Get -Path "/api/payment-methods" -Token $shopper
Assert-True (($list.Data.paymentMethods | Where-Object { $_.paymentMethodId -eq $pmId }).Count -eq 1) "saved card listed for owner"

$adminList = Invoke-Api -Method Get -Path "/api/payment-methods" -Token $admin
Assert-True (($adminList.Data.paymentMethods | Where-Object { $_.paymentMethodId -eq $pmId }).Count -eq 0) "other user cannot see the saved card"

$order2 = Invoke-Api -Method Post -Path "/api/orders" -Token $shopper -Body @{
    items = @( @{ catalogItemId = $i1.id; quantity = 1 } )
}
$order2Id = $order2.Data.orderId
$pay2 = Invoke-Api -Method Post -Path "/api/orders/$order2Id/pay" -Token $shopper -Body @{ paymentMethodId = $pmId }
Assert-True ($pay2.Status -eq 200 -and $pay2.Data.payment.status -eq "Authorized") "second order paid with saved card" "status=$($pay2.Status) err=$($pay2.Error)"
Assert-True ([decimal]$pay2.Data.payment.amount -eq [decimal]$order2.Data.total) "saved-card hold equals order total"

Write-Host "== Cancel (void) ==" -ForegroundColor Cyan
$cancel = Invoke-Api -Method Post -Path "/api/orders/$order2Id/cancel" -Token $admin
Assert-True ($cancel.Status -eq 200) "cancel succeeded" "status=$($cancel.Status) err=$($cancel.Error)"
Assert-True ($cancel.Data.payment.status -eq "Voided") "held funds released (Voided)" "got $($cancel.Data.payment.status)"

$myOrders2 = Invoke-Api -Method Get -Path "/api/my-orders" -Token $shopper
$mo2 = $myOrders2.Data.orders | Where-Object { $_.orderId -eq $order2Id }
Assert-True ($mo2.status -eq "Cancelled") "order shows Cancelled in my-orders" "got $($mo2.status)"

Write-Host "== Delete saved card ==" -ForegroundColor Cyan
$del = Invoke-Api -Method Delete -Path "/api/payment-methods/$pmId" -Token $shopper
Assert-True ($del.Status -in 200,204) "card deleted" "got $($del.Status)"
$list2 = Invoke-Api -Method Get -Path "/api/payment-methods" -Token $shopper
Assert-True (($list2.Data.paymentMethods | Where-Object { $_.paymentMethodId -eq $pmId }).Count -eq 0) "deleted card no longer listed"
$order3 = Invoke-Api -Method Post -Path "/api/orders" -Token $shopper -Body @{ items = @( @{ catalogItemId = $i1.id; quantity = 1 } ) }
$pay3 = Invoke-Api -Method Post -Path "/api/orders/$($order3.Data.orderId)/pay" -Token $shopper -Body @{ paymentMethodId = $pmId }
Assert-True ($pay3.Status -in 404,409,422) "deleted card cannot pay" "got $($pay3.Status)"

Write-Host "== Reconciliation ==" -ForegroundColor Cyan
$from = (Get-Date).ToUniversalTime().AddDays(-7).ToString("o")
$to = (Get-Date).ToUniversalTime().AddDays(1).ToString("o")
$recon = Invoke-Api -Method Get -Path "/api/reconciliation?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))" -Token $admin
Assert-True ($recon.Status -eq 200) "reconciliation report returns 200" "status=$($recon.Status) err=$($recon.Error)"
if ($recon.Status -eq 200) {
    $n = @($recon.Data.transactions).Count
    Write-Host "reconciliation transactions: $n (sandbox reporting lag may legitimately exclude fresh activity)"
    # The payment captured minutes ago is not yet in PayPal's transaction report, so it
    # must surface as "missing in PayPal" — the reverse direction of the report.
    $missing = @($recon.Data.paymentsMissingInPayPal | Where-Object { $_.orderId -eq $orderId })
    Assert-True ($missing.Count -eq 1) "freshly captured payment visible as not-yet-reported by PayPal" "got $($missing.Count)"
    $shopperRecon = Invoke-Api -Method Get -Path "/api/reconciliation?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))" -Token $shopper
    Assert-True ($shopperRecon.Status -eq 403) "reconciliation is admin-only (403)" "got $($shopperRecon.Status)"
}

Write-Host ""
if ($script:failures -eq 0) {
    Write-Host "ALL CHECKS PASSED" -ForegroundColor Green
    exit 0
} else {
    Write-Host "$($script:failures) CHECK(S) FAILED" -ForegroundColor Red
    exit 1
}
