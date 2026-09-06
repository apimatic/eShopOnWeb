#!/usr/bin/env pwsh
<#
.SYNOPSIS
Tests the Maxio subscription billing integration for eShopOnWeb

.DESCRIPTION
This script verifies that the Maxio integration is working correctly by:
1. Authenticating a user
2. Listing subscription plans
3. Creating a subscription
4. Retrieving user subscriptions

.PARAMETER ApiUrl
The base URL of the PublicApi application (default: https://localhost:25843)

.PARAMETER SkipCertificateCheck
Skip HTTPS certificate verification (useful for dev/self-signed certs)

.EXAMPLE
./test-maxio-integration.ps1 -SkipCertificateCheck
#>

param(
    [string]$ApiUrl = "https://localhost:25843",
    [switch]$SkipCertificateCheck = $true,
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word123",
    [string]$PlanHandle = "eshop-pro"
)

# Disable certificate verification if requested
if ($SkipCertificateCheck) {
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Maxio Subscription Integration Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Authenticate
Write-Host "[1/4] Authenticating user..." -ForegroundColor Yellow
try {
    $authResponse = Invoke-RestMethod -Uri "$ApiUrl/api/authenticate" `
        -Method Post `
        -Body (@{
            username = $Username
            password = $Password
        } | ConvertTo-Json) `
        -ContentType "application/json" `
        -SkipCertificateCheck:$SkipCertificateCheck

    if (-not $authResponse.result) {
        Write-Host "Authentication failed!" -ForegroundColor Red
        Write-Host "Response: $($authResponse | ConvertTo-Json)"
        exit 1
    }

    $token = $authResponse.token
    Write-Host "✓ Successfully authenticated" -ForegroundColor Green
    Write-Host "  Token: $($token.Substring(0, 20))..." -ForegroundColor Gray
}
catch {
    Write-Host "✗ Authentication failed: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Test 2: List Subscription Plans
Write-Host "[2/4] Listing subscription plans..." -ForegroundColor Yellow
try {
    $plansResponse = Invoke-RestMethod -Uri "$ApiUrl/api/subscription-plans" `
        -Method Get `
        -Headers @{ Authorization = "Bearer $token" } `
        -SkipCertificateCheck:$SkipCertificateCheck

    if (-not $plansResponse.success) {
        Write-Host "Failed to list plans!" -ForegroundColor Red
        Write-Host "Response: $($plansResponse | ConvertTo-Json)"
        exit 1
    }

    Write-Host "✓ Successfully retrieved subscription plans" -ForegroundColor Green
    $plansResponse.plans | ForEach-Object {
        Write-Host "  - $($_.handle): $($_.name) - \$$($_.price)/mo" -ForegroundColor Gray
    }

    # Verify our test plan exists
    if (-not ($plansResponse.plans | Where-Object { $_.handle -eq $PlanHandle })) {
        Write-Host "Warning: Test plan '$PlanHandle' not found in available plans" -ForegroundColor Yellow
        $PlanHandle = $plansResponse.plans[0].handle
        Write-Host "Using first available plan instead: $PlanHandle" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "✗ Failed to list plans: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Test 3: Create Subscription
Write-Host "[3/4] Creating subscription to plan '$PlanHandle'..." -ForegroundColor Yellow
try {
    $createSubResponse = Invoke-RestMethod -Uri "$ApiUrl/api/subscriptions" `
        -Method Post `
        -Headers @{ Authorization = "Bearer $token" } `
        -Body (@{
            planHandle = $PlanHandle
        } | ConvertTo-Json) `
        -ContentType "application/json" `
        -SkipCertificateCheck:$SkipCertificateCheck

    if (-not $createSubResponse.success) {
        Write-Host "Failed to create subscription!" -ForegroundColor Red
        Write-Host "Error: $($createSubResponse.errorMessage)" -ForegroundColor Red
        exit 1
    }

    Write-Host "✓ Successfully created subscription" -ForegroundColor Green
    Write-Host "  Subscription ID: $($createSubResponse.subscriptionId)" -ForegroundColor Gray
    Write-Host "  Plan: $($createSubResponse.planHandle)" -ForegroundColor Gray
    Write-Host "  State: $($createSubResponse.state)" -ForegroundColor Gray
    Write-Host "  Price: \$$($createSubResponse.currentPrice)/mo" -ForegroundColor Gray
    if ($createSubResponse.nextBillingAt) {
        Write-Host "  Next Billing: $($createSubResponse.nextBillingAt)" -ForegroundColor Gray
    }
}
catch {
    Write-Host "✗ Failed to create subscription: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Test 4: Get User Subscriptions
Write-Host "[4/4] Retrieving user subscriptions..." -ForegroundColor Yellow
try {
    $userSubsResponse = Invoke-RestMethod -Uri "$ApiUrl/api/my-subscriptions" `
        -Method Get `
        -Headers @{ Authorization = "Bearer $token" } `
        -SkipCertificateCheck:$SkipCertificateCheck

    if (-not $userSubsResponse.success) {
        Write-Host "Failed to retrieve subscriptions!" -ForegroundColor Red
        Write-Host "Response: $($userSubsResponse | ConvertTo-Json)"
        exit 1
    }

    Write-Host "✓ Successfully retrieved user subscriptions" -ForegroundColor Green
    Write-Host "  Found $($userSubsResponse.subscriptions.Count) subscription(s):" -ForegroundColor Gray
    $userSubsResponse.subscriptions | ForEach-Object {
        Write-Host "    - ID: $($_.id)" -ForegroundColor Gray
        Write-Host "      Plan: $($_.planHandle)" -ForegroundColor Gray
        Write-Host "      State: $($_.state)" -ForegroundColor Gray
        Write-Host "      Price: \$$($_.currentPrice)/mo" -ForegroundColor Gray
        if ($_.nextBillingAt) {
            Write-Host "      Next Billing: $($_.nextBillingAt)" -ForegroundColor Gray
        }
    }
}
catch {
    Write-Host "✗ Failed to retrieve subscriptions: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "✓ All tests passed!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Integration is working correctly." -ForegroundColor Green
Write-Host "Maxio subscription billing has been successfully integrated into eShopOnWeb." -ForegroundColor Green
