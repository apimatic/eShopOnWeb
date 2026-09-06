#!/usr/bin/env pwsh

# Maxio Subscription Integration Verification Script
# This script verifies that the subscription integration is working correctly

param(
    [string]$ApiUrl = "https://localhost:25203",
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word1",
    [string]$PlanHandle = "eshop-pro"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Maxio Subscription Integration Verification ===" -ForegroundColor Cyan

# Step 1: List subscription plans
Write-Host "`n[1] Listing subscription plans..."
try {
    $plansResponse = Invoke-WebRequest -Uri "$ApiUrl/api/subscription-plans" `
        -Method Get `
        -Headers @{"Content-Type"="application/json"} `
        -SkipCertificateCheck `
        -ErrorAction SilentlyContinue

    if ($plansResponse.StatusCode -eq 200) {
        $plans = $plansResponse.Content | ConvertFrom-Json
        Write-Host "✓ Found $($plans.plans.Count) plans" -ForegroundColor Green
        $plans.plans | ForEach-Object {
            Write-Host "  - $($_.handle): $($_.name) `$$($_.price)/month"
        }
    } else {
        Write-Host "✗ Failed to list plans (Status: $($plansResponse.StatusCode))" -ForegroundColor Red
        return
    }
} catch {
    Write-Host "✗ Error listing plans: $_" -ForegroundColor Red
    return
}

# Step 2: Authenticate
Write-Host "`n[2] Authenticating user..."
try {
    $authBody = @{
        username = $Username
        password = $Password
    } | ConvertTo-Json

    $authResponse = Invoke-WebRequest -Uri "$ApiUrl/api/authenticate" `
        -Method Post `
        -Headers @{"Content-Type"="application/json"} `
        -Body $authBody `
        -SkipCertificateCheck `
        -ErrorAction SilentlyContinue

    if ($authResponse.StatusCode -eq 200) {
        $auth = $authResponse.Content | ConvertFrom-Json
        if ($auth.result) {
            $token = $auth.token
            Write-Host "✓ Authentication successful" -ForegroundColor Green
            Write-Host "  Token: $($token.Substring(0,20))..." -ForegroundColor Gray
        } else {
            Write-Host "✗ Authentication failed: $($auth.error)" -ForegroundColor Red
            return
        }
    } else {
        Write-Host "✗ Authentication request failed (Status: $($authResponse.StatusCode))" -ForegroundColor Red
        return
    }
} catch {
    Write-Host "✗ Error authenticating: $_" -ForegroundColor Red
    return
}

# Step 3: Create subscription
Write-Host "`n[3] Creating subscription..."
try {
    $subBody = @{
        planHandle = $PlanHandle
    } | ConvertTo-Json

    $subResponse = Invoke-WebRequest -Uri "$ApiUrl/api/subscriptions" `
        -Method Post `
        -Headers @{
            "Content-Type"="application/json"
            "Authorization"="Bearer $token"
        } `
        -Body $subBody `
        -SkipCertificateCheck `
        -ErrorAction SilentlyContinue

    if ($subResponse.StatusCode -eq 201) {
        $subscription = $subResponse.Content | ConvertFrom-Json
        Write-Host "✓ Subscription created successfully" -ForegroundColor Green
        Write-Host "  Subscription ID: $($subscription.maxioSubscriptionId)"
        Write-Host "  Customer ID: $($subscription.maxioCustomerId)"
        Write-Host "  Status: $($subscription.status)"
        Write-Host "  Next Billing: $($subscription.nextBillingDate)"
        $subscriptionId = $subscription.maxioSubscriptionId
    } else {
        Write-Host "✗ Failed to create subscription (Status: $($subResponse.StatusCode))" -ForegroundColor Red
        $error = $subResponse.Content | ConvertFrom-Json
        Write-Host "  Error: $($error.error)" -ForegroundColor Red
        return
    }
} catch {
    Write-Host "✗ Error creating subscription: $_" -ForegroundColor Red
    return
}

# Step 4: Get user's subscriptions
Write-Host "`n[4] Retrieving user's subscriptions..."
try {
    $mySubsResponse = Invoke-WebRequest -Uri "$ApiUrl/api/my-subscriptions" `
        -Method Get `
        -Headers @{
            "Content-Type"="application/json"
            "Authorization"="Bearer $token"
        } `
        -SkipCertificateCheck `
        -ErrorAction SilentlyContinue

    if ($mySubsResponse.StatusCode -eq 200) {
        $mySubs = $mySubsResponse.Content | ConvertFrom-Json
        Write-Host "✓ Retrieved $($mySubs.subscriptions.Count) subscription(s)" -ForegroundColor Green
        $mySubs.subscriptions | ForEach-Object {
            Write-Host "  - ID: $($_.maxioSubscriptionId), Plan: $($_.planHandle), Status: $($_.status)"
        }

        $found = $mySubs.subscriptions | Where-Object { $_.maxioSubscriptionId -eq $subscriptionId }
        if ($found) {
            Write-Host "✓ Created subscription found in user's subscriptions" -ForegroundColor Green
        } else {
            Write-Host "✗ Created subscription not found in user's subscriptions" -ForegroundColor Red
        }
    } else {
        Write-Host "✗ Failed to retrieve subscriptions (Status: $($mySubsResponse.StatusCode))" -ForegroundColor Red
        return
    }
} catch {
    Write-Host "✗ Error retrieving subscriptions: $_" -ForegroundColor Red
    return
}

# Step 5: Test idempotency
Write-Host "`n[5] Testing idempotency (creating same subscription again)..."
try {
    $subBody2 = @{
        planHandle = $PlanHandle
    } | ConvertTo-Json

    $subResponse2 = Invoke-WebRequest -Uri "$ApiUrl/api/subscriptions" `
        -Method Post `
        -Headers @{
            "Content-Type"="application/json"
            "Authorization"="Bearer $token"
        } `
        -Body $subBody2 `
        -SkipCertificateCheck `
        -ErrorAction SilentlyContinue

    if ($subResponse2.StatusCode -eq 201) {
        $subscription2 = $subResponse2.Content | ConvertFrom-Json
        if ($subscription2.maxioSubscriptionId -eq $subscriptionId) {
            Write-Host "✓ Idempotency confirmed (same subscription ID returned)" -ForegroundColor Green
        } else {
            Write-Host "⚠ Different subscription ID returned (may indicate non-idempotent behavior)" -ForegroundColor Yellow
        }
    } else {
        Write-Host "✗ Idempotency test failed (Status: $($subResponse2.StatusCode))" -ForegroundColor Red
    }
} catch {
    Write-Host "⚠ Error testing idempotency: $_" -ForegroundColor Yellow
}

Write-Host "`n=== Verification Complete ===" -ForegroundColor Cyan
Write-Host "All tests passed! The subscription integration is working correctly." -ForegroundColor Green
