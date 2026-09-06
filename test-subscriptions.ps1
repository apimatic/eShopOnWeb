#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test script for Maxio subscription billing integration endpoints.

.DESCRIPTION
    This script tests the subscription endpoints by:
    1. Authenticating with demo credentials
    2. Retrieving available plans
    3. Creating a subscription
    4. Retrieving user's subscriptions

.EXAMPLE
    ./test-subscriptions.ps1 -ApiUrl "https://localhost:25883" -PlanHandle "eshop-pro"
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$ApiUrl = "https://localhost:25883",

    [Parameter(Mandatory=$false)]
    [string]$PlanHandle = "eshop-pro",

    [Parameter(Mandatory=$false)]
    [string]$Username = "demouser@microsoft.com",

    [Parameter(Mandatory=$false)]
    [string]$Password = "Pass@word1"
)

$ErrorActionPreference = "Stop"

# Disable certificate validation for self-signed certs
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

Write-Host "=== Maxio Subscription Integration Test ===" -ForegroundColor Cyan
Write-Host "API URL: $ApiUrl" -ForegroundColor Gray
Write-Host ""

# Test 1: Authenticate
Write-Host "Step 1: Authenticate" -ForegroundColor Yellow
try {
    $authPayload = @{
        username = $Username
        password = $Password
    } | ConvertTo-Json

    $authResponse = Invoke-WebRequest -Uri "$ApiUrl/api/authenticate" `
        -Method POST `
        -ContentType "application/json" `
        -Body $authPayload `
        -SkipCertificateCheck

    $authData = $authResponse.Content | ConvertFrom-Json

    if ($authData.result -eq $true) {
        Write-Host "✓ Authentication successful" -ForegroundColor Green
        $token = $authData.token
        Write-Host "Token: $($token.Substring(0, 20))..." -ForegroundColor Gray
    } else {
        Write-Host "✗ Authentication failed" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "✗ Authentication error: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Test 2: Get Plans
Write-Host "Step 2: Retrieve Subscription Plans" -ForegroundColor Yellow
try {
    $plansResponse = Invoke-WebRequest -Uri "$ApiUrl/api/subscription-plans" `
        -Method GET `
        -Headers @{ "Authorization" = "Bearer $token" } `
        -SkipCertificateCheck

    $plansData = $plansResponse.Content | ConvertFrom-Json

    Write-Host "✓ Retrieved plans" -ForegroundColor Green

    if ($plansData.plans -and $plansData.plans.Count -gt 0) {
        Write-Host "Found $($plansData.plans.Count) plan(s):" -ForegroundColor Gray
        foreach ($plan in $plansData.plans) {
            Write-Host "  - $($plan.name) ($($plan.handle)): `$$($plan.price)/$($plan.intervalUnit)" -ForegroundColor Gray
        }

        # Use first available plan if specified plan not found
        $targetPlan = $plansData.plans | Where-Object { $_.handle -eq $PlanHandle } | Select-Object -First 1
        if (-not $targetPlan) {
            Write-Host "Plan '$PlanHandle' not found, using first available: $($plansData.plans[0].handle)" -ForegroundColor Yellow
            $targetPlan = $plansData.plans[0]
        }
        $PlanHandle = $targetPlan.handle
    } else {
        Write-Host "✗ No plans returned" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "✗ Error retrieving plans: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Test 3: Create Subscription
Write-Host "Step 3: Create Subscription" -ForegroundColor Yellow
try {
    $subscriptionPayload = @{
        planHandle = $PlanHandle
    } | ConvertTo-Json

    $subResponse = Invoke-WebRequest -Uri "$ApiUrl/api/subscriptions" `
        -Method POST `
        -ContentType "application/json" `
        -Headers @{ "Authorization" = "Bearer $token" } `
        -Body $subscriptionPayload `
        -SkipCertificateCheck

    $subData = $subResponse.Content | ConvertFrom-Json

    if ($subData.subscription) {
        Write-Host "✓ Subscription created successfully" -ForegroundColor Green
        $subscription = $subData.subscription
        Write-Host "  ID: $($subscription.id)" -ForegroundColor Gray
        Write-Host "  State: $($subscription.state)" -ForegroundColor Gray
        Write-Host "  Product: $($subscription.productName)" -ForegroundColor Gray
        Write-Host "  Price: `$$($subscription.price)" -ForegroundColor Gray
        Write-Host "  Next Billing: $($subscription.nextBillingDate)" -ForegroundColor Gray
    } else {
        Write-Host "✗ Subscription creation failed" -ForegroundColor Red
        Write-Host $subResponse.Content -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "✗ Error creating subscription: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Test 4: Get User's Subscriptions
Write-Host "Step 4: Retrieve User's Subscriptions" -ForegroundColor Yellow
try {
    $mySubsResponse = Invoke-WebRequest -Uri "$ApiUrl/api/my-subscriptions" `
        -Method GET `
        -Headers @{ "Authorization" = "Bearer $token" } `
        -SkipCertificateCheck

    $mySubsData = $mySubsResponse.Content | ConvertFrom-Json

    Write-Host "✓ Retrieved subscriptions" -ForegroundColor Green

    if ($mySubsData.subscriptions -and $mySubsData.subscriptions.Count -gt 0) {
        Write-Host "User has $($mySubsData.subscriptions.Count) subscription(s):" -ForegroundColor Gray
        foreach ($sub in $mySubsData.subscriptions) {
            Write-Host "  - ID: $($sub.id) | State: $($sub.state) | Product: $($sub.productName)" -ForegroundColor Gray
        }

        # Verify the subscription we just created is in the list
        $foundSub = $mySubsData.subscriptions | Where-Object { $_.id -eq $subscription.id }
        if ($foundSub) {
            Write-Host "✓ Newly created subscription found in user's subscriptions" -ForegroundColor Green
        }
    } else {
        Write-Host "⚠ No subscriptions returned (this may be expected if Maxio is not configured)" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "✗ Error retrieving subscriptions: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== All Tests Completed ===" -ForegroundColor Cyan
Write-Host "If you see this message without errors, the subscription integration is working!" -ForegroundColor Green
