#!/usr/bin/env pwsh
param(
    [string]$BaseUrl = "https://localhost:28283",
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word123"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "eShopOnWeb Subscription Integration Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Authenticate
Write-Host "[1/5] Authenticating user..." -ForegroundColor Yellow
try {
    $authBody = @{
        username = $Username
        password = $Password
    } | ConvertTo-Json

    $authResponse = Invoke-WebRequest `
        -Uri "$BaseUrl/api/authenticate" `
        -Method POST `
        -ContentType "application/json" `
        -Body $authBody `
        -SkipCertificateCheck `
        -ErrorAction Stop

    $authData = $authResponse.Content | ConvertFrom-Json
    $token = $authData.token

    if (-not $token) {
        throw "No token in response"
    }

    Write-Host "✓ Authentication successful" -ForegroundColor Green
    Write-Host "  Token: $($token.Substring(0, 20))..." -ForegroundColor Gray
}
catch {
    Write-Host "✗ Authentication failed: $_" -ForegroundColor Red
    exit 1
}

# Set up headers
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

# Test 2: List subscription plans
Write-Host "[2/5] Listing subscription plans..." -ForegroundColor Yellow
try {
    $plansResponse = Invoke-WebRequest `
        -Uri "$BaseUrl/api/subscription-plans" `
        -Method GET `
        -Headers $headers `
        -SkipCertificateCheck `
        -ErrorAction Stop

    $plansData = $plansResponse.Content | ConvertFrom-Json
    $plans = $plansData.plans

    if ($plans.Count -eq 0) {
        throw "No plans returned"
    }

    Write-Host "✓ Plans retrieved successfully" -ForegroundColor Green
    Write-Host "  Found $($plans.Count) plans:" -ForegroundColor Gray
    foreach ($plan in $plans) {
        Write-Host "    - $($plan.name) ($($plan.handle)): \$$($plan.priceInDollars)/month" -ForegroundColor Gray
    }

    $firstPlan = $plans[0]
}
catch {
    Write-Host "✗ Failed to list plans: $_" -ForegroundColor Red
    exit 1
}

# Test 3: Create subscription
Write-Host "[3/5] Creating subscription to '$($firstPlan.name)'..." -ForegroundColor Yellow
try {
    $subBody = @{
        productHandle = $firstPlan.handle
    } | ConvertTo-Json

    $subResponse = Invoke-WebRequest `
        -Uri "$BaseUrl/api/subscriptions" `
        -Method POST `
        -Headers $headers `
        -Body $subBody `
        -SkipCertificateCheck `
        -ErrorAction Stop

    $subData = $subResponse.Content | ConvertFrom-Json
    $subscription = $subData.subscription

    Write-Host "✓ Subscription created successfully" -ForegroundColor Green
    Write-Host "  ID: $($subscription.id)" -ForegroundColor Gray
    Write-Host "  State: $($subscription.state)" -ForegroundColor Gray
    Write-Host "  Plan: $($subscription.productName)" -ForegroundColor Gray
    Write-Host "  Price: \$$($subscription.productPriceInDollars)/month" -ForegroundColor Gray
    Write-Host "  Next Billing: $($subscription.currentPeriodEndsAt)" -ForegroundColor Gray
}
catch {
    Write-Host "✗ Failed to create subscription: $_" -ForegroundColor Red
    exit 1
}

# Test 4: List user subscriptions
Write-Host "[4/5] Listing user's subscriptions..." -ForegroundColor Yellow
try {
    $mySubsResponse = Invoke-WebRequest `
        -Uri "$BaseUrl/api/my-subscriptions" `
        -Method GET `
        -Headers $headers `
        -SkipCertificateCheck `
        -ErrorAction Stop

    $mySubsData = $mySubsResponse.Content | ConvertFrom-Json
    $userSubs = $mySubsData.subscriptions

    Write-Host "✓ User subscriptions retrieved successfully" -ForegroundColor Green
    Write-Host "  Found $($userSubs.Count) subscription(s):" -ForegroundColor Gray
    foreach ($sub in $userSubs) {
        Write-Host "    - $($sub.productName) ($($sub.state)): \$$($sub.productPriceInDollars)/month" -ForegroundColor Gray
    }
}
catch {
    Write-Host "✗ Failed to list subscriptions: $_" -ForegroundColor Red
    exit 1
}

# Test 5: Verify authorization requirement
Write-Host "[5/5] Verifying authorization requirements..." -ForegroundColor Yellow
try {
    $unauthorizedResponse = Invoke-WebRequest `
        -Uri "$BaseUrl/api/subscription-plans" `
        -Method GET `
        -SkipCertificateCheck `
        -ErrorAction Stop

    Write-Host "✗ Authorization check failed - endpoint allowed unauthorized access!" -ForegroundColor Red
    exit 1
}
catch {
    if ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host "✓ Authorization requirement verified" -ForegroundColor Green
        Write-Host "  Endpoints properly require JWT authentication" -ForegroundColor Gray
    } else {
        Write-Host "✗ Unexpected error: $_" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "All tests passed! ✓" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Summary:" -ForegroundColor Cyan
Write-Host "✓ Authentication working" -ForegroundColor Green
Write-Host "✓ Plan listing functional" -ForegroundColor Green
Write-Host "✓ Subscription creation working" -ForegroundColor Green
Write-Host "✓ User subscription retrieval functional" -ForegroundColor Green
Write-Host "✓ JWT authorization enforced" -ForegroundColor Green
