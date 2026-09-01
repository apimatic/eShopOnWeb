$ErrorActionPreference = "Stop"
$base = "https://localhost:21543"
$tmpBody = Join-Path $env:TEMP "eshop-verify-body.json"

function Invoke-Api($method, $path, $token, $body) {
    $args = @("-k", "-s", "-X", $method, "$base$path", "-H", "Content-Type: application/json")
    if ($token) { $args += @("-H", "Authorization: Bearer $token") }
    if ($null -ne $body) {
        ($body | ConvertTo-Json -Depth 10 -Compress) | Set-Content -Path $tmpBody -Encoding ascii
        $args += @("-d", "@$tmpBody")
    }
    $out = & curl.exe @args
    if ([string]::IsNullOrWhiteSpace($out)) { return $null }
    return $out | ConvertFrom-Json
}

# 1. Authenticate shopper + admin
$shopper = Invoke-Api "POST" "/api/authenticate" $null @{ username = "demouser@microsoft.com"; password = "Pass@word1" }
$admin = Invoke-Api "POST" "/api/authenticate" $null @{ username = "admin@microsoft.com"; password = "Pass@word1" }
"shopper token: $($shopper.token.Substring(0,20))..."
"admin token: $($admin.token.Substring(0,20))..."

# 2. Create order
$order = Invoke-Api "POST" "/api/orders" $shopper.token @{ items = @(@{ catalogItemId = 1; quantity = 2 }, @{ catalogItemId = 2; quantity = 1 }) }
"ORDER: $( $order | ConvertTo-Json -Compress )"

# 3. Pay with one-off card
$pay = Invoke-Api "POST" "/api/orders/$($order.orderId)/pay" $shopper.token @{
    cardNumber = "4111111111111111"; expiryMonth = 12; expiryYear = 2028
    securityCode = "123"; cardholderName = "Demo User"
    billingAddress = @{ street = "1 Main St"; city = "San Jose"; state = "CA"; zipCode = "95131"; country = "US" }
}
"PAY: $( $pay | ConvertTo-Json -Compress )"

# 3b. Idempotent re-pay (double-click)
$pay2 = Invoke-Api "POST" "/api/orders/$($order.orderId)/pay" $shopper.token @{
    cardNumber = "4111111111111111"; expiryMonth = 12; expiryYear = 2028
    securityCode = "123"; cardholderName = "Demo User"
}
"PAY-AGAIN (should be same authorization, no double hold): $( $pay2 | ConvertTo-Json -Compress )"

# 4. Fulfil as admin (capture)
$fulfil = Invoke-Api "POST" "/api/orders/$($order.orderId)/fulfil" $admin.token $null
"FULFIL: $( $fulfil | ConvertTo-Json -Compress )"

# 4b. Fulfil again (idempotent)
$fulfil2 = Invoke-Api "POST" "/api/orders/$($order.orderId)/fulfil" $admin.token $null
"FULFIL-AGAIN: $( $fulfil2 | ConvertTo-Json -Compress )"

# 5. Partial refund + idempotent repeat + second partial
$r1 = Invoke-Api "POST" "/api/orders/$($order.orderId)/refunds" $admin.token @{ amount = 1.00; idempotencyKey = "refund-key-1"; noteToPayer = "partial 1" }
"REFUND1: $( $r1 | ConvertTo-Json -Compress )"
$r1b = Invoke-Api "POST" "/api/orders/$($order.orderId)/refunds" $admin.token @{ amount = 1.00; idempotencyKey = "refund-key-1"; noteToPayer = "partial 1" }
"REFUND1-REPEAT (must not refund twice): $( $r1b | ConvertTo-Json -Compress )"
$r2 = Invoke-Api "POST" "/api/orders/$($order.orderId)/refunds" $admin.token @{ amount = 0.50; idempotencyKey = "refund-key-2" }
"REFUND2: $( $r2 | ConvertTo-Json -Compress )"

# 5b. Over-refund attempt must fail
$over = Invoke-Api "POST" "/api/orders/$($order.orderId)/refunds" $admin.token @{ amount = 9999.00; idempotencyKey = "refund-key-3" }
"OVER-REFUND (should be error): $( $over | ConvertTo-Json -Compress )"

# 6. Save a card, list, pay second order with it
$pm = Invoke-Api "POST" "/api/payment-methods" $shopper.token @{
    cardNumber = "4111111111111111"; expiryMonth = 11; expiryYear = 2029
    securityCode = "456"; cardholderName = "Demo User"
    billingAddress = @{ street = "1 Main St"; city = "San Jose"; state = "CA"; zipCode = "95131"; country = "US" }
}
"SAVED CARD: $( $pm | ConvertTo-Json -Compress )"
$pmlist = Invoke-Api "GET" "/api/payment-methods" $shopper.token $null
"LIST CARDS: $( $pmlist | ConvertTo-Json -Compress )"

$order2 = Invoke-Api "POST" "/api/orders" $shopper.token @{ items = @(@{ catalogItemId = 3; quantity = 1 }) }
$payVault = Invoke-Api "POST" "/api/orders/$($order2.orderId)/pay" $shopper.token @{ paymentMethodId = $pm.paymentMethodId }
"PAY WITH SAVED CARD: $( $payVault | ConvertTo-Json -Compress )"

# 7. Third order: pay then cancel (void)
$order3 = Invoke-Api "POST" "/api/orders" $shopper.token @{ items = @(@{ catalogItemId = 4; quantity = 1 }) }
$pay3 = Invoke-Api "POST" "/api/orders/$($order3.orderId)/pay" $shopper.token @{
    cardNumber = "4111111111111111"; expiryMonth = 10; expiryYear = 2027; securityCode = "789"; cardholderName = "Demo User"
}
"PAY3: $( $pay3 | ConvertTo-Json -Compress )"
$cancel = Invoke-Api "POST" "/api/orders/$($order3.orderId)/cancel" $admin.token $null
"CANCEL: $( $cancel | ConvertTo-Json -Compress )"

# 8. Delete saved card, verify gone and unusable
Invoke-Api "DELETE" "/api/payment-methods/$($pm.paymentMethodId)" $shopper.token $null | Out-Null
$pmlist2 = Invoke-Api "GET" "/api/payment-methods" $shopper.token $null
"LIST AFTER DELETE: $( $pmlist2 | ConvertTo-Json -Compress )"
$order4 = Invoke-Api "POST" "/api/orders" $shopper.token @{ items = @(@{ catalogItemId = 5; quantity = 1 }) }
$payDeleted = Invoke-Api "POST" "/api/orders/$($order4.orderId)/pay" $shopper.token @{ paymentMethodId = $pm.paymentMethodId }
"PAY WITH DELETED CARD (should be 404 error): $( $payDeleted | ConvertTo-Json -Compress )"

# 9. My orders
$myOrders = Invoke-Api "GET" "/api/my-orders" $shopper.token $null
"MY-ORDERS count: $($myOrders.orders.Count)"

# 10. Reconciliation (admin)
$from = (Get-Date).ToUniversalTime().AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ")
$to = (Get-Date).ToUniversalTime().AddHours(1).ToString("yyyy-MM-ddTHH:mm:ssZ")
$recon = Invoke-Api "GET" "/api/reconciliation?from=$from&to=$to" $admin.token $null
"RECON: $( $recon | ConvertTo-Json -Compress -Depth 6 )"

# 11. Shopper cannot call operator endpoints
$forbidden = & curl.exe -k -s -o NUL -w "%{http_code}" -X POST "$base/api/orders/$($order.orderId)/fulfil" -H "Authorization: Bearer $($shopper.token)"
"SHOPPER FULFIL ATTEMPT HTTP: $forbidden (expect 403)"

# 12. Delete nonexistent saved card
$other = Invoke-Api "DELETE" "/api/payment-methods/99999" $shopper.token $null
"DELETE NONEXISTENT (expect 404): $( $other | ConvertTo-Json -Compress )"
