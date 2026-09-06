# Setup script to configure Maxio user-secrets for PublicApi project
# Run this script with: .\setup-maxio-secrets.ps1

param(
    [string]$ApiKey,
    [string]$Subdomain,
    [string]$ProductFamilyHandle,
    [string]$BaseUrl = ""
)

$projectPath = ".\src\PublicApi"

if (-not (Test-Path $projectPath)) {
    Write-Error "PublicApi project not found at $projectPath"
    exit 1
}

if ([string]::IsNullOrEmpty($ApiKey) -or [string]::IsNullOrEmpty($Subdomain) -or [string]::IsNullOrEmpty($ProductFamilyHandle)) {
    Write-Host "Usage: .\setup-maxio-secrets.ps1 -ApiKey <key> -Subdomain <subdomain> -ProductFamilyHandle <handle> [-BaseUrl <url>]"
    Write-Host ""
    Write-Host "Example:"
    Write-Host "  .\setup-maxio-secrets.ps1 -ApiKey abc123def456 -Subdomain cp-exp-2 -ProductFamilyHandle eshop-subscribe"
    exit 1
}

Push-Location $projectPath

try {
    Write-Host "Initializing user-secrets..."
    dotnet user-secrets init --force | Out-Null

    Write-Host "Setting Maxio configuration..."
    dotnet user-secrets set "Maxio:ApiKey" $ApiKey
    dotnet user-secrets set "Maxio:Subdomain" $Subdomain
    dotnet user-secrets set "Maxio:ProductFamilyHandle" $ProductFamilyHandle

    if (-not [string]::IsNullOrEmpty($BaseUrl)) {
        dotnet user-secrets set "Maxio:BaseUrl" $BaseUrl
        Write-Host "  Maxio:BaseUrl = $BaseUrl"
    }

    Write-Host ""
    Write-Host "✓ Maxio configuration set successfully!"
    Write-Host ""
    Write-Host "Configured values:"
    Write-Host "  Maxio:ApiKey = (hidden)"
    Write-Host "  Maxio:Subdomain = $Subdomain"
    Write-Host "  Maxio:ProductFamilyHandle = $ProductFamilyHandle"
}
catch {
    Write-Error "Error setting secrets: $_"
    exit 1
}
finally {
    Pop-Location
}
