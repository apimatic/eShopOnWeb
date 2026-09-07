# Setup Maxio Configuration for eShopOnWeb PublicApi
# This script configures user secrets for Maxio subscription billing integration

$projectPath = "$PSScriptRoot\src\PublicApi"
$projectFile = "$projectPath\PublicApi.csproj"

# Change to project directory
Push-Location $projectPath

try {
    # Initialize user-secrets for this project if not already initialized
    dotnet user-secrets init

    # Read environment variables and set user secrets
    $apiKey = $env:MAXIO_API_KEY
    $subdomain = $env:MAXIO_SITE_SUBDOMAIN
    $productFamily = $env:MAXIO_DEFAULT_PRODUCT_FAMILY
    $baseUrl = $env:MAXIO_BASE_URL

    if ([string]::IsNullOrEmpty($apiKey)) {
        Write-Error "MAXIO_API_KEY environment variable is not set"
        exit 1
    }

    if ([string]::IsNullOrEmpty($subdomain)) {
        Write-Error "MAXIO_SITE_SUBDOMAIN environment variable is not set"
        exit 1
    }

    if ([string]::IsNullOrEmpty($productFamily)) {
        Write-Error "MAXIO_DEFAULT_PRODUCT_FAMILY environment variable is not set"
        exit 1
    }

    # Set user secrets
    Write-Host "Setting Maxio user secrets..."
    dotnet user-secrets set "Maxio:ApiKey" $apiKey
    dotnet user-secrets set "Maxio:Subdomain" $subdomain
    dotnet user-secrets set "Maxio:ProductFamilyHandle" $productFamily

    if (-not [string]::IsNullOrEmpty($baseUrl)) {
        dotnet user-secrets set "Maxio:BaseUrl" $baseUrl
    }

    Write-Host "User secrets configured successfully"
    Write-Host "Maxio settings:"
    Write-Host "  - Subdomain: $subdomain"
    Write-Host "  - ProductFamily: $productFamily"
    if (-not [string]::IsNullOrEmpty($baseUrl)) {
        Write-Host "  - BaseUrl: $baseUrl"
    }
}
finally {
    Pop-Location
}
