# Test script for subscription billing integration
# Run with: .\test-subscriptions.ps1 -BaseUrl "https://localhost:5002" -Token "your-jwt-token"

param(
    [string]$BaseUrl = "https://localhost:5002",
    [string]$Token = ""
)

$ErrorActionPreference = "Stop"

Write-Host "Testing Subscription Billing Integration" -ForegroundColor Cyan
Write-Host "Base URL: $BaseUrl" -ForegroundColor Cyan
Write-Host ""

# Skip SSL verification for localhost testing
$SkipCert = @{
    SkipCertificateCheck = $true
}

# Test 1: Get subscription plans (no auth required)
Write-Host "TEST 1: Get Subscription Plans" -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$BaseUrl/api/subscription-plans" `
        -Method GET `
        -Headers @{ "Accept" = "application/json" } `
        @SkipCert

    $plans = $response.Content | ConvertFrom-Json
    Write-Host "✓ Plans retrieved:" -ForegroundColor Green
    $plans.plans | ForEach-Object {
        Write-Host "  - $($_.name): `$$($_.price)/month ($($_.handle))"
    }
    Write-Host ""
}
catch {
    Write-Host "✗ Failed to get plans: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
}

# Test 2: Create subscription (requires auth)
if ([string]::IsNullOrEmpty($Token)) {
    Write-Host "TEST 2: Create Subscription - SKIPPED (no token provided)" -ForegroundColor Yellow
    Write-Host "  To test: get a token from /api/authenticate and pass with -Token parameter" -ForegroundColor Gray
    Write-Host ""
}
else {
    Write-Host "TEST 2: Create Subscription" -ForegroundColor Yellow
    try {
        $body = @{
            productHandle = "eshop-pro"
        } | ConvertTo-Json

        $response = Invoke-WebRequest -Uri "$BaseUrl/api/subscriptions" `
            -Method POST `
            -Headers @{
                "Authorization" = "Bearer $Token"
                "Content-Type" = "application/json"
            } `
            -Body $body `
            @SkipCert

        $subscription = $response.Content | ConvertFrom-Json
        Write-Host "✓ Subscription created:" -ForegroundColor Green
        Write-Host "  ID: $($subscription.subscriptionId)"
        Write-Host "  State: $($subscription.state)"
        Write-Host "  Price: `$$($subscription.pricePerCycle)/month"
        Write-Host "  Next billing: $($subscription.nextBillingDate)"
        Write-Host ""
    }
    catch {
        Write-Host "✗ Failed to create subscription: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
    }

    # Test 3: Get user subscriptions
    Write-Host "TEST 3: Get My Subscriptions" -ForegroundColor Yellow
    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl/api/my-subscriptions" `
            -Method GET `
            -Headers @{
                "Authorization" = "Bearer $Token"
                "Accept" = "application/json"
            } `
            @SkipCert

        $subs = $response.Content | ConvertFrom-Json
        Write-Host "✓ User subscriptions retrieved: $($subs.subscriptions.Length) found" -ForegroundColor Green
        $subs.subscriptions | ForEach-Object {
            Write-Host "  - $($_.productName) ($($_.state)) - next billing: $($_.nextAssessmentAt)"
        }
        Write-Host ""
    }
    catch {
        Write-Host "✗ Failed to get subscriptions: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host ""
    }
}

Write-Host "Verification complete!" -ForegroundColor Cyan
