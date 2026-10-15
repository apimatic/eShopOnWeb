param([string]$BaseUrl)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/PublicApi/PublicApi.csproj'
$settings = @{}
foreach ($pair in @(
    @('Maxio:ApiKey', 'MAXIO_API_KEY'),
    @('Maxio:Subdomain', 'MAXIO_SITE_SUBDOMAIN'),
    @('Maxio:ProductFamilyHandle', 'MAXIO_DEFAULT_PRODUCT_FAMILY'))) {
    $value = [Environment]::GetEnvironmentVariable($pair[1])
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing $($pair[1])" }
    $settings[$pair[0]] = $value
}
if ($BaseUrl) { $settings['Maxio:BaseUrl'] = $BaseUrl }
elseif ($env:MAXIO_ENVIRONMENT -eq 'EU') {
    $settings['Maxio:BaseUrl'] = 'https://' + $settings['Maxio:Subdomain'] + '.ebilling.maxio.com'
}
elseif ($env:MAXIO_ENVIRONMENT -and $env:MAXIO_ENVIRONMENT -notin @('US', 'sandbox', 'test')) {
    throw 'Unsupported MAXIO_ENVIRONMENT; supply -BaseUrl explicitly.'
}
else {
    # Prevent an override for an earlier site from silently surviving reconfiguration.
    & dotnet user-secrets remove 'Maxio:BaseUrl' --project $project | Out-Null
}
# JSON goes to stdin; credential values never appear in command arguments or repo files.
$settings | ConvertTo-Json -Compress | & dotnet user-secrets set --project $project
if ($LASTEXITCODE -ne 0) { throw 'Unable to save Maxio user-secrets.' }
