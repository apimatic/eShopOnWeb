param()
$ErrorActionPreference = 'Stop'
$base = 'https://localhost:18083'
$PSDefaultParameterValues['Invoke-RestMethod:SkipCertificateCheck'] = $true
$PSDefaultParameterValues['Invoke-WebRequest:SkipCertificateCheck'] = $true

function Step($name) { Write-Host "`n=== $name ===" -ForegroundColor Cyan }

Step 'Authenticate shopper (demouser) and operator (admin)'
$demoLogin = Invoke-RestMethod -Method Post -Uri "$base/api/authenticate" -ContentType 'application/json' -Body (@{ username = 'demouser@microsoft.com'; password = 'Pass@word1' } | ConvertTo-Json)
$adminLogin = Invoke-RestMethod -Method Post -Uri "$base/api/authenticate" -ContentType 'application/json' -Body (@{ username = 'admin@microsoft.com'; password = 'Pass@word1' } | ConvertTo-Json)
$demo = @{ Authorization = "Bearer $($demoLogin.Token)" }
$admin = @{ Authorization = "Bearer $($adminLogin.Token)" }
Write-Host "shopper token: $($demoLogin.Token.Substring(0,24))..."
Write-Host "operator token: $($adminLogin.Token.Substring(0,24))..."

Step 'Shopper registers an invalid number -> must be rejected'
try {
    Invoke-RestMethod -Method Post -Uri "$base/api/contact-numbers" -Headers $demo -ContentType 'application/json' -Body (@{ phoneNumber = '123' } | ConvertTo-Json)
    Write-Host 'UNEXPECTED: invalid number accepted'
} catch {
    Write-Host "rejected as expected: HTTP $([int]$_.Exception.Response.StatusCode) - $($_.ErrorDetails.Message)"
}

Step 'Shopper registers the reachable Canadian number'
$toNumber = $env:TWILIO_TEST_TO_NUMBER
if (-not $toNumber) { throw 'TWILIO_TEST_TO_NUMBER env var not set' }
$reg = Invoke-RestMethod -Method Post -Uri "$base/api/contact-numbers" -Headers $demo -ContentType 'application/json' -Body (@{ phoneNumber = $toNumber } | ConvertTo-Json)
$reg | ConvertTo-Json -Compress
$script:contactNumberId = $reg.ContactNumberId

Step 'Shopper lists their numbers'
$list = Invoke-RestMethod -Method Get -Uri "$base/api/contact-numbers" -Headers $demo
$list.ContactNumbers | ConvertTo-Json -Compress

Step 'Shopper places an order -> order-placed SMS'
$order = Invoke-RestMethod -Method Post -Uri "$base/api/orders" -Headers $demo -ContentType 'application/json' -Body (@{ items = @(@{ catalogItemId = 1; quantity = 1 }) } | ConvertTo-Json)
$order | ConvertTo-Json -Compress
$script:orderId = $order.OrderId

Step 'Order notifications (status refreshed from provider)'
Start-Sleep -Seconds 5
$notifs = Invoke-RestMethod -Method Get -Uri "$base/api/orders/$($order.OrderId)/notifications" -Headers $demo
$notifs.Notifications | ConvertTo-Json -Compress

Step 'Operator dispatches the order -> dispatch SMS + scheduled follow-up'
$disp = Invoke-RestMethod -Method Post -Uri "$base/api/orders/$($order.OrderId)/dispatch" -Headers $admin
$disp | ConvertTo-Json -Compress
Start-Sleep -Seconds 3
$notifs2 = Invoke-RestMethod -Method Get -Uri "$base/api/orders/$($order.OrderId)/notifications" -Headers $demo
$notifs2.Notifications | ConvertTo-Json -Compress

Step 'Shopper cannot dispatch (operator-only) -> 403'
try {
    Invoke-RestMethod -Method Post -Uri "$base/api/orders/$($order.OrderId)/cancel" -Headers $demo
    Write-Host 'UNEXPECTED: shopper cancelled an order'
} catch {
    Write-Host "forbidden as expected: HTTP $([int]$_.Exception.Response.StatusCode)"
}

Step 'Operator cancels the order -> cancel SMS + follow-up called off'
$cancel = Invoke-RestMethod -Method Post -Uri "$base/api/orders/$($order.OrderId)/cancel" -Headers $admin
$cancel | ConvertTo-Json -Compress
Start-Sleep -Seconds 3
$notifs3 = Invoke-RestMethod -Method Get -Uri "$base/api/orders/$($order.OrderId)/notifications" -Headers $demo
$notifs3.Notifications | ConvertTo-Json -Compress

Step 'Shopper views my-orders with notification status'
$myOrders = Invoke-RestMethod -Method Get -Uri "$base/api/my-orders" -Headers $demo
$myOrders.Orders | ConvertTo-Json -Compress -Depth 5

Write-Host "`nDONE part 1. orderId=$($order.OrderId) contactNumberId=$($reg.ContactNumberId)"
