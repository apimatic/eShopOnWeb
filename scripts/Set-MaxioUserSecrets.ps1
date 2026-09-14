<#
.SYNOPSIS
    Copies the Maxio Advanced Billing credentials from environment variables into the
    PublicApi project's .NET user-secrets store.

.DESCRIPTION
    Secrets must never be written into the repository. This script reads them from the
    environment and hands them to `dotnet user-secrets`, which stores them outside the
    repository (%APPDATA%\Microsoft\UserSecrets on Windows, ~/.microsoft/usersecrets
    elsewhere). It prints key names only - never values.

    Environment variables consumed:
      MAXIO_API_KEY               -> Maxio:ApiKey                (required)
      MAXIO_SITE_SUBDOMAIN        -> Maxio:Subdomain             (required unless Maxio:BaseUrl is set)
      MAXIO_DEFAULT_PRODUCT_FAMILY-> Maxio:ProductFamilyHandle   (required)
      MAXIO_ENVIRONMENT           -> Maxio:Environment           (optional, US or EU, defaults to US)
      MAXIO_BASE_URL              -> Maxio:BaseUrl               (optional, used verbatim when set)
      MAXIO_DEFAULT_PLAN          -> Maxio:DefaultPlanHandle     (optional, plan used when a request names none)

.EXAMPLE
    ./scripts/Set-MaxioUserSecrets.ps1
#>
[CmdletBinding()]
param(
    [string] $Project = (Join-Path $PSScriptRoot '..' 'src' 'PublicApi' 'PublicApi.csproj')
)

$ErrorActionPreference = 'Stop'

$map = [ordered]@{
    'Maxio:ApiKey'             = @{ Variable = 'MAXIO_API_KEY';                Required = $true }
    'Maxio:Subdomain'          = @{ Variable = 'MAXIO_SITE_SUBDOMAIN';         Required = $true }
    'Maxio:ProductFamilyHandle'= @{ Variable = 'MAXIO_DEFAULT_PRODUCT_FAMILY'; Required = $true }
    'Maxio:Environment'        = @{ Variable = 'MAXIO_ENVIRONMENT';            Required = $false }
    'Maxio:BaseUrl'            = @{ Variable = 'MAXIO_BASE_URL';               Required = $false }
    'Maxio:DefaultPlanHandle'  = @{ Variable = 'MAXIO_DEFAULT_PLAN';           Required = $false }
}

$missing = @()

foreach ($key in $map.Keys) {
    $variable = $map[$key].Variable
    $value = [Environment]::GetEnvironmentVariable($variable)

    if ([string]::IsNullOrWhiteSpace($value)) {
        if ($map[$key].Required) { $missing += $variable }
        else { Write-Host "skipped  $key (`$env:$variable is not set)" }
        continue
    }

    dotnet user-secrets --project $Project set $key $value | Out-Null
    Write-Host "set      $key (from `$env:$variable)"
}

if ($missing.Count -gt 0) {
    throw "Missing required environment variable(s): $($missing -join ', ')"
}

Write-Host ''
Write-Host 'Stored keys:'
dotnet user-secrets --project $Project list |
    ForEach-Object { ($_ -split ' = ')[0] } |
    Where-Object { $_ -like 'Maxio:*' } |
    ForEach-Object { Write-Host "  $_" }
