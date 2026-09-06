#!/usr/bin/env pwsh
<#
.SYNOPSIS
Test script for the Maxio subscription integration endpoints

.DESCRIPTION
This script tests the complete subscription flow:
1. Authenticate to get JWT token
2. List subscription plans
3. Create a subscription
4. List user's subscriptions

.PARAMETER BaseUrl
The base URL of the PublicApi (default: https://localhost:25603)

.PARAMETER Username
Username for authentication (default: demouser@example.com)

.PARAMETER Password
Password for authentication (default: Pass@word1)

.PARAMETER ProductHandle
Product handle to subscribe to (default: eshop-pro)
#>

param(
    [string]$BaseUrl = "https://localhost:25603",
    [string]$Username = "demouser@example.com",
    [string]$Password = "Pass@word1",
    [string]$ProductHandle = "eshop-pro"
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

# Ignore certificate errors for localhost development
if ($BaseUrl -like "https://localhost*") {
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
}

try {
    Write-Step "Step 1: Authenticate"
    $authResponse = Invoke-RestMethod -Uri "$BaseUrl/api/authenticate" `
        -Method Post `
        -ContentType "application/json" `
        -Body (ConvertTo-Json @{
            username = $Username
            password = $Password
        })

    $token = $authResponse.token
    if (-not $token) {
        throw "Failed to obtain JWT token"
    }
    Write-Success "Authentication successful"
    Write-Host "Token obtained: $($token.Substring(0, 20))..."

    Write-Step "Step 2: List Subscription Plans"
    $headers = @{}
    $plansResponse = Invoke-RestMethod -Uri "$BaseUrl/api/subscription-plans" `
        -Method Get `
        -Headers $headers

    $plans = $plansResponse.plans
    if (-not $plans -or $plans.Count -eq 0) {
        throw "No subscription plans found"
    }
    Write-Success "Retrieved subscription plans"
    $plans | ForEach-Object {
        Write-Host "  - $($_.name) ($($_.handle)): `$$($_.price)/month" -ForegroundColor Gray
    }

    $selectedPlan = $plans | Where-Object { $_.handle -eq $ProductHandle } | Select-Object -First 1
    if (-not $selectedPlan) {
        Write-Host "  Note: Plan '$ProductHandle' not found. Using first plan instead." -ForegroundColor Yellow
        $selectedPlan = $plans[0]
    }
    Write-Host "  Selected plan: $($selectedPlan.name) ($($selectedPlan.handle))" -ForegroundColor Cyan

    Write-Step "Step 3: Create Subscription"
    $headers = @{
        "Authorization" = "Bearer $token"
    }
    $createResponse = Invoke-RestMethod -Uri "$BaseUrl/api/subscriptions" `
        -Method Post `
        -ContentType "application/json" `
        -Headers $headers `
        -Body (ConvertTo-Json @{
            productHandle = $selectedPlan.handle
        })

    if (-not $createResponse.id) {
        throw "Failed to create subscription"
    }
    Write-Success "Subscription created successfully"
    Write-Host "  Subscription ID: $($createResponse.id)" -ForegroundColor Gray
    Write-Host "  State: $($createResponse.state)" -ForegroundColor Gray
    Write-Host "  Product: $($createResponse.productName)" -ForegroundColor Gray
    Write-Host "  Price: `$$($createResponse.pricePerBillingCycle)/$($createResponse.billingInterval)" -ForegroundColor Gray
    Write-Host "  Next Billing: $($createResponse.nextAssessmentAt)" -ForegroundColor Gray

    Write-Step "Step 4: List My Subscriptions"
    $headers = @{
        "Authorization" = "Bearer $token"
    }
    $mySubsResponse = Invoke-RestMethod -Uri "$BaseUrl/api/my-subscriptions" `
        -Method Get `
        -Headers $headers

    $subs = $mySubsResponse.subscriptions
    Write-Success "Retrieved user's subscriptions"
    if ($subs.Count -eq 0) {
        Write-Host "  No subscriptions found" -ForegroundColor Yellow
    } else {
        Write-Host "  Found $($subs.Count) subscription(s):" -ForegroundColor Gray
        $subs | ForEach-Object {
            Write-Host "    - ID: $($_.id), State: $($_.state), Product: $($_.productName)" -ForegroundColor Gray
        }
    }

    Write-Step "Test Complete"
    Write-Success "All subscription endpoints working correctly!"
    Write-Host "`nSummary:" -ForegroundColor Cyan
    Write-Host "  • Plans available: $($plans.Count)" -ForegroundColor Gray
    Write-Host "  • User subscriptions: $($subs.Count)" -ForegroundColor Gray
    Write-Host "  • Latest subscription: $($createResponse.id) ($($createResponse.state))" -ForegroundColor Gray
}
catch {
    Write-Step "Error Occurred"
    Write-Error $_.Exception.Message
    if ($_.Exception.Response) {
        Write-Host "Response Status: $($_.Exception.Response.StatusCode)" -ForegroundColor Gray
        try {
            $errorContent = $_.Exception.Response.Content.ReadAsStringAsync().Result
            Write-Host "Response Body: $errorContent" -ForegroundColor Gray
        }
        catch {}
    }
    exit 1
}
