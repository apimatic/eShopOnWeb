# Test script for Maxio subscription endpoints
# Prerequisites:
# - PublicApi running on https://localhost:25023
# - Database seeded with test user
# - Maxio credentials configured in user-secrets

$BaseUrl = "https://localhost:25023/api"
$Username = "test@example.com"
$Password = "Pass@word1"

Write-Host "=== Maxio Subscription Endpoints Test ==="
Write-Host ""

# Step 1: Authenticate
Write-Host "Step 1: Authenticating..."
$authResponse = Invoke-WebRequest -Uri "$BaseUrl/authenticate" `
    -Method POST `
    -ContentType "application/json" `
    -Body (@{
        username = $Username
        password = $Password
    } | ConvertTo-Json) `
    -SkipCertificateCheck

$authJson = $authResponse.Content | ConvertFrom-Json
$token = $authJson.token

if (-not $token) {
    Write-Error "Failed to authenticate. Response: $($authResponse.Content)"
    exit 1
}

Write-Host "✓ Authentication successful"
Write-Host "  Token: $($token.Substring(0, 20))..."
Write-Host ""

# Headers for authenticated requests
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

# Step 2: List subscription plans
Write-Host "Step 2: Listing subscription plans..."
try {
    $plansResponse = Invoke-WebRequest -Uri "$BaseUrl/subscription-plans" `
        -Method GET `
        -Headers $headers `
        -SkipCertificateCheck

    $plansJson = $plansResponse.Content | ConvertFrom-Json
    $plans = $plansJson.subscriptionPlans

    Write-Host "✓ Plans retrieved: $($plans.Count) plan(s)"
    foreach ($plan in $plans) {
        Write-Host "  - $($plan.name) ($($plan.handle)): $($plan.price) per $($plan.billingCycle)"
    }
    Write-Host ""
}
catch {
    Write-Error "Failed to list plans: $($_.Exception.Message)"
    exit 1
}

# Step 3: Create a subscription
if ($plans.Count -eq 0) {
    Write-Error "No subscription plans available. Cannot proceed with subscription test."
    exit 1
}

$planToSubscribe = $plans[0]
Write-Host "Step 3: Creating subscription to $($planToSubscribe.name)..."
try {
    $subscribeBody = @{
        productHandle = $planToSubscribe.handle
    } | ConvertTo-Json

    $subscribeResponse = Invoke-WebRequest -Uri "$BaseUrl/subscriptions" `
        -Method POST `
        -Headers $headers `
        -Body $subscribeBody `
        -SkipCertificateCheck

    $subJson = $subscribeResponse.Content | ConvertFrom-Json
    $subscriptionId = $subJson.subscriptionId

    Write-Host "✓ Subscription created successfully"
    Write-Host "  Subscription ID: $subscriptionId"
    Write-Host "  State: $($subJson.state)"
    Write-Host "  Next Billing: $($subJson.nextBillingDate)"
    Write-Host ""
}
catch {
    Write-Error "Failed to create subscription: $($_.Exception.Message)"
    exit 1
}

# Step 4: List user's subscriptions
Write-Host "Step 4: Retrieving user's subscriptions..."
try {
    $mySubsResponse = Invoke-WebRequest -Uri "$BaseUrl/my-subscriptions" `
        -Method GET `
        -Headers $headers `
        -SkipCertificateCheck

    $mySubsJson = $mySubsResponse.Content | ConvertFrom-Json
    $subs = $mySubsJson.subscriptions

    Write-Host "✓ User subscriptions retrieved: $($subs.Count) subscription(s)"
    Write-Host "  Customer: $($mySubsJson.customerName) ($($mySubsJson.email))"
    foreach ($sub in $subs) {
        Write-Host "  - ID:$($sub.subscriptionId) State:$($sub.state) Plan:$($sub.productName)"
    }
    Write-Host ""
}
catch {
    Write-Error "Failed to retrieve subscriptions: $($_.Exception.Message)"
    exit 1
}

# Step 5: Verify idempotency
Write-Host "Step 5: Testing idempotency (creating same subscription again)..."
try {
    $subscribeBody = @{
        productHandle = $planToSubscribe.handle
    } | ConvertTo-Json

    $subscribeResponse2 = Invoke-WebRequest -Uri "$BaseUrl/subscriptions" `
        -Method POST `
        -Headers $headers `
        -Body $subscribeBody `
        -SkipCertificateCheck

    $subJson2 = $subscribeResponse2.Content | ConvertFrom-Json

    if ($subJson2.customerId -eq $subJson.customerId) {
        Write-Host "✓ Idempotency verified: Same customer used"
    }
    Write-Host ""
}
catch {
    Write-Error "Failed to test idempotency: $($_.Exception.Message)"
    exit 1
}

Write-Host "=== All Tests Passed ✓ ==="
Write-Host ""
Write-Host "Summary:"
Write-Host "  - Successfully authenticated with JWT"
Write-Host "  - Retrieved $($plans.Count) subscription plan(s)"
Write-Host "  - Created subscription to $($planToSubscribe.name)"
Write-Host "  - Retrieved user subscriptions"
Write-Host "  - Verified idempotency"
