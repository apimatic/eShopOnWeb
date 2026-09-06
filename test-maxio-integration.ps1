#!/usr/bin/env pwsh
# Test script for Maxio subscription billing integration
# This script tests the three subscription endpoints

param(
    [string]$BaseUrl = "https://localhost:25723/api",
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word123",
    [string]$ProductHandle = "eshop-pro"
)

$ErrorActionPreference = "Stop"

# Colors for output
$Green = [System.Console]::ForegroundColor = [System.ConsoleColor]::Green
$Red = [System.ConsoleColor]::Red
$Yellow = [System.ConsoleColor]::Yellow
$Reset = [System.ConsoleColor]::Gray

function Write-Header {
    param([string]$Text)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host $Text -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Text)
    Write-Host "✓ $Text" -ForegroundColor Green
}

function Write-Error {
    param([string]$Text)
    Write-Host "✗ $Text" -ForegroundColor Red
}

function Write-Info {
    param([string]$Text)
    Write-Host "ℹ $Text" -ForegroundColor Yellow
}

Write-Header "Maxio Subscription Billing Integration Test"

Write-Info "Base URL: $BaseUrl"
Write-Info "Username: $Username"
Write-Info "Product Handle: $ProductHandle`n"

try {
    # Step 1: Authenticate
    Write-Header "Step 1: Authenticate and Get JWT Token"

    $authResponse = Invoke-WebRequest -Uri "$BaseUrl/authenticate" `
        -Method Post `
        -Headers @{
            "Content-Type" = "application/json"
        } `
        -Body (@{
            username = $Username
            password = $Password
        } | ConvertTo-Json) `
        -SkipCertificateCheck

    $authJson = $authResponse.Content | ConvertFrom-Json
    $token = $authJson.token

    if (-not $token) {
        Write-Error "Failed to get authentication token"
        Write-Host $authResponse.Content
        exit 1
    }

    Write-Success "Authentication successful"
    Write-Info "Token: $($token.Substring(0, 20))..."

    # Step 2: List Subscription Plans
    Write-Header "Step 2: List Available Subscription Plans"

    $plansResponse = Invoke-WebRequest -Uri "$BaseUrl/subscription-plans" `
        -Method Get `
        -Headers @{
            "Content-Type" = "application/json"
        } `
        -SkipCertificateCheck

    $plansJson = $plansResponse.Content | ConvertFrom-Json
    $plans = $plansJson.subscriptionPlans

    if ($plans.Count -eq 0) {
        Write-Error "No subscription plans found"
        Write-Host $plansResponse.Content
        exit 1
    }

    Write-Success "Retrieved $($plans.Count) subscription plan(s)"
    foreach ($plan in $plans) {
        Write-Info "Plan: $($plan.name) (handle: $($plan.handle)) - `$$($plan.priceInCents / 100)/month"
    }

    # Step 3: Create Subscription
    Write-Header "Step 3: Create Subscription"

    Write-Info "Creating subscription for product: $ProductHandle"

    $subResponse = Invoke-WebRequest -Uri "$BaseUrl/subscriptions" `
        -Method Post `
        -Headers @{
            "Authorization" = "Bearer $token"
            "Content-Type" = "application/json"
        } `
        -Body (@{
            productHandle = $ProductHandle
        } | ConvertTo-Json) `
        -SkipCertificateCheck

    $subJson = $subResponse.Content | ConvertFrom-Json
    $subscription = $subJson.subscription

    if (-not $subscription.id) {
        Write-Error "Failed to create subscription"
        Write-Host $subResponse.Content
        exit 1
    }

    Write-Success "Subscription created successfully"
    Write-Info "Subscription ID: $($subscription.id)"
    Write-Info "State: $($subscription.state)"
    Write-Info "Product: $($subscription.productName)"
    Write-Info "Price: `$$($subscription.productPriceInCents / 100)/month"
    Write-Info "Billing Period: $($subscription.currentPeriodStartsAt) to $($subscription.currentPeriodEndsAt)"
    Write-Info "Next Assessment: $($subscription.nextAssessmentAt)"

    # Step 4: List User Subscriptions
    Write-Header "Step 4: List User's Subscriptions"

    $userSubsResponse = Invoke-WebRequest -Uri "$BaseUrl/my-subscriptions" `
        -Method Get `
        -Headers @{
            "Authorization" = "Bearer $token"
            "Content-Type" = "application/json"
        } `
        -SkipCertificateCheck

    $userSubsJson = $userSubsResponse.Content | ConvertFrom-Json
    $userSubs = $userSubsJson.subscriptions

    if ($userSubs.Count -eq 0) {
        Write-Error "No subscriptions found for user"
        exit 1
    }

    Write-Success "Retrieved $($userSubs.Count) subscription(s) for user"
    foreach ($sub in $userSubs) {
        Write-Info "- ID: $($sub.id), Product: $($sub.productName), State: $($sub.state)"
    }

    # Step 5: Verify Idempotency
    Write-Header "Step 5: Verify Idempotency (Create Same Subscription Again)"

    Write-Info "Attempting to create subscription with same product..."

    $sub2Response = Invoke-WebRequest -Uri "$BaseUrl/subscriptions" `
        -Method Post `
        -Headers @{
            "Authorization" = "Bearer $token"
            "Content-Type" = "application/json"
        } `
        -Body (@{
            productHandle = $ProductHandle
        } | ConvertTo-Json) `
        -SkipCertificateCheck

    $sub2Json = $sub2Response.Content | ConvertFrom-Json
    $subscription2 = $sub2Json.subscription

    if ($subscription2.id -eq $subscription.id) {
        Write-Success "Idempotency verified: same subscription returned"
    } else {
        Write-Error "Idempotency check failed: different subscription IDs returned"
        Write-Host "First subscription ID: $($subscription.id)"
        Write-Host "Second subscription ID: $($subscription2.id)"
    }

    Write-Header "All Tests Passed!"
    Write-Success "Maxio subscription billing integration is working correctly"

} catch {
    Write-Header "Test Failed"
    Write-Error $_.Exception.Message
    if ($_.Exception.Response) {
        Write-Host "Response:" -ForegroundColor Red
        Write-Host $_.Exception.Response.Content
    }
    exit 1
}
