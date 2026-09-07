# PowerShell script to set up user secrets for Maxio integration
# Usage: .\setup-secrets.ps1 -ApiKey "your-api-key" -Subdomain "your-subdomain"

param(
    [Parameter(Mandatory=$true)]
    [string]$ApiKey,

    [Parameter(Mandatory=$true)]
    [string]$Subdomain,

    [string]$Environment = "sandbox",
    [string]$ProductFamilyHandle = "eshop-subscribe"
)

# Navigate to PublicApi project
$publicApiPath = Join-Path $PSScriptRoot "src/PublicApi"
Push-Location $publicApiPath

Write-Host "Setting up user secrets for Maxio integration..." -ForegroundColor Green

# Initialize user secrets if not already done
dotnet user-secrets init

# Set Maxio configuration
Write-Host "Setting Maxio:ApiKey..." -ForegroundColor Cyan
dotnet user-secrets set "Maxio:ApiKey" $ApiKey

Write-Host "Setting Maxio:Subdomain..." -ForegroundColor Cyan
dotnet user-secrets set "Maxio:Subdomain" $Subdomain

Write-Host "Setting Maxio:Environment..." -ForegroundColor Cyan
dotnet user-secrets set "Maxio:Environment" $Environment

Write-Host "Setting Maxio:ProductFamilyHandle..." -ForegroundColor Cyan
dotnet user-secrets set "Maxio:ProductFamilyHandle" $ProductFamilyHandle

# Also set environment variables for convenience
$env:MAXIO_API_KEY = $ApiKey
$env:MAXIO_SITE_SUBDOMAIN = $Subdomain
$env:MAXIO_ENVIRONMENT = $Environment
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = $ProductFamilyHandle

Write-Host "User secrets configured successfully!" -ForegroundColor Green
Write-Host "Environment variables also set for this session." -ForegroundColor Green

Pop-Location
