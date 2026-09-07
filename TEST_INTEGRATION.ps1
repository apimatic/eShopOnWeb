# Test script to verify Maxio subscription integration
# Run this after starting the PublicApi application

$ApiUrl = "https://localhost:28923"
$SkipCertCheck = @{SkipCertificateCheck = $true}

Write-Host "=== Testing Maxio Subscription Integration ===" -ForegroundColor Green

# Test 1: Check application is running
Write-Host "`n1. Checking if API is running..." -ForegroundColor Cyan
try {
    $response = Invoke-WebRequest -Uri "$ApiUrl/swagger/index.html" @SkipCertCheck -ErrorAction Stop
    Write-Host "✓ API is running on $ApiUrl" -ForegroundColor Green
} catch {
    Write-Host "✗ API is not responding. Start the application with: cd src/PublicApi && dotnet run" -ForegroundColor Red
    exit 1
}

# Test 2: List subscription plans
Write-Host "`n2. Testing GET /api/subscription-plans..." -ForegroundColor Cyan
try {
    $response = Invoke-RestMethod -Uri "$ApiUrl/api/subscription-plans" -Method Get @SkipCertCheck
    if ($response.plans -and $response.plans.Count -gt 0) {
        Write-Host "✓ Found $($response.plans.Count) subscription plans:" -ForegroundColor Green
        foreach ($plan in $response.plans) {
            Write-Host "  - $($plan.name) (\$$($plan.price)/month) - Handle: $($plan.handle)" -ForegroundColor Green
        }
    } else {
        Write-Host "✗ No plans returned" -ForegroundColor Red
    }
} catch {
    Write-Host "✗ Failed to get plans: $_" -ForegroundColor Red
}

# Test 3: Authenticate
Write-Host "`n3. Testing authentication with default user..." -ForegroundColor Cyan
$authBody = @{
    username = "demouser@microsoft.com"
    password = "Pass@word`$123"
} | ConvertTo-Json

try {
    $authResponse = Invoke-RestMethod -Uri "$ApiUrl/api/authenticate" -Method Post `
        -ContentType "application/json" -Body $authBody @SkipCertCheck

    if ($authResponse.token) {
        $token = $authResponse.token
        Write-Host "✓ Authentication successful. Token: $($token.Substring(0, 20))..." -ForegroundColor Green
    } else {
        Write-Host "✗ No token in response" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Authentication failed: $_" -ForegroundColor Red
    exit 1
}

# Test 4: Create subscription (this requires Maxio configuration)
Write-Host "`n4. Testing POST /api/subscriptions (requires Maxio config)..." -ForegroundColor Cyan
$subscriptionBody = @{
    productHandle = "eshop-pro"
} | ConvertTo-Json

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

try {
    $subResponse = Invoke-RestMethod -Uri "$ApiUrl/api/subscriptions" -Method Post `
        -Headers $headers -Body $subscriptionBody @SkipCertCheck

    if ($subResponse.subscription.maxioSubscriptionId) {
        Write-Host "✓ Subscription created successfully!" -ForegroundColor Green
        Write-Host "  Maxio Subscription ID: $($subResponse.subscription.maxioSubscriptionId)" -ForegroundColor Green
        Write-Host "  State: $($subResponse.subscription.state)" -ForegroundColor Green
        Write-Host "  Next Billing: $($subResponse.subscription.nextBillingAt)" -ForegroundColor Green
    } else {
        Write-Host "⚠ Endpoint responded but check Maxio config: $($subResponse | ConvertTo-Json)" -ForegroundColor Yellow
    }
} catch {
    $errorMsg = $_.Exception.Message
    if ($errorMsg -Contains "Failed to create/retrieve Maxio customer") {
        Write-Host "⚠ Maxio configuration not set. This is expected if Maxio credentials aren't configured." -ForegroundColor Yellow
        Write-Host "  Set environment variables: MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_DEFAULT_PRODUCT_FAMILY" -ForegroundColor Yellow
    } else {
        Write-Host "✗ Create subscription failed: $errorMsg" -ForegroundColor Red
    }
}

# Test 5: List user subscriptions
Write-Host "`n5. Testing GET /api/my-subscriptions..." -ForegroundColor Cyan
try {
    $userSubsResponse = Invoke-RestMethod -Uri "$ApiUrl/api/my-subscriptions" -Method Get `
        -Headers $headers @SkipCertCheck

    if ($userSubsResponse.subscriptions) {
        Write-Host "✓ User has $($userSubsResponse.subscriptions.Count) subscription(s):" -ForegroundColor Green
        foreach ($sub in $userSubsResponse.subscriptions) {
            Write-Host "  - Plan: $($sub.plan.name), State: $($sub.state)" -ForegroundColor Green
        }
    } else {
        Write-Host "✓ User has no subscriptions (expected if Maxio not configured)" -ForegroundColor Green
    }
} catch {
    Write-Host "✗ Failed to get user subscriptions: $_" -ForegroundColor Red
}

Write-Host "`n=== Integration Tests Complete ===" -ForegroundColor Green
Write-Host "Note: Full subscription tests require Maxio sandbox credentials configured." -ForegroundColor Yellow
