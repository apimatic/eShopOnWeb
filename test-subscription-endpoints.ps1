#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test script for Maxio subscription endpoints
.DESCRIPTION
    Automated testing of subscription endpoints with proper error handling
.PARAMETER ApiUrl
    Base URL of the PublicApi service (default: https://localhost:25523)
.PARAMETER Username
    Username for authentication (default: demouser@microsoft.com)
.PARAMETER Password
    Password for authentication (default: Pass@word1)
.PARAMETER ProductHandle
    Product handle for subscription (default: eshop-pro)
#>

param(
    [string]$ApiUrl = "https://localhost:25523",
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word1",
    [string]$ProductHandle = "eshop-pro"
)

$ErrorActionPreference = "Continue"

function Write-TestHeader {
    param([string]$Text)
    Write-Host ""
    Write-Host "=" * 60
    Write-Host "TEST: $Text" -ForegroundColor Cyan
    Write-Host "=" * 60
}

function Write-Success {
    param([string]$Text)
    Write-Host "✅ $Text" -ForegroundColor Green
}

function Write-Error {
    param([string]$Text)
    Write-Host "❌ $Text" -ForegroundColor Red
}

function Write-Info {
    param([string]$Text)
    Write-Host "ℹ️  $Text" -ForegroundColor Blue
}

# Suppress certificate validation for self-signed certs
$PSDefaultParameterValues['Invoke-WebRequest:SkipCertificateCheck'] = $true

Write-Host "Maxio Subscription Endpoints - Test Suite" -ForegroundColor Yellow
Write-Host "=======================================" -ForegroundColor Yellow
Write-Info "API URL: $ApiUrl"
Write-Info "Username: $Username"
Write-Info "Product: $ProductHandle"

# Test 1: Authenticate
Write-TestHeader "Authentication"

try {
    $authPayload = @{
        username = $Username
        password = $Password
    } | ConvertTo-Json

    $authResponse = Invoke-WebRequest -Uri "$ApiUrl/api/authenticate" `
        -Method POST `
        -ContentType "application/json" `
        -Body $authPayload

    $authData = $authResponse.Content | ConvertFrom-Json

    if ($authData.token) {
        $token = $authData.token
        Write-Success "Authentication successful"
        Write-Info "Token: $($token.Substring(0, 20))..."
    }
    else {
        Write-Error "No token in response"
        exit 1
    }
}
catch {
    Write-Error "Authentication failed: $_"
    exit 1
}

# Test 2: List Subscription Plans
Write-TestHeader "List Subscription Plans"

try {
    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
    }

    $plansResponse = Invoke-WebRequest -Uri "$ApiUrl/api/subscription-plans" `
        -Method GET `
        -Headers $headers

    $plansData = $plansResponse.Content | ConvertFrom-Json

    Write-Success "Retrieved subscription plans"

    if ($plansData.plans -and $plansData.plans.Count -gt 0) {
        Write-Info "Available plans:"
        foreach ($plan in $plansData.plans) {
            Write-Host "  - $($plan.name) ($($plan.handle))"
            Write-Host "    Price: `$$($plan.price) $($plan.billingInterval)"
            Write-Host "    ID: $($plan.id)"
        }
    }
    else {
        Write-Info "No plans available (may indicate Maxio API connection issue)"
    }
}
catch {
    Write-Error "Failed to list plans: $_"
    # Don't exit, continue with other tests
}

# Test 3: Create Subscription
Write-TestHeader "Create Subscription"

try {
    $subscriptionPayload = @{
        productHandle = $ProductHandle
    } | ConvertTo-Json

    $createResponse = Invoke-WebRequest -Uri "$ApiUrl/api/subscriptions" `
        -Method POST `
        -Headers $headers `
        -Body $subscriptionPayload

    $subscriptionData = $createResponse.Content | ConvertFrom-Json

    if ($subscriptionData.subscription) {
        $subscription = $subscriptionData.subscription
        Write-Success "Subscription created successfully"
        Write-Info "Subscription ID: $($subscription.id)"
        Write-Info "Customer ID: $($subscription.customerId)"
        Write-Info "State: $($subscription.state)"
        Write-Info "Product: $($subscription.productHandle)"
        Write-Info "Activated: $($subscription.activatedAt)"
        Write-Info "Next Billing: $($subscription.nextAssessmentAt)"
    }
    else {
        Write-Error "Invalid subscription response"
    }
}
catch {
    Write-Error "Failed to create subscription: $_"
    # Don't exit, continue with other tests
}

# Test 4: List User's Subscriptions
Write-TestHeader "List User's Subscriptions"

try {
    $mySubsResponse = Invoke-WebRequest -Uri "$ApiUrl/api/my-subscriptions" `
        -Method GET `
        -Headers $headers

    $mySubsData = $mySubsResponse.Content | ConvertFrom-Json

    Write-Success "Retrieved user's subscriptions"

    if ($mySubsData.subscriptions -and $mySubsData.subscriptions.Count -gt 0) {
        Write-Info "User has $($mySubsData.subscriptions.Count) subscription(s):"
        foreach ($sub in $mySubsData.subscriptions) {
            Write-Host "  - ID: $($sub.id)"
            Write-Host "    Product: $($sub.productHandle)"
            Write-Host "    State: $($sub.state)"
            Write-Host "    Next Billing: $($sub.nextAssessmentAt)"
        }
    }
    else {
        Write-Info "No subscriptions found"
    }
}
catch {
    Write-Error "Failed to list subscriptions: $_"
}

# Summary
Write-Host ""
Write-Host "=" * 60
Write-Host "Test Suite Complete" -ForegroundColor Yellow
Write-Host "=" * 60
Write-Info "For detailed documentation, see MAXIO_INTEGRATION_GUIDE.md"
