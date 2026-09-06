<#
.SYNOPSIS
    Loads the Maxio sandbox credentials from environment variables into .NET user-secrets
    for src/PublicApi.

.DESCRIPTION
    Reads MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY from the
    environment and stores them under the Maxio: configuration section. Secret values live in
    the user-secrets store outside the repository - this script only ever names the variables.

    Optionally set MAXIO_BASE_URL to override the API base address (for example an EU-hosted
    site or an API Gateway connector); otherwise it is derived from the subdomain.

.EXAMPLE
    pwsh ./scripts/set-maxio-user-secrets.ps1
#>
[CmdletBinding()]
param(
    [string] $ProjectPath = (Join-Path $PSScriptRoot '..' 'src' 'PublicApi')
)

$ErrorActionPreference = 'Stop'

$required = @{
    'Maxio:ApiKey'              = 'MAXIO_API_KEY'
    'Maxio:Subdomain'           = 'MAXIO_SITE_SUBDOMAIN'
    'Maxio:ProductFamilyHandle' = 'MAXIO_DEFAULT_PRODUCT_FAMILY'
}

$missing = $required.Values | Where-Object { -not [Environment]::GetEnvironmentVariable($_) }

if ($missing) {
    throw "Missing environment variable(s): $($missing -join ', '). Set them before running this script."
}

foreach ($key in $required.Keys) {
    $value = [Environment]::GetEnvironmentVariable($required[$key])
    dotnet user-secrets --project $ProjectPath set $key $value | Out-Null
    Write-Host "set $key from `$env:$($required[$key])"
}

$baseUrl = [Environment]::GetEnvironmentVariable('MAXIO_BASE_URL')
if ($baseUrl) {
    dotnet user-secrets --project $ProjectPath set 'Maxio:BaseUrl' $baseUrl | Out-Null
    Write-Host 'set Maxio:BaseUrl from $env:MAXIO_BASE_URL'
}

Write-Host ''
Write-Host 'Stored keys (values are held in the user-secrets store, not in the repository):'
dotnet user-secrets --project $ProjectPath list |
    ForEach-Object { ($_ -replace '(?<=^Maxio:ApiKey = ).*', '<redacted>') }
