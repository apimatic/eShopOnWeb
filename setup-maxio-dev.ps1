#Requires -Version 7.0

param(
    [Parameter(Mandatory=$true)]
    [string]$ApiKey,

    [Parameter(Mandatory=$false)]
    [string]$Subdomain = "cp-exp-2",

    [Parameter(Mandatory=$false)]
    [string]$Environment = "sandbox",

    [Parameter(Mandatory=$false)]
    [string]$ProductFamily = "eshop-subscribe"
)

Write-Host "Setting up Maxio credentials for eShopOnWeb development..."

$publicApiDir = "./src/PublicApi"

if (-not (Test-Path $publicApiDir)) {
    Write-Error "PublicApi directory not found at $publicApiDir"
    exit 1
}

Write-Host "Configuring Maxio environment..."

# Set user secrets for PublicApi (this stores secrets securely per-user)
Write-Host "Setting secrets in PublicApi project..."
Push-Location $publicApiDir

dotnet user-secrets init --force 2>$null
dotnet user-secrets set "Maxio:ApiKey" $ApiKey
dotnet user-secrets set "Maxio:Subdomain" $Subdomain
dotnet user-secrets set "Maxio:Environment" $Environment
dotnet user-secrets set "Maxio:ProductFamilyHandle" $ProductFamily

Pop-Location

Write-Host "Setup completed! Secrets stored using .NET user-secrets."
Write-Host ""
Write-Host "To run the PublicApi with these settings:"
Write-Host "  cd src/PublicApi"
Write-Host "  set MAXIO_API_KEY=$ApiKey"
Write-Host "  set MAXIO_SITE_SUBDOMAIN=$Subdomain"
Write-Host "  set MAXIO_ENVIRONMENT=$Environment"
Write-Host "  set MAXIO_DEFAULT_PRODUCT_FAMILY=$ProductFamily"
Write-Host "  set UseOnlyInMemoryDatabase=true"
Write-Host "  set DOTNET_ROLL_FORWARD=Major"
Write-Host "  dotnet run"
