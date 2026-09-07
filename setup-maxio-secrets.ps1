#!/usr/bin/env pwsh

<#
.SYNOPSIS
Sets up Maxio API credentials in .NET user-secrets for the PublicApi project.

.DESCRIPTION
This script reads Maxio credentials from environment variables and stores them
in .NET user-secrets for secure configuration during development.

Required environment variables:
  - MAXIO_API_KEY: The API key for Maxio Advanced Billing
  - MAXIO_SITE_SUBDOMAIN: The subdomain for your Maxio site
  - MAXIO_ENVIRONMENT: The environment (sandbox or production)
  - MAXIO_DEFAULT_PRODUCT_FAMILY: The product family handle for subscriptions

.EXAMPLE
./setup-maxio-secrets.ps1
#>

$ErrorActionPreference = "Stop"

# Navigate to PublicApi project
$publicApiPath = Join-Path (Get-Location) "src" "PublicApi"
if (-not (Test-Path $publicApiPath)) {
    Write-Error "PublicApi project not found at $publicApiPath"
    exit 1
}

# Check if required environment variables are set
$requiredEnvVars = @("MAXIO_API_KEY", "MAXIO_SITE_SUBDOMAIN")
$missingVars = @()

foreach ($var in $requiredEnvVars) {
    if (-not (Test-Path env:$var)) {
        $missingVars += $var
    }
}

if ($missingVars.Count -gt 0) {
    Write-Error "Missing required environment variables: $($missingVars -join ', ')"
    Write-Host ""
    Write-Host "Please set the following environment variables:"
    Write-Host "  MAXIO_API_KEY - Your Maxio API key"
    Write-Host "  MAXIO_SITE_SUBDOMAIN - Your Maxio site subdomain"
    Write-Host "  MAXIO_ENVIRONMENT - sandbox or production (default: sandbox)"
    Write-Host "  MAXIO_DEFAULT_PRODUCT_FAMILY - Product family handle (default: eshop-subscribe)"
    exit 1
}

# Get environment variables with defaults
$apiKey = $env:MAXIO_API_KEY
$subdomain = $env:MAXIO_SITE_SUBDOMAIN
$environment = $env:MAXIO_ENVIRONMENT ?? "sandbox"
$productFamily = $env:MAXIO_DEFAULT_PRODUCT_FAMILY ?? "eshop-subscribe"

Write-Host "Setting up Maxio secrets for PublicApi project..."
Write-Host "  Subdomain: $subdomain"
Write-Host "  Environment: $environment"
Write-Host "  Product Family: $productFamily"
Write-Host ""

# Set the secrets using dotnet user-secrets
Push-Location $publicApiPath
try {
    Write-Host "Setting Maxio:ApiKey..."
    dotnet user-secrets set "Maxio:ApiKey" $apiKey 2>&1 | Write-Host

    Write-Host "Setting Maxio:Subdomain..."
    dotnet user-secrets set "Maxio:Subdomain" $subdomain 2>&1 | Write-Host

    Write-Host "Setting Maxio:Environment..."
    dotnet user-secrets set "Maxio:Environment" $environment 2>&1 | Write-Host

    Write-Host "Setting Maxio:ProductFamilyHandle..."
    dotnet user-secrets set "Maxio:ProductFamilyHandle" $productFamily 2>&1 | Write-Host

    Write-Host ""
    Write-Host "✓ Maxio secrets configured successfully!"
    Write-Host ""
    Write-Host "You can now run the PublicApi project:"
    Write-Host "  dotnet run --project src/PublicApi/PublicApi.csproj"
}
finally {
    Pop-Location
}
