$ErrorActionPreference = 'Stop'
$base = 'https://localhost:17783/api'
$ProgressPreference = 'SilentlyContinue'

function Invoke-Api($method, $path, $token, $body) {
    $headers = @{}
    if ($token) { $headers['Authorization'] = "Bearer $token" }
    $params = @{ Method = $method; Uri = "$base$path"; Headers = $headers; UseBasicParsing = $true; SkipCertificateCheck = $true }
    if ($body) { $params['Body'] = ($body | ConvertTo-Json -Depth 10); $params['ContentType'] = 'application/json' }
    try {
        $r = Invoke-WebRequest @params
        return @{ Status = [int]$r.StatusCode; Body = ($r.Content | ConvertFrom-Json) }
    } catch {
        $code = -1
        $content = $_.ErrorDetails.Message
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        return @{ Status = $code; Body = $content }
    }
}

function Get-Token($user) {
    $r = Invoke-Api 'POST' '/authenticate' $null @{ username = $user; password = 'Pass@word1' }
    if (-not $r.Body.token) { throw "Auth failed for ${user}: status=$($r.Status) body=$($r.Body)" }
    return $r.Body.token
}

$shopper = Get-Token 'demouser@microsoft.com'
$admin = Get-Token 'admin@microsoft.com'
Write-Host "== tokens acquired =="

# --- Flow 1: order -> pay (one-off card) -> fulfil -> partial refunds ---
$order1 = Invoke-Api 'POST' '/orders' $shopper @{ items = @(@{ catalogItemId = 1; quantity = 1 }, @{ catalogItemId = 2; quantity = 2 }) }
Write-Host "order1: $($order1.Status) id=$($order1.Body.orderId) total=$($order1.Body.total) $($order1.Body.currency) status=$($order1.Body.status)"
$oid1 = $order1.Body.orderId

$card = @{ number = '4111111111111111'; expiry = '2030-12'; securityCode = '123'; name = 'Demo User'; billingAddress = @{ addressLine1 = '1 Main St'; adminArea2 = 'San Jose'; adminArea1 = 'CA'; postalCode = '95131'; countryCode = 'US' } }
$pay1 = Invoke-Api 'POST' "/orders/$oid1/pay" $shopper @{ card = $card }
Write-Host "pay1: $($pay1.Status) status=$($pay1.Body.status) authId=$($pay1.Body.payment.authorizationId) authStatus=$($pay1.Body.payment.authorizationStatus) amount=$($pay1.Body.payment.amount) $($pay1.Body.payment.currency)"

# idempotent replay of pay
$pay1b = Invoke-Api 'POST' "/orders/$oid1/pay" $shopper @{ card = $card }
Write-Host "pay1 replay: $($pay1b.Status) authId=$($pay1b.Body.payment.authorizationId) (must equal $($pay1.Body.payment.authorizationId))"

# shopper cannot fulfil (operator-only)
$fulfilDenied = Invoke-Api 'POST' "/orders/$oid1/fulfil" $shopper $null
Write-Host "fulfil as shopper: $($fulfilDenied.Status) (expect 403)"

$fulfil1 = Invoke-Api 'POST' "/orders/$oid1/fulfil" $admin $null
Write-Host "fulfil1: $($fulfil1.Status) status=$($fulfil1.Body.status) captureId=$($fulfil1.Body.payment.captureId) captured=$($fulfil1.Body.payment.capturedAmount) fee=$($fulfil1.Body.payment.payPalFee) net=$($fulfil1.Body.payment.netAmount)"

# idempotent replay of fulfil
$fulfil1b = Invoke-Api 'POST' "/orders/$oid1/fulfil" $admin $null
Write-Host "fulfil1 replay: $($fulfil1b.Status) captureId=$($fulfil1b.Body.payment.captureId) (must equal $($fulfil1.Body.payment.captureId))"

$ref1 = Invoke-Api 'POST' "/orders/$oid1/refunds" $shopper @{ amount = 5.00; idempotencyKey = 'refund-key-1'; note = 'partial return' }
Write-Host "refund1: $($ref1.Status) refundId=$($ref1.Body.refundId) amount=$($ref1.Body.amount) status=$($ref1.Body.status)"

$ref1replay = Invoke-Api 'POST' "/orders/$oid1/refunds" $shopper @{ amount = 5.00; idempotencyKey = 'refund-key-1' }
Write-Host "refund1 replay: $($ref1replay.Status) refundId=$($ref1replay.Body.refundId) (must equal $($ref1.Body.refundId))"

$ref2 = Invoke-Api 'POST' "/orders/$oid1/refunds" $shopper @{ amount = 3.00; idempotencyKey = 'refund-key-2' }
Write-Host "refund2 (distinct partial): $($ref2.Status) refundId=$($ref2.Body.refundId) amount=$($ref2.Body.amount)"

$refTooMuch = Invoke-Api 'POST' "/orders/$oid1/refunds" $shopper @{ amount = 9999.00; idempotencyKey = 'refund-key-3' }
Write-Host "refund beyond captured: $($refTooMuch.Status) (expect 409) $($refTooMuch.Body)"

# --- Flow 2: saved card -> pay second order -> fulfil -> full refund ---
$pm = Invoke-Api 'POST' '/payment-methods' $shopper @{ card = $card }
Write-Host "save card: $($pm.Status) paymentMethodId=$($pm.Body.paymentMethodId) brand=$($pm.Body.brand) last4=$($pm.Body.lastDigits) expiry=$($pm.Body.expiry)"
$pmId = $pm.Body.paymentMethodId

$pms = Invoke-Api 'GET' '/payment-methods' $shopper $null
Write-Host "list cards: $($pms.Status) count=$($pms.Body.paymentMethods.Count)"

$order2 = Invoke-Api 'POST' '/orders' $shopper @{ items = @(@{ catalogItemId = 3; quantity = 1 }) }
$oid2 = $order2.Body.orderId
Write-Host "order2: id=$oid2 total=$($order2.Body.total)"

$pay2 = Invoke-Api 'POST' "/orders/$oid2/pay" $shopper @{ paymentMethodId = $pmId }
Write-Host "pay2 (saved card): $($pay2.Status) authId=$($pay2.Body.payment.authorizationId) label=$($pay2.Body.payment.paymentMethodLabel)"

$fulfil2 = Invoke-Api 'POST' "/orders/$oid2/fulfil" $admin $null
Write-Host "fulfil2: $($fulfil2.Status) captured=$($fulfil2.Body.payment.capturedAmount) fee=$($fulfil2.Body.payment.payPalFee) net=$($fulfil2.Body.payment.netAmount)"

$refFull = Invoke-Api 'POST' "/orders/$oid2/refunds" $shopper @{ idempotencyKey = 'refund-full-1' }
Write-Host "refund full: $($refFull.Status) refundId=$($refFull.Body.refundId) amount=$($refFull.Body.amount)"

$refAfterFull = Invoke-Api 'POST' "/orders/$oid2/refunds" $shopper @{ amount = 1.00; idempotencyKey = 'refund-key-4' }
Write-Host "refund after full: $($refAfterFull.Status) (expect 409)"

# --- cancel flow ---
$order3 = Invoke-Api 'POST' '/orders' $shopper @{ items = @(@{ catalogItemId = 4; quantity = 1 }) }
$oid3 = $order3.Body.orderId
$pay3 = Invoke-Api 'POST' "/orders/$oid3/pay" $shopper @{ card = $card }
Write-Host "pay3: $($pay3.Status) authId=$($pay3.Body.payment.authorizationId)"
$cancel3 = Invoke-Api 'POST' "/orders/$oid3/cancel" $admin $null
Write-Host "cancel3: $($cancel3.Status) status=$($cancel3.Body.status) authStatus=$($cancel3.Body.payment.authorizationStatus)"
$payAfterCancel = Invoke-Api 'POST' "/orders/$oid3/pay" $shopper @{ card = $card }
Write-Host "pay after cancel: $($payAfterCancel.Status) (expect 409)"

# --- my-orders ---
$myOrders = Invoke-Api 'GET' '/my-orders' $shopper $null
Write-Host "my-orders: $($myOrders.Status) count=$($myOrders.Body.orders.Count) statuses=$(($myOrders.Body.orders | ForEach-Object { $_.status }) -join ',')"

# --- cross-shopper isolation ---
$otherOrder = Invoke-Api 'POST' "/orders/$oid1/pay" $admin @{ card = $card }
Write-Host "admin paying shopper's order: $($otherOrder.Status) (expect 404)"

# --- delete saved card ---
$del = Invoke-Api 'DELETE' "/payment-methods/$pmId" $shopper $null
Write-Host "delete card: $($del.Status)"
$pmsAfter = Invoke-Api 'GET' '/payment-methods' $shopper $null
Write-Host "list after delete: count=$($pmsAfter.Body.paymentMethods.Count) (expect 0)"
$order4 = Invoke-Api 'POST' '/orders' $shopper @{ items = @(@{ catalogItemId = 5; quantity = 1 }) }
$payDeleted = Invoke-Api 'POST' "/orders/$($order4.Body.orderId)/pay" $shopper @{ paymentMethodId = $pmId }
Write-Host "pay with deleted card: $($payDeleted.Status) (expect 404)"

# --- reconciliation (admin) ---
$from = (Get-Date).ToUniversalTime().AddDays(-1).ToString('yyyy-MM-ddTHH:mm:ssZ')
$to = (Get-Date).ToUniversalTime().AddDays(1).ToString('yyyy-MM-ddTHH:mm:ssZ')
$recon = Invoke-Api 'GET' "/reconciliation?from=$from&to=$to" $admin $null
Write-Host "reconciliation: $($recon.Status) transactions=$($recon.Body.report.transactions.Count) localNotInPayPal=$($recon.Body.report.localPaymentsNotInPayPal.Count)"
$reconDenied = Invoke-Api 'GET' "/reconciliation?from=$from&to=$to" $shopper $null
Write-Host "reconciliation as shopper: $($reconDenied.Status) (expect 403)"

Write-Host "== ALL FLOWS EXECUTED =="
