#!/usr/bin/env pwsh
<#
.SYNOPSIS
Test script for Maxio subscription integration in eShopOnWeb PublicApi

.DESCRIPTION
This script tests the subscription endpoints after they are running.
Before running this, start the PublicApi with:
    $env:DOTNET_ROLL_FORWARD = "Major"
    $env:UseOnlyInMemoryDatabase = "true"
    $env:MAXIO_API_KEY = "your_api_key"
    $env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"
    $env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
    dotnet run --project src/PublicApi/PublicApi.csproj

.EXAMPLE
./test-subscriptions.ps1 -BaseUrl "https://localhost:27483"
#>

param(
    [string]$BaseUrl = "https://localhost:27483"
)

$ErrorActionPreference = "Continue"

# Suppress certificate validation for self-signed certs in development
if ($PSVersionTable.PSVersion.Major -ge 6) {
    $PSDefaultParameterValues['*:SkipCertificateCheck'] = $true
}

Write-Host "======================================"
Write-Host "Maxio Subscription Integration Tests"
Write-Host "======================================"
Write-Host "Base URL: $BaseUrl"
Write-Host ""

# Test 1: Get subscription plans (no auth required)
Write-Host "Test 1: Get Subscription Plans (no auth)"
Write-Host "GET $BaseUrl/api/subscription-plans"
try {
    $response = Invoke-RestMethod -Uri "$BaseUrl/api/subscription-plans" -Method Get
    Write-Host "✓ Success" -ForegroundColor Green
    Write-Host "Response:"
    Write-Host ($response | ConvertTo-Json -Depth 3)
} catch {
    Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "Test 2: Authenticate and Get JWT Token"
Write-Host "POST $BaseUrl/api/authenticate"
try {
    $authPayload = @{
        username = "demouser@microsoft.com"
        password = "Pass@word1"
    } | ConvertTo-Json

    $authResponse = Invoke-RestMethod -Uri "$BaseUrl/api/authenticate" -Method Post `
        -ContentType "application/json" `
        -Body $authPayload

    if ($authResponse.token) {
        $token = $authResponse.token
        Write-Host "✓ Success - Got JWT token" -ForegroundColor Green
        Write-Host "Token: $($token.Substring(0, 50))..."
        Write-Host "User: $($authResponse.username)"
    } else {
        Write-Host "✗ Failed: No token in response" -ForegroundColor Red
        Write-Host "Response: $($authResponse | ConvertTo-Json)"
        exit 1
    }
} catch {
    Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Test 3: List User's Subscriptions (before subscription)"
Write-Host "GET $BaseUrl/api/my-subscriptions"
try {
    $subResponse = Invoke-RestMethod -Uri "$BaseUrl/api/my-subscriptions" -Method Get `
        -Headers @{ Authorization = "Bearer $token" }

    Write-Host "✓ Success" -ForegroundColor Green
    Write-Host "Response:"
    Write-Host ($subResponse | ConvertTo-Json -Depth 3)
} catch {
    Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "Test 4: Create Subscription"
Write-Host "POST $BaseUrl/api/subscriptions"
try {
    $subPayload = @{
        productHandle = "eshop-pro"
    } | ConvertTo-Json

    $createResponse = Invoke-RestMethod -Uri "$BaseUrl/api/subscriptions" -Method Post `
        -ContentType "application/json" `
        -Headers @{ Authorization = "Bearer $token" } `
        -Body $subPayload

    Write-Host "✓ Success" -ForegroundColor Green
    Write-Host "Response:"
    Write-Host ($createResponse | ConvertTo-Json -Depth 3)

    if ($createResponse.success) {
        Write-Host "Subscription ID: $($createResponse.subscriptionId)" -ForegroundColor Green
        Write-Host "State: $($createResponse.state)"
        Write-Host "Next Billing: $($createResponse.nextBillingDate)"
    }
} catch {
    Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "Test 5: List User's Subscriptions (after subscription)"
Write-Host "GET $BaseUrl/api/my-subscriptions"
try {
    $subResponse = Invoke-RestMethod -Uri "$BaseUrl/api/my-subscriptions" -Method Get `
        -Headers @{ Authorization = "Bearer $token" }

    Write-Host "✓ Success" -ForegroundColor Green
    Write-Host "Response:"
    Write-Host ($subResponse | ConvertTo-Json -Depth 3)
} catch {
    Write-Host "✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "======================================"
Write-Host "Tests Complete"
Write-Host "======================================"
