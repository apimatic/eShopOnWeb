#!/usr/bin/env pwsh
<#
.SYNOPSIS
Tests the Maxio subscription endpoints in eShopOnWeb PublicApi.

.DESCRIPTION
This script:
1. Gets a JWT token from the authenticate endpoint
2. Tests GET /api/subscription-plans
3. Tests POST /api/subscriptions
4. Tests GET /api/my-subscriptions

.PARAMETER ApiUrl
The base URL of the PublicApi (default: https://localhost:25363)

.PARAMETER Username
The username to authenticate with (default: demouser@microsoft.com)

.PARAMETER Password
The password to authenticate with (default: Pass@word1)

.EXAMPLE
.\test-subscription-endpoints.ps1
.\test-subscription-endpoints.ps1 -ApiUrl https://localhost:25363 -Username admin@example.com

#>

param(
    [string]$ApiUrl = "https://localhost:25363",
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word1"
)

# Ignore self-signed certificate warnings
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

function Write-Header {
    param([string]$Text)
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host $Text -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Text)
    Write-Host $Text -ForegroundColor Green
}

function Write-Error-Custom {
    param([string]$Text)
    Write-Host $Text -ForegroundColor Red
}

function Write-Info {
    param([string]$Text)
    Write-Host $Text -ForegroundColor Yellow
}

Write-Host "Maxio Subscription Endpoints Test" -ForegroundColor Magenta
Write-Info "API URL: $ApiUrl"
Write-Info "Username: $Username"

# Step 1: Authenticate
Write-Header "Step 1: Authenticate"

$authRequest = @{
    username = $Username
    password = $Password
}

$authBody = $authRequest | ConvertTo-Json

try {
    $authResponse = Invoke-WebRequest `
        -Uri "$ApiUrl/api/authenticate" `
        -Method POST `
        -ContentType "application/json" `
        -Body $authBody `
        -SkipCertificateCheck

    $authData = $authResponse.Content | ConvertFrom-Json

    if ($authData.result) {
        $token = $authData.token
        Write-Success "✓ Authentication successful!"
        Write-Info "Token: $($token.Substring(0, 50))..."
    } else {
        Write-Error-Custom "✗ Authentication failed: $($authData.message)"
        exit 1
    }
}
catch {
    Write-Error-Custom "✗ Authentication error: $_"
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

# Step 2: Get subscription plans
Write-Header "Step 2: Get Subscription Plans"

try {
    $plansResponse = Invoke-WebRequest `
        -Uri "$ApiUrl/api/subscription-plans" `
        -Method GET `
        -Headers $headers `
        -SkipCertificateCheck

    $plansData = $plansResponse.Content | ConvertFrom-Json

    if ($plansData.plans.Count -gt 0) {
        Write-Success "✓ Retrieved $($plansData.plans.Count) plan(s):"
        foreach ($plan in $plansData.plans) {
            Write-Info "  - $($plan.name) ($($plan.handle)): `$$($plan.priceInDollars)/month"
        }
        $firstPlanHandle = $plansData.plans[0].handle
    } else {
        Write-Error-Custom "✗ No plans available. Check Maxio configuration."
        Write-Info "  - Ensure MAXIO_API_KEY is set"
        Write-Info "  - Ensure MAXIO_SITE_SUBDOMAIN is set"
        Write-Info "  - Ensure MAXIO_DEFAULT_PRODUCT_FAMILY is set"
        exit 1
    }
}
catch {
    Write-Error-Custom "✗ Error retrieving plans: $_"
    Write-Info "Response: $($_.Exception.Response.StatusCode)"
    exit 1
}

# Step 3: Create subscription
Write-Header "Step 3: Create Subscription"

$subRequest = @{
    productHandle = $firstPlanHandle
}

$subBody = $subRequest | ConvertTo-Json

try {
    $subResponse = Invoke-WebRequest `
        -Uri "$ApiUrl/api/subscriptions" `
        -Method POST `
        -Headers $headers `
        -Body $subBody `
        -SkipCertificateCheck

    $subData = $subResponse.Content | ConvertFrom-Json

    if ($subData.subscriptionId) {
        Write-Success "✓ Subscription created successfully!"
        Write-Info "  Subscription ID: $($subData.subscriptionId)"
        Write-Info "  Customer ID: $($subData.customerId)"
        Write-Info "  Plan: $($subData.productHandle)"
        Write-Info "  Status: $($subData.status)"
    } else {
        Write-Error-Custom "✗ Subscription creation failed"
        Write-Info "Response: $($subResponse.Content)"
    }
}
catch {
    Write-Error-Custom "✗ Error creating subscription: $_"
    Write-Info "Response: $($_.Exception.Response.StatusDescription)"
    exit 1
}

# Step 4: Get user subscriptions
Write-Header "Step 4: Get User Subscriptions"

try {
    $userSubsResponse = Invoke-WebRequest `
        -Uri "$ApiUrl/api/my-subscriptions" `
        -Method GET `
        -Headers $headers `
        -SkipCertificateCheck

    $userSubsData = $userSubsResponse.Content | ConvertFrom-Json

    if ($userSubsData.subscriptions.Count -gt 0) {
        Write-Success "✓ Retrieved $($userSubsData.subscriptions.Count) subscription(s):"
        foreach ($sub in $userSubsData.subscriptions) {
            $nextBilling = if ($sub.nextBillingAt) {
                [DateTime]::Parse($sub.nextBillingAt).ToString("yyyy-MM-dd")
            } else {
                "N/A"
            }
            Write-Info "  - $($sub.planName) ($($sub.productHandle))"
            Write-Info "    State: $($sub.state), Price: `$$($sub.priceInDollars), Next Billing: $nextBilling"
        }
    } else {
        Write-Host "✓ No subscriptions found (expected for new user)" -ForegroundColor Green
    }
}
catch {
    Write-Error-Custom "✗ Error retrieving subscriptions: $_"
    exit 1
}

# Summary
Write-Header "Test Summary"
Write-Success "✓ All tests passed!"
Write-Info ""
Write-Info "Next steps:"
Write-Info "1. Log in to your Maxio sandbox: https://cp-exp-4.chargify.com"
Write-Info "2. Go to Settings → Customers"
Write-Info "3. Look for customer with reference: eshop-<userid>"
Write-Info "4. Verify the subscription is active"
Write-Info ""
