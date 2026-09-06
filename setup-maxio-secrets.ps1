# Setup Maxio Secrets for PublicApi
# This script configures the PublicApi project's user secrets with Maxio credentials from environment variables

# Get the project directory
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$publicApiDir = Join-Path $projectDir "src/PublicApi"

# Change to project directory
Push-Location $publicApiDir

# Initialize user secrets if not already done
dotnet user-secrets init --force

# Configure secrets from environment variables
if ($env:MAXIO_API_KEY) {
    dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY"
    Write-Host "✓ Set Maxio:ApiKey from MAXIO_API_KEY"
}

if ($env:MAXIO_SITE_SUBDOMAIN) {
    dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN"
    Write-Host "✓ Set Maxio:Subdomain from MAXIO_SITE_SUBDOMAIN"
}

if ($env:MAXIO_DEFAULT_PRODUCT_FAMILY) {
    dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY"
    Write-Host "✓ Set Maxio:ProductFamilyHandle from MAXIO_DEFAULT_PRODUCT_FAMILY"
}

if ($env:MAXIO_BASE_URL) {
    dotnet user-secrets set "Maxio:BaseUrl" "$env:MAXIO_BASE_URL"
    Write-Host "✓ Set Maxio:BaseUrl from MAXIO_BASE_URL"
}

# Verify secrets
Write-Host "`nConfigured secrets:"
dotnet user-secrets list

Pop-Location
