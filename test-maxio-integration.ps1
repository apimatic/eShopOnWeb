# Maxio Integration Test Script
# Tests the subscription billing endpoints

param(
    [string]$ApiUrl = "https://localhost:28243",
    [string]$Username = "demouser",
    [string]$Password = "Pass@word1",
    [string]$PlanHandle = "eshop-pro"
)

# Suppress certificate validation warnings for local testing
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ️  $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Error-Custom {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Maxio Integration Verification" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Get Available Plans
Write-Info "Test 1: Getting available subscription plans..."
try {
    $plansResponse = Invoke-RestMethod -Uri "$ApiUrl/api/subscription-plans" `
        -Method Get `
        -Headers @{"Accept" = "application/json"} `
        -SkipCertificateCheck

    if ($plansResponse.plans.Count -gt 0) {
        Write-Success "Found $($plansResponse.plans.Count) subscription plans"
        foreach ($plan in $plansResponse.plans) {
            Write-Host "  - $($plan.name) ($($plan.handle)): `$$($plan.price)/month" -ForegroundColor Gray
        }
    } else {
        Write-Error-Custom "No plans found. Check Maxio configuration."
        exit 1
    }
} catch {
    Write-Error-Custom "Failed to get plans: $_"
    exit 1
}

Write-Host ""

# Test 2: Authenticate
Write-Info "Test 2: Authenticating user '$Username'..."
try {
    $authResponse = Invoke-RestMethod -Uri "$ApiUrl/api/authenticate" `
        -Method Post `
        -Headers @{"Content-Type" = "application/json"} `
        -Body (@{username = $Username; password = $Password} | ConvertTo-Json) `
        -SkipCertificateCheck

    if ($authResponse.result -eq $true) {
        Write-Success "Authentication successful"
        $token = $authResponse.token
    } else {
        Write-Error-Custom "Authentication failed: $($authResponse | ConvertTo-Json)"
        exit 1
    }
} catch {
    Write-Error-Custom "Failed to authenticate: $_"
    exit 1
}

Write-Host ""

# Test 3: Create Subscription
Write-Info "Test 3: Creating subscription to plan '$PlanHandle'..."
try {
    $subResponse = Invoke-RestMethod -Uri "$ApiUrl/api/subscriptions" `
        -Method Post `
        -Headers @{
            "Authorization" = "Bearer $token"
            "Content-Type" = "application/json"
        } `
        -Body (@{planHandle = $PlanHandle} | ConvertTo-Json) `
        -SkipCertificateCheck

    if ($subResponse.subscription.id -gt 0) {
        Write-Success "Subscription created successfully"
        Write-Host "  - ID: $($subResponse.subscription.id)" -ForegroundColor Gray
        Write-Host "  - Plan: $($subResponse.subscription.productName)" -ForegroundColor Gray
        Write-Host "  - State: $($subResponse.subscription.state)" -ForegroundColor Gray
        Write-Host "  - Next Billing: $($subResponse.subscription.nextAssessmentAt)" -ForegroundColor Gray
        $subscriptionId = $subResponse.subscription.id
    } else {
        Write-Error-Custom "Subscription creation failed: $($subResponse | ConvertTo-Json)"
        exit 1
    }
} catch {
    Write-Error-Custom "Failed to create subscription: $_"
    exit 1
}

Write-Host ""

# Test 4: Get User Subscriptions
Write-Info "Test 4: Retrieving user subscriptions..."
try {
    $mySubsResponse = Invoke-RestMethod -Uri "$ApiUrl/api/my-subscriptions" `
        -Method Get `
        -Headers @{"Authorization" = "Bearer $token"} `
        -SkipCertificateCheck

    if ($mySubsResponse.subscriptions.Count -gt 0) {
        Write-Success "Found $($mySubsResponse.subscriptions.Count) active subscription(s)"
        foreach ($sub in $mySubsResponse.subscriptions) {
            Write-Host "  - $($sub.productName) (ID: $($sub.id), State: $($sub.state))" -ForegroundColor Gray
        }
    } else {
        Write-Error-Custom "No subscriptions found for user"
        exit 1
    }
} catch {
    Write-Error-Custom "Failed to get user subscriptions: $_"
    exit 1
}

Write-Host ""

# Test 5: Verify Subscription Details
Write-Info "Test 5: Verifying subscription was created correctly..."
if ($mySubsResponse.subscriptions | Where-Object { $_.id -eq $subscriptionId }) {
    Write-Success "Subscription verified in user's subscription list"
} else {
    Write-Error-Custom "Subscription not found in user's list"
    exit 1
}

Write-Host ""
Write-Host "================================" -ForegroundColor Green
Write-Host "✓ All tests passed!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green
Write-Host ""
Write-Host "Integration Status: WORKING" -ForegroundColor Green
