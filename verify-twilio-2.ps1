param()
$ErrorActionPreference = 'Stop'
$base = 'https://localhost:18083'
$PSDefaultParameterValues['Invoke-RestMethod:SkipCertificateCheck'] = $true

function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }

Step 'Authenticate operator (admin) and shopper (demouser)'
$adminLogin = Invoke-RestMethod -Method Post -Uri "$base/api/authenticate" -ContentType 'application/json' -Body (@{ username = 'admin@microsoft.com'; password = 'Pass@word1' } | ConvertTo-Json)
$demoLogin = Invoke-RestMethod -Method Post -Uri "$base/api/authenticate" -ContentType 'application/json' -Body (@{ username = 'demouser@microsoft.com'; password = 'Pass@word1' } | ConvertTo-Json)
$admin = @{ Authorization = "Bearer $($adminLogin.Token)" }
$demo = @{ Authorization = "Bearer $($demoLogin.Token)" }

Step 'Operator registers the unreachable US number (valid format, undeliverable)'
$unreachable = $env:TWILIO_UNREACHABLE_TO_NUMBER
if (-not $unreachable) { throw 'TWILIO_UNREACHABLE_TO_NUMBER env var not set' }
$reg = Invoke-RestMethod -Method Post -Uri "$base/api/contact-numbers" -Headers $admin -ContentType 'application/json' -Body (@{ phoneNumber = $unreachable } | ConvertTo-Json)
$reg | ConvertTo-Json -Compress

Step 'Operator places an order -> SMS accepted, then refused by carrier'
$order = Invoke-RestMethod -Method Post -Uri "$base/api/orders" -Headers $admin -ContentType 'application/json' -Body (@{ items = @(@{ catalogItemId = 2; quantity = 1 }) } | ConvertTo-Json)
$order | ConvertTo-Json -Compress
$orderId = $order.OrderId
Write-Host 'waiting for carrier refusal...'
Start-Sleep -Seconds 12
$notifs = Invoke-RestMethod -Method Get -Uri "$base/api/orders/$orderId/notifications" -Headers $admin
$notifs.Notifications | ConvertTo-Json -Compress
$failed = $notifs.Notifications[0]
if ($failed.Status -notin @('undelivered', 'failed')) { Write-Host "NOTE: status is $($failed.Status) (may still be in transit)" -ForegroundColor Yellow }

Step 'Shopper scoping: demouser cannot see the operator order notifications -> 404'
try {
    Invoke-RestMethod -Method Get -Uri "$base/api/orders/$orderId/notifications" -Headers $demo
    Write-Host 'UNEXPECTED: shopper saw another shopper''s order'
} catch {
    Write-Host "not found as expected: HTTP $([int]$_.Exception.Response.StatusCode)"
}

Step 'Resend the failed message with an idempotency key'
$resend1 = Invoke-RestMethod -Method Post -Uri "$base/api/notifications/$($failed.NotificationId)/resend" -Headers $admin -ContentType 'application/json' -Body (@{ idempotencyKey = 'op-retry-1' } | ConvertTo-Json)
$resend1 | ConvertTo-Json -Compress

Step 'Repeat the same request under the same key -> same message, no second send'
$resend2 = Invoke-RestMethod -Method Post -Uri "$base/api/notifications/$($failed.NotificationId)/resend" -Headers $admin -ContentType 'application/json' -Body (@{ idempotencyKey = 'op-retry-1' } | ConvertTo-Json)
$resend2 | ConvertTo-Json -Compress
if ($resend1.NotificationId -eq $resend2.NotificationId) { Write-Host 'IDEMPOTENT: same notificationId returned' -ForegroundColor Green } else { Write-Host 'FAILED: different notificationId' -ForegroundColor Red }

Step 'Dispose of message content (order 1, notification 1) -> erased at provider, record survives'
$del = Invoke-RestMethod -Method Delete -Uri "$base/api/notifications/1/content" -Headers $admin
$del | ConvertTo-Json -Compress
$after = Invoke-RestMethod -Method Get -Uri "$base/api/orders/1/notifications" -Headers $admin
($after.Notifications | Where-Object { $_.NotificationId -eq 1 }) | ConvertTo-Json -Compress

Step 'Resend a disposed message -> rejected'
try {
    Invoke-RestMethod -Method Post -Uri "$base/api/notifications/1/resend" -Headers $admin -ContentType 'application/json' -Body (@{ idempotencyKey = 'op-retry-2' } | ConvertTo-Json)
    Write-Host 'UNEXPECTED: disposed content was resent'
} catch {
    Write-Host "rejected as expected: HTTP $([int]$_.Exception.Response.StatusCode) - $($_.ErrorDetails.Message)"
}

Step 'Reconciliation report over today'
$from = [DateTimeOffset]::UtcNow.Date.ToString('o')
$to = [DateTimeOffset]::UtcNow.AddHours(1).ToString('o')
$report = Invoke-RestMethod -Method Get -Uri "$base/api/notifications/reconciliation?from=$([uri]::EscapeDataString($from))&to=$([uri]::EscapeDataString($to))" -Headers $admin
"fromNumber: $($report.FromNumber)  truncated: $($report.ProviderListTruncated)"
"matched: $($report.Matched.Count)  providerOnly: $($report.ProviderOnly.Count)  appOnly: $($report.AppOnly.Count)"
$report.Matched | ConvertTo-Json -Compress
$report.AppOnly | ConvertTo-Json -Compress

Step 'Delete the shopper contact number -> gone, and nothing sent to it again'
Invoke-RestMethod -Method Delete -Uri "$base/api/contact-numbers/1" -Headers $demo | Out-Null
$remaining = Invoke-RestMethod -Method Get -Uri "$base/api/contact-numbers" -Headers $demo
"remaining numbers for shopper: $($remaining.ContactNumbers.Count)"
try {
    Invoke-RestMethod -Method Delete -Uri "$base/api/contact-numbers/1" -Headers $demo
    Write-Host 'UNEXPECTED: second delete succeeded'
} catch {
    Write-Host "second delete -> HTTP $([int]$_.Exception.Response.StatusCode) (expected 404)"
}

Step 'Order with no number on file -> succeeds, no notifications'
Invoke-RestMethod -Method Delete -Uri "$base/api/contact-numbers/$($reg.ContactNumberId)" -Headers $admin | Out-Null
$order2 = Invoke-RestMethod -Method Post -Uri "$base/api/orders" -Headers $admin -ContentType 'application/json' -Body (@{ items = @(@{ catalogItemId = 3; quantity = 2 }) } | ConvertTo-Json)
$order2 | ConvertTo-Json -Compress
$notifs2 = Invoke-RestMethod -Method Get -Uri "$base/api/orders/$($order2.OrderId)/notifications" -Headers $admin
"notifications for number-less order: $($notifs2.Notifications.Count)"

Write-Host "`nDONE part 2."
