# PowerShell script to test the subscription API endpoints
# Requires the PublicApi to be running on https://localhost:28623

param(
    [string]$ApiUrl = "https://localhost:28623",
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "Pass@word1",
    [switch]$SkipCertificateCheck = $true
)

# Skip certificate validation for self-signed certs
if ($SkipCertificateCheck) {
    if (-not ([System.Management.Automation.PSTypeName]'ServerCertificateValidationCallback').Type) {
        $certCallback = @"
        using System;
        using System.Net;
        using System.Net.Security;
        using System.Security.Cryptography.X509Certificates;
        public class ServerCertificateValidationCallback
        {
            public static void Ignore()
            {
                if(ServicePointManager.ServerCertificateValidationCallback == null)
                {
                    ServicePointManager.ServerCertificateValidationCallback +=
                        delegate (
                            Object obj,
                            X509Certificate certificate,
                            X509Chain chain,
                            SslPolicyErrors errors
                        ) {
                            return true;
                        };
                }
            }
        }
"@
        Add-Type $certCallback
    }
    [ServerCertificateValidationCallback]::Ignore()
}

Write-Host "Testing Subscription API Endpoints" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Authenticate
Write-Host "Step 1: Authenticating..." -ForegroundColor Green
$authPayload = @{
    username = $Username
    password = $Password
} | ConvertTo-Json

try {
    $authResponse = Invoke-RestMethod -Uri "$ApiUrl/api/authenticate" `
        -Method Post `
        -ContentType "application/json" `
        -Body $authPayload

    $token = $authResponse.token
    Write-Host "✓ Authentication successful!" -ForegroundColor Green
    Write-Host "Token: $($token.Substring(0, 20))..." -ForegroundColor Gray
    Write-Host ""
} catch {
    Write-Host "✗ Authentication failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

# Step 2: List subscription plans
Write-Host "Step 2: Listing subscription plans..." -ForegroundColor Green
try {
    $plansResponse = Invoke-RestMethod -Uri "$ApiUrl/api/subscription-plans" `
        -Method Get `
        -Headers $headers

    if ($plansResponse.plans -and $plansResponse.plans.Count -gt 0) {
        Write-Host "✓ Retrieved plans successfully!" -ForegroundColor Green
        Write-Host ""
        foreach ($plan in $plansResponse.plans) {
            Write-Host "  - $($plan.name) ($($plan.handle))" -ForegroundColor Cyan
            Write-Host "    Price: `$$($plan.price)/$($plan.intervalUnit)" -ForegroundColor Gray
            Write-Host "    ID: $($plan.id)" -ForegroundColor Gray
        }
        Write-Host ""

        # Store first plan for subscription test
        $testPlanHandle = $plansResponse.plans[0].handle
    } else {
        Write-Host "✗ No plans found!" -ForegroundColor Yellow
        Write-Host "Make sure Maxio is configured with the product family handle." -ForegroundColor Yellow
        exit 1
    }
} catch {
    Write-Host "✗ Failed to list plans!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Step 3: Create a subscription
Write-Host "Step 3: Creating subscription to $testPlanHandle..." -ForegroundColor Green
$subscriptionPayload = @{
    planHandle = $testPlanHandle
} | ConvertTo-Json

try {
    $subResponse = Invoke-RestMethod -Uri "$ApiUrl/api/subscriptions" `
        -Method Post `
        -Headers $headers `
        -Body $subscriptionPayload

    Write-Host "✓ Subscription created successfully!" -ForegroundColor Green
    Write-Host "  Subscription ID: $($subResponse.subscriptionId)" -ForegroundColor Cyan
    Write-Host "  State: $($subResponse.state)" -ForegroundColor Cyan
    Write-Host "  Product: $($subResponse.productName)" -ForegroundColor Cyan
    Write-Host "  Price: `$$($subResponse.price)" -ForegroundColor Cyan
    Write-Host "  Next Assessment: $($subResponse.nextAssessmentAt)" -ForegroundColor Cyan
    Write-Host ""
} catch {
    Write-Host "✗ Failed to create subscription!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red

    # Try to get more details from response
    if ($_.Exception.Response -ne $null) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response: $responseBody" -ForegroundColor Gray
    }
    exit 1
}

# Step 4: Get user's subscriptions
Write-Host "Step 4: Retrieving user's subscriptions..." -ForegroundColor Green
try {
    $mySubsResponse = Invoke-RestMethod -Uri "$ApiUrl/api/my-subscriptions" `
        -Method Get `
        -Headers $headers

    Write-Host "✓ Retrieved subscriptions successfully!" -ForegroundColor Green
    Write-Host "  Count: $($mySubsResponse.subscriptions.Count)" -ForegroundColor Cyan
    Write-Host ""

    foreach ($sub in $mySubsResponse.subscriptions) {
        Write-Host "  Subscription: $($sub.subscriptionId)" -ForegroundColor Cyan
        Write-Host "    Product: $($sub.productName)" -ForegroundColor Gray
        Write-Host "    State: $($sub.state)" -ForegroundColor Gray
        Write-Host "    Price: `$$($sub.price)" -ForegroundColor Gray
        Write-Host "    Active Since: $($sub.activatedAt)" -ForegroundColor Gray
        Write-Host "    Next Billing: $($sub.nextAssessmentAt)" -ForegroundColor Gray
    }
    Write-Host ""
} catch {
    Write-Host "✗ Failed to retrieve subscriptions!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "=================================" -ForegroundColor Cyan
Write-Host "All tests passed! ✓" -ForegroundColor Green
Write-Host ""
Write-Host "The subscription API is working correctly." -ForegroundColor Green
