param([string]$BaseUrl = "https://localhost:17723")
$ErrorActionPreference = "Stop"
$base = $BaseUrl

function Invoke-Api($method, $path, $token, $body) {
    $curlArgs = @("-sk", "-X", $method, "$base$path", "-H", "Content-Type: application/json", "-w", "`n%{http_code}")
    if ($token) { $curlArgs += @("-H", "Authorization: Bearer $token") }
    if ($body) { $curlArgs += @("-d", $body) }
    $raw = (& curl.exe @curlArgs) -join "`n"
    $parts = $raw -split "`n"
    $code = $parts[-1]
    $json = ($parts[0..($parts.Count - 2)] -join "`n")
    return @{ Code = $code; Body = $json }
}

function Show($label, $result) {
    Write-Host "`n=== $label (HTTP $($result.Code)) ==="
    Write-Host $result.Body
}

$card = '{"number":"4111111111111111","expiry":"2028-12","securityCode":"123","cardholderName":"Demo User","billingAddress":{"addressLine1":"123 Main St","city":"Anytown","state":"CA","postalCode":"12345","countryCode":"US"}}'

# --- auth ---
$shopper = (Invoke-Api "POST" "/api/authenticate" $null '{"username":"demouser@microsoft.com","password":"Pass@word1"}').Body | ConvertFrom-Json
$admin = (Invoke-Api "POST" "/api/authenticate" $null '{"username":"admin@microsoft.com","password":"Pass@word1"}').Body | ConvertFrom-Json
$shopperToken = $shopper.token; $adminToken = $admin.token
Write-Host "shopper token: $($shopper.result), admin token: $($admin.result)"

# --- Flow 1: order + pay ---
$r = Invoke-Api "POST" "/api/orders" $shopperToken '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":3,"quantity":1}]}'
Show "create order 1" $r
$order1 = ($r.Body | ConvertFrom-Json).orderId

$r = Invoke-Api "POST" "/api/orders/$order1/pay" $shopperToken "{`"card`":$card}"
Show "pay order 1 (card)" $r
$auth1 = ($r.Body | ConvertFrom-Json).payment.authorizationId

$r = Invoke-Api "POST" "/api/orders/$order1/pay" $shopperToken "{`"card`":$card}"
Show "pay order 1 again (idempotent double-click)" $r
$auth1b = ($r.Body | ConvertFrom-Json).payment.authorizationId
Write-Host "IDEMPOTENT PAY: $(if ($auth1 -eq $auth1b) { 'PASS - same authorization' } else { 'FAIL' })"

$r = Invoke-Api "GET" "/api/my-orders" $shopperToken $null
Show "my-orders (shopper)" $r

# --- operator guard checks ---
$r = Invoke-Api "POST" "/api/orders/$order1/fulfil" $shopperToken $null
Write-Host "`nshopper fulfil attempt -> HTTP $($r.Code) (expect 403)"

# --- fulfil (admin) ---
$r = Invoke-Api "POST" "/api/orders/$order1/fulfil" $adminToken $null
Show "fulfil order 1 (admin, captures money)" $r
$capture1 = ($r.Body | ConvertFrom-Json).payment.captureId

$r = Invoke-Api "POST" "/api/orders/$order1/fulfil" $adminToken $null
$cap1b = ($r.Body | ConvertFrom-Json).payment.captureId
Write-Host "IDEMPOTENT FULFIL: $(if ($capture1 -eq $cap1b) { 'PASS - same capture' } else { 'FAIL' })"

# --- refunds (shopper) ---
$r = Invoke-Api "POST" "/api/orders/$order1/refunds" $shopperToken '{"amount":10.00,"idempotencyKey":"return-001"}'
Show "partial refund 10.00 (key return-001)" $r
$refund1 = ($r.Body | ConvertFrom-Json).refundId

$r = Invoke-Api "POST" "/api/orders/$order1/refunds" $shopperToken '{"amount":10.00,"idempotencyKey":"return-001"}'
$refund1b = ($r.Body | ConvertFrom-Json).refundId
Write-Host "IDEMPOTENT REFUND: $(if ($refund1 -eq $refund1b) { "PASS - same refundId $refund1b" } else { 'FAIL' })"

$r = Invoke-Api "POST" "/api/orders/$order1/refunds" $shopperToken '{"amount":5.00,"idempotencyKey":"return-002"}'
Show "second partial refund 5.00 (key return-002)" $r

$r = Invoke-Api "POST" "/api/orders/$order1/refunds" $shopperToken '{"amount":100.00,"idempotencyKey":"return-003"}'
Write-Host "`nover-refund attempt -> HTTP $($r.Code) (expect 409): $($r.Body)"

# --- Flow 2: saved cards ---
$r = Invoke-Api "POST" "/api/payment-methods" $shopperToken "{`"card`":$card}"
Show "save card" $r
$pmId = ($r.Body | ConvertFrom-Json).paymentMethodId

$r = Invoke-Api "GET" "/api/payment-methods" $shopperToken $null
Show "list saved cards" $r

$r = Invoke-Api "POST" "/api/orders" $shopperToken '{"items":[{"catalogItemId":2,"quantity":1}]}'
$order2 = ($r.Body | ConvertFrom-Json).orderId
Write-Host "`norder 2 id: $order2"

$r = Invoke-Api "POST" "/api/orders/$order2/pay" $shopperToken "{`"paymentMethodId`":$pmId}"
Show "pay order 2 with saved card" $r

# --- cancel (admin) releases the hold ---
$r = Invoke-Api "POST" "/api/orders/$order2/cancel" $adminToken $null
Show "cancel order 2 (admin, voids authorization)" $r

# --- delete saved card, prove unusable ---
$r = Invoke-Api "DELETE" "/api/payment-methods/$pmId" $shopperToken $null
Show "delete saved card" $r

$r = Invoke-Api "GET" "/api/payment-methods" $shopperToken $null
Show "list after delete (expect empty)" $r

$r = Invoke-Api "POST" "/api/orders" $shopperToken '{"items":[{"catalogItemId":2,"quantity":1}]}'
$order3 = ($r.Body | ConvertFrom-Json).orderId
$r = Invoke-Api "POST" "/api/orders/$order3/pay" $shopperToken "{`"paymentMethodId`":$pmId}"
Write-Host "`npay with deleted card -> HTTP $($r.Code) (expect 404): $($r.Body)"

# --- cross-user isolation ---
$r = Invoke-Api "GET" "/api/my-orders" $adminToken $null
$adminOrders = ($r.Body | ConvertFrom-Json).orders.Count
Write-Host "`nadmin sees $adminOrders own orders (shopper's orders invisible)"
$r = Invoke-Api "POST" "/api/orders/$order1/refunds" $adminToken '{"amount":1.00,"idempotencyKey":"admin-x"}'
Write-Host "admin refund on shopper's order -> HTTP $($r.Code) (expect 404 - not their order)"

# --- reconciliation (admin) ---
$from = (Get-Date).ToUniversalTime().AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ")
$to = (Get-Date).ToUniversalTime().AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ")
$r = Invoke-Api "GET" "/api/reconciliation?from=$from&to=$to" $adminToken $null
Show "reconciliation (admin)" $r
$r = Invoke-Api "GET" "/api/reconciliation?from=$from&to=$to" $shopperToken $null
Write-Host "`nshopper reconciliation attempt -> HTTP $($r.Code) (expect 403)"

Write-Host "`n=== DONE ==="
