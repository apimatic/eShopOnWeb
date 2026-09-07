# Maxio Subscription Integration Verification Script
# This script verifies the subscription integration is properly set up

Write-Host "=== Maxio Subscription Integration Verification ===" -ForegroundColor Cyan

# Check 1: Environment Variables
Write-Host "`n[1/4] Checking Maxio Configuration..." -ForegroundColor Yellow
$apiKey = $env:MAXIO_API_KEY
$subdomain = $env:MAXIO_SITE_SUBDOMAIN
$productFamily = $env:MAXIO_DEFAULT_PRODUCT_FAMILY

if ([string]::IsNullOrEmpty($apiKey)) {
    Write-Host "  ❌ MAXIO_API_KEY is not set" -ForegroundColor Red
    Write-Host "     Set it with: set MAXIO_API_KEY=<your-api-key>"
} else {
    Write-Host "  ✓ MAXIO_API_KEY is configured" -ForegroundColor Green
}

if ([string]::IsNullOrEmpty($subdomain)) {
    Write-Host "  ⚠ MAXIO_SITE_SUBDOMAIN not set (default: cp-exp-4)" -ForegroundColor Yellow
} else {
    Write-Host "  ✓ MAXIO_SITE_SUBDOMAIN: $subdomain" -ForegroundColor Green
}

if ([string]::IsNullOrEmpty($productFamily)) {
    Write-Host "  ⚠ MAXIO_DEFAULT_PRODUCT_FAMILY not set (default: eshop-subscribe)" -ForegroundColor Yellow
} else {
    Write-Host "  ✓ MAXIO_DEFAULT_PRODUCT_FAMILY: $productFamily" -ForegroundColor Green
}

# Check 2: Build Status
Write-Host "`n[2/4] Verifying Build..." -ForegroundColor Yellow
Push-Location "src/PublicApi"
$buildOutput = dotnet build 2>&1
Pop-Location

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ PublicApi builds successfully" -ForegroundColor Green
} else {
    Write-Host "  ❌ Build failed" -ForegroundColor Red
    Write-Host $buildOutput | Select-Object -Last 20
    exit 1
}

# Check 3: Endpoints Exist
Write-Host "`n[3/4] Verifying Endpoints..." -ForegroundColor Yellow
$endpointsFile = "src/PublicApi/SubscriptionEndpoints"
if (Test-Path $endpointsFile) {
    Write-Host "  ✓ SubscriptionEndpoints directory exists" -ForegroundColor Green
    $files = @(
        "ListSubscriptionPlansEndpoint.cs",
        "CreateSubscriptionEndpoint.cs",
        "ListMySubscriptionsEndpoint.cs"
    )
    foreach ($file in $files) {
        if (Test-Path "$endpointsFile/$file") {
            Write-Host "    ✓ $file" -ForegroundColor Green
        } else {
            Write-Host "    ❌ $file missing" -ForegroundColor Red
        }
    }
} else {
    Write-Host "  ❌ SubscriptionEndpoints directory not found" -ForegroundColor Red
}

# Check 4: Database Entities
Write-Host "`n[4/4] Verifying Database Entities..." -ForegroundColor Yellow
$entitiesFile = "src/ApplicationCore/Entities/SubscriptionAggregate"
if (Test-Path $entitiesFile) {
    Write-Host "  ✓ SubscriptionAggregate directory exists" -ForegroundColor Green
    $entities = @(
        "MaxioCustomer.cs",
        "Subscription.cs"
    )
    foreach ($entity in $entities) {
        if (Test-Path "$entitiesFile/$entity") {
            Write-Host "    ✓ $entity" -ForegroundColor Green
        } else {
            Write-Host "    ❌ $entity missing" -ForegroundColor Red
        }
    }
} else {
    Write-Host "  ❌ SubscriptionAggregate directory not found" -ForegroundColor Red
}

Write-Host "`n=== Verification Complete ===" -ForegroundColor Cyan
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Set MAXIO_API_KEY environment variable with your Maxio sandbox API key"
Write-Host "2. Run: cd src/PublicApi && dotnet run"
Write-Host "3. Visit: https://localhost:28483/swagger"
Write-Host "`nFor detailed setup instructions, see: SUBSCRIPTION_SETUP.md"
