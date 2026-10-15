[CmdletBinding()]
param([string]$ProductHandle)
$ErrorActionPreference = 'Stop'
$api = "https://localhost:$([int]$env:APP_PORT_BLOCK_BASE + 3)"

# The seeded demo shopper; this token is held in memory and never printed or written to disk.
$auth = Invoke-RestMethod "$api/api/authenticate" -Method Post -ContentType 'application/json' `
    -Body (@{ username = 'demouser@microsoft.com'; password = 'Pass@word1' } | ConvertTo-Json)
if (-not $auth.result -or -not $auth.token) { throw 'Demo shopper authentication failed.' }
$headers = @{ Authorization = "Bearer $($auth.token)" }
$plans = @(Invoke-RestMethod "$api/api/subscription-plans" -Headers $headers | ForEach-Object { $_ })
if ($plans.Count -eq 0) { throw 'No available card-free subscription plans were found.' }
$plans | Format-Table handle, name, priceInCents, interval, intervalUnit
if (-not $ProductHandle) { $ProductHandle = ($plans | Sort-Object priceInCents -Descending | Select-Object -First 1).handle }
if (-not ($plans | Where-Object handle -CEQ $ProductHandle)) { throw 'Choose a handle returned by the plans endpoint.' }
$body = @{ productHandle = $ProductHandle; firstName = 'Demo'; lastName = 'Shopper' } | ConvertTo-Json
$enrollment = Invoke-RestMethod "$api/api/subscriptions" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
for ($attempt = 0; $enrollment.status -eq 'pending' -and $attempt -lt 35; $attempt++) {
    Start-Sleep -Seconds 2
    $enrollment = Invoke-RestMethod "$api/api/subscriptions" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
}
if ($enrollment.status -ne 'confirmed') { throw 'Enrollment is pending reconciliation; check the API logs and account response.' }
$repeat = Invoke-RestMethod "$api/api/subscriptions" -Method Post -Headers $headers -ContentType 'application/json' -Body $body
if ($repeat.subscription.id -ne $enrollment.subscription.id) { throw 'Repeat enrollment returned a different subscription.' }
$mine = Invoke-RestMethod "$api/api/my-subscriptions" -Headers $headers
$matching = @($mine.subscriptions | Where-Object productHandle -CEQ $ProductHandle)
if ($matching.Count -ne 1 -or $matching[0].id -ne $enrollment.subscription.id) { throw 'Account mapping verification failed.' }
if ($enrollment.subscription.state -eq 'active' -and -not $enrollment.subscription.nextBillingDate) { throw 'Active subscription has no next billing date.' }
try {
    Invoke-WebRequest "$api/api/my-subscriptions" | Out-Null
    throw 'Anonymous account access was unexpectedly allowed.'
} catch {
    if (-not $_.Exception.Response -or [int]$_.Exception.Response.StatusCode -ne 401) { throw }
}
$otherAuth = Invoke-RestMethod "$api/api/authenticate" -Method Post -ContentType 'application/json' `
    -Body (@{ username = 'admin@microsoft.com'; password = 'Pass@word1' } | ConvertTo-Json)
$other = Invoke-RestMethod "$api/api/my-subscriptions" -Headers @{ Authorization = "Bearer $($otherAuth.token)" }
if (@($other.subscriptions | Where-Object id -EQ $enrollment.subscription.id).Count -ne 0) { throw 'A different account can see this subscription.' }
$enrollment.subscription | Format-List
Write-Output 'Verified: enrollment, repeat-request idempotency, account visibility, JWT requirement, and user isolation.'
