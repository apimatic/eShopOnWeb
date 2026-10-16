param([string]$BaseUrl)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$settings = @{}
foreach ($pair in @(
    @('MAXIO_API_KEY', 'Maxio:ApiKey'),
    @('MAXIO_SITE_SUBDOMAIN', 'Maxio:Subdomain'),
    @('MAXIO_DEFAULT_PRODUCT_FAMILY', 'Maxio:ProductFamilyHandle')
)) {
    $value = [Environment]::GetEnvironmentVariable($pair[0])
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing environment variable $($pair[0])" }
    $settings[$pair[1]] = $value
}
if ($PSBoundParameters.ContainsKey('BaseUrl')) {
    $settings['Maxio:BaseUrl'] = $BaseUrl
} elseif ($env:MAXIO_ENVIRONMENT -eq 'EU') {
    $settings['Maxio:BaseUrl'] = "https://$($settings['Maxio:Subdomain']).ebilling.maxio.com"
} elseif ($env:MAXIO_ENVIRONMENT -and $env:MAXIO_ENVIRONMENT -ne 'US') {
    throw 'MAXIO_ENVIRONMENT must be US or EU. Sandbox selection comes from the supplied site/key.'
}
# JSON goes directly through stdin into the per-user store outside the repository.
$settings | ConvertTo-Json -Compress | dotnet user-secrets set --project "$repo/src/PublicApi/PublicApi.csproj"
if ($LASTEXITCODE -ne 0) { throw 'Unable to save Maxio user-secrets.' }
Write-Output 'Maxio settings saved to PublicApi user-secrets. Restart PublicApi after changing settings.'
