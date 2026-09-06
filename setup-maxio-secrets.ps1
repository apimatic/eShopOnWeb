# Maxio Subscription Integration - User Secrets Setup Script
# This script configures user-secrets for local development with Maxio credentials

param(
    [Parameter(Mandatory=$true, HelpMessage="Maxio API Key")]
    [string]$ApiKey,

    [Parameter(Mandatory=$true, HelpMessage="Maxio Site Subdomain (e.g., 'my-site')")]
    [string]$Subdomain,

    [Parameter(Mandatory=$false, HelpMessage="Product Family Handle (default: eshop-subscribe)")]
    [string]$ProductFamilyHandle = "eshop-subscribe",

    [Parameter(Mandatory=$false, HelpMessage="Base URL override (optional)")]
    [string]$BaseUrl = $null
)

Write-Host "================================" -ForegroundColor Cyan
Write-Host "Maxio Subscription Secrets Setup" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Navigate to PublicApi directory
$publicApiPath = "src/PublicApi"
if (-not (Test-Path $publicApiPath)) {
    Write-Host "ERROR: PublicApi directory not found at $publicApiPath" -ForegroundColor Red
    Write-Host "Please run this script from the repository root directory" -ForegroundColor Red
    exit 1
}

Write-Host "Changing to PublicApi directory..." -ForegroundColor Yellow
Push-Location $publicApiPath

try {
    # Initialize user-secrets if not already done
    Write-Host "Initializing user-secrets..." -ForegroundColor Yellow
    & dotnet user-secrets init --force 2>$null

    if ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 1) {
        Write-Host "✓ User-secrets initialized (or already initialized)" -ForegroundColor Green
    } else {
        Write-Host "ERROR: Failed to initialize user-secrets" -ForegroundColor Red
        exit 1
    }

    Write-Host ""
    Write-Host "Configuring Maxio settings..." -ForegroundColor Yellow

    # Set Maxio credentials
    $settings = @(
        @{
            Key   = "Maxio:ApiKey"
            Value = $ApiKey
            Label = "API Key"
        },
        @{
            Key   = "Maxio:Subdomain"
            Value = $Subdomain
            Label = "Subdomain"
        },
        @{
            Key   = "Maxio:ProductFamilyHandle"
            Value = $ProductFamilyHandle
            Label = "Product Family Handle"
        }
    )

    if ($BaseUrl) {
        $settings += @{
            Key   = "Maxio:BaseUrl"
            Value = $BaseUrl
            Label = "Base URL"
        }
    }

    foreach ($setting in $settings) {
        Write-Host "  Setting {0}..." -ForegroundColor Gray -f $setting.Label
        & dotnet user-secrets set $setting.Key $setting.Value | Out-Null

        if ($LASTEXITCODE -eq 0) {
            Write-Host "    ✓ {0} configured" -ForegroundColor Green -f $setting.Label
        } else {
            Write-Host "    ERROR: Failed to set {0}" -ForegroundColor Red -f $setting.Label
            exit 1
        }
    }

    Write-Host ""
    Write-Host "Verifying configuration..." -ForegroundColor Yellow

    # List secrets to verify
    $secrets = & dotnet user-secrets list

    Write-Host ""
    Write-Host "Current user-secrets configuration:" -ForegroundColor Cyan
    Write-Host $secrets

    Write-Host ""
    Write-Host "================================" -ForegroundColor Green
    Write-Host "✓ Setup Complete!" -ForegroundColor Green
    Write-Host "================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Cyan
    Write-Host "1. Build the solution: dotnet build eShopOnWeb.sln -c Debug" -ForegroundColor White
    Write-Host "2. Run PublicApi: dotnet run --environment Development" -ForegroundColor White
    Write-Host "3. Test endpoints at: https://localhost:27403/swagger" -ForegroundColor White
    Write-Host ""
    Write-Host "For detailed testing instructions, see SUBSCRIPTION_SETUP.md" -ForegroundColor Cyan

} finally {
    Pop-Location
}
