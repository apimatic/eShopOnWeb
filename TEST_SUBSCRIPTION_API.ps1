# Test script for Maxio Subscription API
# Prerequisites:
#   - PublicApi is running on https://localhost:24703
#   - Environment variables are set or user-secrets are configured
#   - A user is logged in (use /api/authenticate first)

param(
    [string]$BaseUrl = "https://localhost:24703",
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word1"
)

# Disable certificate verification for self-signed certificates
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Maxio Subscription API Test Suite" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Step 1: Authenticate and get token
Write-Host ""
Write-Host "[1/5] Authenticating user..." -ForegroundColor Yellow

$authResponse = @{
    username = $Username
    password = $Password
} | ConvertTo-Json

try {
    $authResult = Invoke-RestMethod -Uri "$BaseUrl/api/authenticate" `
        -Method Post `
        -ContentType "application/json" `
        -Body $authResponse `
        -ErrorAction Stop

    $token = $authResult.token
    Write-Host "✓ Authentication successful" -ForegroundColor Green
    Write-Host "  Token: $($token.Substring(0, 20))..." -ForegroundColor Gray
} catch {
    Write-Host "✗ Authentication failed: $_" -ForegroundColor Red
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
}

# Step 2: List subscription plans
Write-Host ""
Write-Host "[2/5] Listing subscription plans..." -ForegroundColor Yellow

try {
    $plansResult = Invoke-RestMethod -Uri "$BaseUrl/api/subscription-plans" `
        -Method Get `
        -Headers $headers `
        -ErrorAction Stop

    if ($plansResult.plans -and $plansResult.plans.Count -gt 0) {
        Write-Host "✓ Retrieved $($plansResult.plans.Count) plans" -ForegroundColor Green
        foreach ($plan in $plansResult.plans) {
            Write-Host "  - $($plan.name) ($($plan.handle)): `$$($plan.price)/month" -ForegroundColor Gray
        }
    } else {
        Write-Host "⚠ No plans found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "✗ Failed to list plans: $_" -ForegroundColor Red
    exit 1
}

# Step 3: Create a subscription
Write-Host ""
Write-Host "[3/5] Creating subscription..." -ForegroundColor Yellow

$subscriptionRequest = @{
    productHandle = "eshop-pro"
} | ConvertTo-Json

try {
    $subscriptionResult = Invoke-RestMethod -Uri "$BaseUrl/api/subscriptions" `
        -Method Post `
        -Headers $headers `
        -ContentType "application/json" `
        -Body $subscriptionRequest `
        -ErrorAction Stop

    $subscriptionId = $subscriptionResult.subscriptionId
    Write-Host "✓ Subscription created successfully" -ForegroundColor Green
    Write-Host "  ID: $subscriptionId" -ForegroundColor Gray
    Write-Host "  State: $($subscriptionResult.state)" -ForegroundColor Gray
    Write-Host "  Product: $($subscriptionResult.productName)" -ForegroundColor Gray
    Write-Host "  Price: `$$($subscriptionResult.monthlyPrice)/month" -ForegroundColor Gray
    Write-Host "  Next Billing: $($subscriptionResult.nextBillingDate)" -ForegroundColor Gray
    Write-Host "  Message: $($subscriptionResult.message)" -ForegroundColor Gray
} catch {
    Write-Host "✗ Failed to create subscription: $_" -ForegroundColor Red
    Write-Host "  Response: $($_.Exception.Response.Content)" -ForegroundColor Gray
}

# Step 4: List user's subscriptions
Write-Host ""
Write-Host "[4/5] Listing user's subscriptions..." -ForegroundColor Yellow

try {
    $mySubsResult = Invoke-RestMethod -Uri "$BaseUrl/api/my-subscriptions" `
        -Method Get `
        -Headers $headers `
        -ErrorAction Stop

    if ($mySubsResult.subscriptions -and $mySubsResult.subscriptions.Count -gt 0) {
        Write-Host "✓ Retrieved $($mySubsResult.subscriptions.Count) subscriptions" -ForegroundColor Green
        foreach ($sub in $mySubsResult.subscriptions) {
            Write-Host "  - [$($sub.id)] $($sub.productName) - State: $($sub.state)" -ForegroundColor Gray
            Write-Host "    Next Billing: $($sub.nextBillingDate)" -ForegroundColor Gray
        }
    } else {
        Write-Host "⚠ No subscriptions found" -ForegroundColor Yellow
    }
} catch {
    Write-Host "✗ Failed to list subscriptions: $_" -ForegroundColor Red
}

# Step 5: Test error handling - try without authorization
Write-Host ""
Write-Host "[5/5] Testing authorization enforcement..." -ForegroundColor Yellow

try {
    $noAuthResult = Invoke-RestMethod -Uri "$BaseUrl/api/subscription-plans" `
        -Method Get `
        -ErrorAction Stop

    Write-Host "⚠ Authorization was not enforced!" -ForegroundColor Yellow
} catch [System.Net.Http.HttpRequestException] {
    if ($_.Exception.Response.StatusCode -eq 401) {
        Write-Host "✓ Authorization properly enforced (401 Unauthorized)" -ForegroundColor Green
    } else {
        Write-Host "⚠ Unexpected status code: $($_.Exception.Response.StatusCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "⚠ Error during authorization test: $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Test Suite Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
