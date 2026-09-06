# Test script for Maxio subscription billing integration
# Usage: .\test-subscription-endpoints.ps1 -Username "demouser@microsoft.com" -Password "Pass@word123"

param(
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word123"
)

$ApiBase = "https://localhost:25643"
$ErrorActionPreference = "Stop"

Write-Host "=== Maxio Subscription Integration Test ===" -ForegroundColor Green
Write-Host "API Base: $ApiBase"
Write-Host "Username: $Username"
Write-Host ""

try {
    # Step 1: Authenticate
    Write-Host "[1/4] Authenticating user..." -ForegroundColor Cyan
    $AuthResponse = Invoke-RestMethod -Uri "$ApiBase/api/authenticate" `
        -Method Post `
        -Headers @{ "Content-Type" = "application/json" } `
        -Body @{
            username = $Username
            password = $Password
        } | ConvertTo-Json -Depth 10 | ConvertFrom-Json

    $Token = $AuthResponse.token
    if ([string]::IsNullOrEmpty($Token)) {
        throw "Failed to authenticate: $AuthResponse"
    }
    Write-Host "✓ Got JWT token: $($Token.Substring(0, [Math]::Min(20, $Token.Length)))..." -ForegroundColor Green
    Write-Host ""

    # Step 2: List subscription plans
    Write-Host "[2/4] Listing subscription plans..." -ForegroundColor Cyan
    $PlansResponse = Invoke-RestMethod -Uri "$ApiBase/api/subscription-plans" `
        -Method Get `
        -Headers @{ "Accept" = "application/json" }

    Write-Host "✓ Available plans:" -ForegroundColor Green
    foreach ($plan in $PlansResponse.plans) {
        Write-Host "  $($plan.handle): $($plan.name) - $$($plan.price)/month"
    }
    Write-Host ""

    # Step 3: Create a subscription
    Write-Host "[3/4] Creating subscription to eshop-pro plan..." -ForegroundColor Cyan
    $SubResponse = Invoke-RestMethod -Uri "$ApiBase/api/subscriptions" `
        -Method Post `
        -Headers @{
            "Content-Type" = "application/json"
            "Authorization" = "Bearer $Token"
        } `
        -Body @{
            productHandle = "eshop-pro"
        } | ConvertTo-Json -Depth 10 | ConvertFrom-Json

    $SubscriptionId = $SubResponse.subscription.id
    if ($null -eq $SubscriptionId) {
        throw "Failed to create subscription: $SubResponse"
    }
    Write-Host "✓ Created subscription (ID: $SubscriptionId)" -ForegroundColor Green
    Write-Host "  State: $($SubResponse.subscription.state), Next Billing: $($SubResponse.subscription.nextBillingAt), Price: `$$($SubResponse.subscription.currentPrice)"
    Write-Host ""

    # Step 4: List user's subscriptions
    Write-Host "[4/4] Listing your subscriptions..." -ForegroundColor Cyan
    $SubsResponse = Invoke-RestMethod -Uri "$ApiBase/api/my-subscriptions" `
        -Method Get `
        -Headers @{ "Authorization" = "Bearer $Token" }

    $SubscriptionCount = $SubsResponse.subscriptions.Count
    Write-Host "✓ Found $SubscriptionCount subscription(s):" -ForegroundColor Green
    foreach ($sub in $SubsResponse.subscriptions) {
        Write-Host "  ID: $($sub.id), State: $($sub.state), Price: `$$($sub.currentPrice)"
    }
    Write-Host ""

    Write-Host "=== All tests passed! ===" -ForegroundColor Green
}
catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    exit 1
}
