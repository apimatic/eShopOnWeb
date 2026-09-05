# Setup script for Maxio secrets in user-secrets store
# Run from repository root: .\scripts\setup-maxio-secrets.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$ApiKey = "",

    [Parameter(Mandatory=$false)]
    [string]$Subdomain = "",

    [Parameter(Mandatory=$false)]
    [string]$ProductFamilyHandle = "eshop-subscribe",

    [Parameter(Mandatory=$false)]
    [string]$BaseUrl = ""
)

$PublicApiPath = "src/PublicApi"

Write-Host "Setting up Maxio secrets for eShopOnWeb PublicApi..." -ForegroundColor Green

if (-not (Test-Path $PublicApiPath)) {
    Write-Error "PublicApi project not found at $PublicApiPath"
    exit 1
}

Push-Location $PublicApiPath

try {
    # Initialize user secrets if not already done
    dotnet user-secrets init 2>$null

    if ($ApiKey) {
        dotnet user-secrets set "Maxio:ApiKey" $ApiKey
        Write-Host "✓ Set Maxio:ApiKey" -ForegroundColor Green
    } else {
        Write-Host "⚠ Skipped Maxio:ApiKey (not provided)" -ForegroundColor Yellow
        Write-Host "  Provide -ApiKey parameter to set it"
    }

    if ($Subdomain) {
        dotnet user-secrets set "Maxio:Subdomain" $Subdomain
        Write-Host "✓ Set Maxio:Subdomain to $Subdomain" -ForegroundColor Green
    } else {
        Write-Host "⚠ Skipped Maxio:Subdomain (not provided)" -ForegroundColor Yellow
        Write-Host "  Provide -Subdomain parameter to set it"
    }

    dotnet user-secrets set "Maxio:ProductFamilyHandle" $ProductFamilyHandle
    Write-Host "✓ Set Maxio:ProductFamilyHandle to $ProductFamilyHandle" -ForegroundColor Green

    if ($BaseUrl) {
        dotnet user-secrets set "Maxio:BaseUrl" $BaseUrl
        Write-Host "✓ Set Maxio:BaseUrl to $BaseUrl" -ForegroundColor Green
    } else {
        dotnet user-secrets set "Maxio:BaseUrl" ""
        Write-Host "✓ Set Maxio:BaseUrl to empty (will auto-derive from subdomain)" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Secrets configured! Current values:" -ForegroundColor Green
    dotnet user-secrets list | Select-Object -Skip 1

    Write-Host ""
    Write-Host "Setup complete! You can now run 'dotnet run' to start the PublicApi." -ForegroundColor Green
}
finally {
    Pop-Location
}
