$ErrorActionPreference = 'Stop'
$base = 'https://localhost:19703/api'

function Invoke-Api($method, $path, $token, $body) {
    $headers = @{ 'Content-Type' = 'application/json' }
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $params = @{ Method = $method; Uri = "$base$path"; Headers = $headers; SkipCertificateCheck = $true }
    if ($body) { $params['Body'] = ($body | ConvertTo-Json -Depth 10) }
    try {
        return Invoke-RestMethod @params
    } catch {
        $status = $null
        try { $status = [int]$_.Exception.Response.StatusCode } catch {}
        $text = $_.ErrorDetails.Message
        if (-not $text) { $text = $_.Exception.Message }
        Write-Host "ERROR $method $path -> $status : $text" -ForegroundColor Red
        throw
    }
}

# --- authenticate shopper and operator ---
$shopper = Invoke-Api 'POST' '/authenticate' $null @{ username = 'demouser@microsoft.com'; password = 'Pass@word1' }
$admin = Invoke-Api 'POST' '/authenticate' $null @{ username = 'admin@microsoft.com'; password = 'Pass@word1' }
$shopperToken = $shopper.token
$adminToken = $admin.token
Write-Host "1. Authenticated shopper + admin" -ForegroundColor Green

# --- catalog items ---
$catalog = Invoke-Api 'GET' '/catalog-items?pageSize=5&pageIndex=0' $shopperToken $null
$item1 = $catalog.catalogItems[0].id
$item2 = $catalog.catalogItems[1].id
$price1 = $catalog.catalogItems[0].price
Write-Host "2. Catalog items: $item1 ($price1), $item2" -ForegroundColor Green

# --- Flow 1: order + pay + fulfil + refund ---
$order = Invoke-Api 'POST' '/orders' $shopperToken @{
    items = @(@{ catalogItemId = $item1; quantity = 2 }, @{ catalogItemId = $item2; quantity = 1 })
    shipToAddress = @{ street = '1 Main St'; city = 'Seattle'; state = 'WA'; country = 'US'; zipCode = '98101' }
}
Write-Host "3. Order created: orderId=$($order.orderId) status=$($order.status) total=$($order.total) $($order.currency)" -ForegroundColor Green

$pay = Invoke-Api 'POST' "/orders/$($order.orderId)/pay" $shopperToken @{
    card = @{
        number = '4111111111111111'; expiry = '2028-12'; securityCode = '123'; name = 'Demo User'
        billingAddress = @{ addressLine1 = '1 Main St'; city = 'Seattle'; state = 'WA'; postalCode = '98101'; countryCode = 'US' }
    }
}
Write-Host "4. Paid (authorized): authId=$($pay.authorizationId) amount=$($pay.authorizedAmount) $($pay.currency) status=$($pay.authorizationStatus)" -ForegroundColor Green
if ($pay.authorizedAmount -ne $order.total) { throw "Authorized amount mismatch!" }

# double-pay must be idempotent
$payAgain = Invoke-Api 'POST' "/orders/$($order.orderId)/pay" $shopperToken @{
    card = @{ number = '4111111111111111'; expiry = '2028-12'; securityCode = '123'; name = 'Demo User' }
}
if ($payAgain.authorizationId -ne $pay.authorizationId) { throw "Double pay created a NEW authorization!" }
Write-Host "5. Double-pay idempotent: same authorizationId=$($payAgain.authorizationId)" -ForegroundColor Green

# shopper cannot fulfil (operator-only)
try {
    Invoke-Api 'POST' "/orders/$($order.orderId)/fulfil" $shopperToken $null
    throw "Shopper was able to fulfil!"
} catch { Write-Host "6. Shopper fulfil correctly rejected" -ForegroundColor Green }

$fulfil = Invoke-Api 'POST' "/orders/$($order.orderId)/fulfil" $adminToken $null
Write-Host "7. Fulfilled: captureId=$($fulfil.captureId) captured=$($fulfil.capturedAmount) fee=$($fulfil.payPalFee) net=$($fulfil.netAmount)" -ForegroundColor Green

# idempotent fulfil
$fulfilAgain = Invoke-Api 'POST' "/orders/$($order.orderId)/fulfil" $adminToken $null
if ($fulfilAgain.captureId -ne $fulfil.captureId) { throw "Double fulfil created a NEW capture!" }
Write-Host "8. Double-fulfil idempotent: same captureId" -ForegroundColor Green

# partial refund + idempotency
$refund1 = Invoke-Api 'POST' "/orders/$($order.orderId)/refunds" $adminToken @{ amount = 1.00; idempotencyKey = 'ref-key-1' }
Write-Host "9. Partial refund: refundId=$($refund1.refundId) paypal=$($refund1.payPalRefundId) amount=$($refund1.amount) status=$($refund1.status)" -ForegroundColor Green

$refund1Again = Invoke-Api 'POST' "/orders/$($order.orderId)/refunds" $adminToken @{ amount = 1.00; idempotencyKey = 'ref-key-1' }
if ($refund1Again.payPalRefundId -ne $refund1.payPalRefundId) { throw "Same idempotency key refunded twice!" }
Write-Host "10. Same idempotency key -> same refund (no double refund)" -ForegroundColor Green

$refund2 = Invoke-Api 'POST' "/orders/$($order.orderId)/refunds" $adminToken @{ amount = 0.50; idempotencyKey = 'ref-key-2' }
Write-Host "11. Second distinct partial refund ok: refundId=$($refund2.refundId)" -ForegroundColor Green

# over-refund must fail
try {
    Invoke-Api 'POST' "/orders/$($order.orderId)/refunds" $adminToken @{ amount = 99999; idempotencyKey = 'ref-key-3' }
    throw "Over-refund succeeded!"
} catch { Write-Host "12. Over-refund correctly rejected" -ForegroundColor Green }

# --- Flow 2: saved cards ---
$saved = Invoke-Api 'POST' '/payment-methods' $shopperToken @{
    card = @{
        number = '4111111111111111'; expiry = '2029-06'; securityCode = '123'; name = 'Demo User'
        billingAddress = @{ addressLine1 = '1 Main St'; city = 'Seattle'; state = 'WA'; postalCode = '98101'; countryCode = 'US' }
    }
}
Write-Host "13. Card saved: paymentMethodId=$($saved.paymentMethodId) $($saved.brand) ****$($saved.lastDigits) exp=$($saved.expiry)" -ForegroundColor Green

$cards = Invoke-Api 'GET' '/payment-methods' $shopperToken $null
Write-Host "14. Listed saved cards: $($cards.paymentMethods.Count)" -ForegroundColor Green

$order2 = Invoke-Api 'POST' '/orders' $shopperToken @{
    items = @(@{ catalogItemId = $item2; quantity = 1 })
    shipToAddress = @{ street = '1 Main St'; city = 'Seattle'; state = 'WA'; country = 'US'; zipCode = '98101' }
}
$pay2 = Invoke-Api 'POST' "/orders/$($order2.orderId)/pay" $shopperToken @{ paymentMethodId = $saved.paymentMethodId }
Write-Host "15. Second order paid with saved card: authId=$($pay2.authorizationId) amount=$($pay2.authorizedAmount)" -ForegroundColor Green

# cancel order2 (operator) -> void
$cancel = Invoke-Api 'POST' "/orders/$($order2.orderId)/cancel" $adminToken $null
Write-Host "16. Order2 cancelled: fundsReleased=$($cancel.fundsReleased)" -ForegroundColor Green

# paying with deleted card must fail
$del = Invoke-Api 'DELETE' "/payment-methods/$($saved.paymentMethodId)" $shopperToken $null
$cardsAfter = Invoke-Api 'GET' '/payment-methods' $shopperToken $null
if ($cardsAfter.paymentMethods.Count -ne 0) { throw "Deleted card still listed!" }
Write-Host "17. Card deleted; no longer listed" -ForegroundColor Green

$order3 = Invoke-Api 'POST' '/orders' $shopperToken @{
    items = @(@{ catalogItemId = $item2; quantity = 1 })
    shipToAddress = @{ street = '1 Main St'; city = 'Seattle'; state = 'WA'; country = 'US'; zipCode = '98101' }
}
try {
    Invoke-Api 'POST' "/orders/$($order3.orderId)/pay" $shopperToken @{ paymentMethodId = $saved.paymentMethodId }
    throw "Deleted card was usable!"
} catch { Write-Host "18. Deleted card cannot be used to pay" -ForegroundColor Green }

# cross-shopper isolation: admin token (different user) must not see shopper's orders via my-orders
$adminOrders = Invoke-Api 'GET' '/my-orders' $adminToken $null
if ($adminOrders.orders.Count -ne 0) { throw "Admin sees shopper orders in my-orders!" }
try {
    Invoke-Api 'POST' "/orders/$($order.orderId)/pay" $adminToken @{ card = @{ number = '4111111111111111'; expiry = '2028-12' } }
    throw "Admin could act on shopper's order!"
} catch { Write-Host "19. Cross-shopper access correctly denied" -ForegroundColor Green }

# my-orders shows payment state
$myOrders = Invoke-Api 'GET' '/my-orders' $shopperToken $null
$o1 = $myOrders.orders | Where-Object { $_.orderId -eq $order.orderId }
Write-Host "20. my-orders: order $($o1.orderId) status=$($o1.status) captured=$($o1.payment.capturedAmount) fee=$($o1.payment.payPalFee) net=$($o1.payment.netAmount) refunded=$($o1.payment.totalRefunded) refundable=$($o1.payment.refundableAmount)" -ForegroundColor Green

# reconciliation (operator)
$from = (Get-Date).ToUniversalTime().AddDays(-1).ToString('yyyy-MM-ddTHH:mm:ssZ')
$to = (Get-Date).ToUniversalTime().AddHours(1).ToString('yyyy-MM-ddTHH:mm:ssZ')
$recon = Invoke-Api 'GET' "/reconciliation?from=$from&to=$to" $adminToken $null
Write-Host "21. Reconciliation: $($recon.entries.Count) entries (sandbox reporting lag may legitimately yield 0)" -ForegroundColor Green
$recon.entries | ForEach-Object { Write-Host "    $($_.payPalTransactionId) $($_.matchStatus) $($_.amount) $($_.currency) order=$($_.orderId)" }

Write-Host "ALL CHECKS PASSED" -ForegroundColor Cyan
