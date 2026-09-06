#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Verification script for Maxio subscription billing integration
.DESCRIPTION
    Tests all three subscription endpoints: list plans, create subscription, view subscriptions
.NOTES
    Requires: .NET SDK, valid Maxio sandbox credentials in user secrets
#>

param(
    [string]$ApiBase = "https://localhost:25163",
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word123",
    [string]$PlanHandle = "eshop-pro"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

Write-Host "=== Maxio Subscription Integration Verification ===" -ForegroundColor Cyan
Write-Host "API Base: $ApiBase" -ForegroundColor Gray
Write-Host ""

# Helper function for API calls
function Invoke-ApiCall {
    param(
        [string]$Method,
        [string]$Endpoint,
        [object]$Body = $null,
        [string]$Token = $null
    )

    $uri = "$ApiBase$Endpoint"
    $headers = @{ "Content-Type" = "application/json" }

    if ($Token) {
        $headers["Authorization"] = "Bearer $Token"
    }

    $params = @{
        Uri = $uri
        Method = $Method
        Headers = $headers
        SkipCertificateCheck = $true
    }

    if ($Body) {
        $params["Body"] = $Body | ConvertTo-Json
    }

    try {
        $response = Invoke-WebRequest @params
        return $response.Content | ConvertFrom-Json
    }
    catch {
        Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.Exception.Response) {
            Write-Host "Response: $($_.Exception.Response.StatusCode)" -ForegroundColor Red
        }
        throw
    }
}

# Step 1: List subscription plans
Write-Host "Step 1: List Available Plans" -ForegroundColor Yellow
try {
    $plans = Invoke-ApiCall -Method GET -Endpoint "/api/subscription-plans"
    Write-Host "✅ Got $(($plans.plans).Count) plans" -ForegroundColor Green
    $plans.plans | ForEach-Object {
        Write-Host "  - $($_.name): $($_.priceFormatted)/month" -ForegroundColor Gray
    }
}
catch {
    Write-Host "❌ Failed to list plans" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Step 2: Authenticate
Write-Host "Step 2: Get Authentication Token" -ForegroundColor Yellow
try {
    $authBody = @{ username = $Username; password = $Password }
    $authResponse = Invoke-ApiCall -Method POST -Endpoint "/api/authenticate" -Body $authBody

    if (-not $authResponse.token) {
        throw "No token in response"
    }

    $token = $authResponse.token
    Write-Host "✅ Authenticated as: $($authResponse.username)" -ForegroundColor Green
}
catch {
    Write-Host "❌ Authentication failed. Ensure credentials are correct." -ForegroundColor Red
    Write-Host "   Default: demouser@microsoft.com / Pass@word123" -ForegroundColor Gray
    exit 1
}

Write-Host ""

# Step 3: Create subscription
Write-Host "Step 3: Create Subscription" -ForegroundColor Yellow
try {
    $subBody = @{ planHandle = $PlanHandle }
    $subscription = Invoke-ApiCall -Method POST `
        -Endpoint "/api/subscriptions" `
        -Body $subBody `
        -Token $token

    Write-Host "✅ Subscription created:" -ForegroundColor Green
    Write-Host "   ID: $($subscription.subscription.id)" -ForegroundColor Gray
    Write-Host "   Plan: $($subscription.subscription.planName)" -ForegroundColor Gray
    Write-Host "   Status: $($subscription.subscription.status)" -ForegroundColor Gray
    Write-Host "   Price: $($subscription.subscription.priceFormatted)" -ForegroundColor Gray
    Write-Host "   Next Billing: $($subscription.subscription.nextBillingAt)" -ForegroundColor Gray
}
catch {
    Write-Host "❌ Failed to create subscription" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Step 4: Get user subscriptions
Write-Host "Step 4: Get User's Subscriptions" -ForegroundColor Yellow
try {
    $mySubscriptions = Invoke-ApiCall -Method GET `
        -Endpoint "/api/my-subscriptions" `
        -Token $token

    Write-Host "✅ Retrieved $(($mySubscriptions.subscriptions).Count) subscription(s):" -ForegroundColor Green
    $mySubscriptions.subscriptions | ForEach-Object {
        Write-Host "   - $($_.planName) ($($_.status)): $($_.priceFormatted)" -ForegroundColor Gray
    }
}
catch {
    Write-Host "❌ Failed to get subscriptions" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== ✅ All Tests Passed ===" -ForegroundColor Green
Write-Host ""
Write-Host "Integration is working correctly!" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Review MAXIO_SUBSCRIPTION_SETUP.md for detailed configuration" -ForegroundColor Gray
Write-Host "  2. Review IMPLEMENTATION_SUMMARY.md for architecture details" -ForegroundColor Gray
Write-Host "  3. Implement additional features (cancellation, plan changes, webhooks)" -ForegroundColor Gray
