Subscription billing is additive to PublicApi; basket checkout and orders retain their existing flow.

Configure `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` through a secret store. `Maxio:BaseUrl` is an optional absolute URL override passed verbatim to the SDK. PublicApi validates these at startup. Supplied development credentials have been loaded into PublicApi's user-secrets store outside the repository. Restart after rotating credentials. Sandbox selection is through the supplied site's credentials, not an SDK sandbox enum.

The project references the installed plugin SDK in place. A different machine or CI runner must install the plugin and repoint PublicApi's ProjectReference to its `sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`; no SDK source is copied here.

For the available .NET 10 SDK/runtime, use PowerShell:

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet build src/PublicApi/PublicApi.csproj
dotnet test eShopOnWeb.sln
```

Development uses `UseOnlyInMemoryDatabase=true`, so claim persistence lasts only for the running application. Production uses the existing SQL CatalogContext and its `20261016000000_AddBillingClaims` migration. Apply catalog migrations before admitting billing traffic. Keep the claims database shared between API instances. The primary key rejects competing reservations before either provider POST; the application does not use a process lock for correctness.

This flow collects no card. It reads the site's invoicing architecture through the SDK and creates recurring invoices using `remittance` for relationship invoicing or `invoice` for legacy statements. The catalog price remains payable. A product's `require_credit_card=false` alone does not make automatic collection work without a payment method; live verification returned a payment-method rejection until invoice collection was selected.

For a live smoke check, run PublicApi using its assigned launch settings/port block, with a detached hidden process. Poll its readiness URL, and stop only that process when done. Authenticate an existing shopper at `POST /api/authenticate` and send its bearer token on each request:

1. `GET /api/subscription-plans`: use the returned handle and price; no catalog identifiers are fixed in application code.
2. `POST /api/subscriptions` with `{ "productHandle": "<returned handle>" }`: check the returned subscription ID, plan, price in cents, currency, state, and next billing date.
3. Repeat the POST, including concurrent clicks. It returns the same subscription when reconciliation finds it; an overlapping unfinished write may return 409 with `billing-in-progress`.
4. `GET /api/my-subscriptions`: verify the same subscription is returned. Authenticate a second shopper and verify their list excludes the first shopper's subscription. Client customer/user IDs are ignored; the token's name is resolved through Identity.
5. Omit or invalidate the bearer token: all three routes return 401. An unoffered handle returns 400 before any customer/subscription write.

Executable PowerShell 7 smoke commands, from the repository root (the sample seeded shopper uses the repository's documented development password):

```powershell
$ErrorActionPreference = 'Stop'
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet build src/PublicApi/PublicApi.csproj
if ($LASTEXITCODE) { throw 'Build failed' }
$project = (Resolve-Path src/PublicApi).Path
$launch = Get-Content "$project/Properties/launchSettings.json" -Raw | ConvertFrom-Json
$urls = $launch.profiles.PublicApi.applicationUrl
$base = [int]$env:APP_PORT_BLOCK_BASE
$size = [int]$env:APP_PORT_BLOCK_SIZE
foreach ($address in $urls.Split(';')) {
    $port = ([uri]$address).Port
    if ($port -lt $base -or $port -ge ($base + $size)) { throw 'Launch port is outside assigned block' }
}
$api = $urls.Split(';')[0]
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = $urls
$stdout = Join-Path $env:TEMP 'eshop-subscription-smoke.stdout.log'
$stderr = Join-Path $env:TEMP 'eshop-subscription-smoke.stderr.log'
$hostProcess = Start-Process dotnet -ArgumentList @("`"$project/bin/Debug/net8.0/PublicApi.dll`"") -WorkingDirectory $project -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($hostProcess.HasExited) { throw 'PublicApi exited; inspect external smoke logs' }
        try {
            Invoke-RestMethod "$api/swagger/v1/swagger.json" -SkipCertificateCheck | Out-Null
            $ready = $true
            break
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if (!$ready) { throw 'PublicApi did not become ready' }
    $login = Invoke-RestMethod "$api/api/authenticate" -Method Post -ContentType 'application/json' -Body (@{ username='demouser@microsoft.com'; password='Pass@word1' } | ConvertTo-Json) -SkipCertificateCheck
    if (!$login.result -or !$login.token) { throw 'Shopper authentication failed' }
    $headers = @{ Authorization = "Bearer $($login.token)" }
    $plans = Invoke-RestMethod "$api/api/subscription-plans" -Headers $headers -SkipCertificateCheck
    $plan = @($plans | Sort-Object priceInCents -Descending)[0]
    if (!$plan.handle) { throw 'No offered plan' }
    $body = @{ productHandle=$plan.handle } | ConvertTo-Json
    $first = Invoke-RestMethod "$api/api/subscriptions" -Method Post -Headers $headers -ContentType 'application/json' -Body $body -SkipCertificateCheck
    $again = Invoke-RestMethod "$api/api/subscriptions" -Method Post -Headers $headers -ContentType 'application/json' -Body $body -SkipCertificateCheck
    if (!$first.id -or $first.id -ne $again.id) { throw 'Repeated enrollment did not preserve subscription ID' }
    $mine = Invoke-RestMethod "$api/api/my-subscriptions" -Headers $headers -SkipCertificateCheck
    if (@($mine | Where-Object id -eq $first.id).Count -ne 1) { throw 'Subscription missing or duplicated in shopper list' }
    Write-Output 'Discovery, enrollment, repeat enrollment, and shopper list passed.'
} finally {
    Stop-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue
    $login = $null
    $headers = $null
}
```

SDK calls have a 60-second request budget, 10-second attempts, and at most one GET retry. POSTs are excluded from retries. An ambiguous write gets an independent 15-second reconciliation attempt. Its claim remains unknown if the provider cannot establish a result; subsequent enrollment/list calls reconcile it and never resend its POST. A crash before the POST is conservatively treated the same way. If the provider continues reporting absence, operator investigation is required before any manual claim removal; there is no automatic expiry that could duplicate billing.

`SubscriptionBillingTests` exercise lost responses after customer/subscription commits, concurrent primary-key claims for both writes, malformed success and error bodies, definite rejection/retry, unknown-state recovery, catalog cutoff, bearer authorization, and Identity-owned enrollment. They use the real plugin SDK over an HTTP stub with synthetic fixtures, and never contact the sandbox.

Verification completed on this workspace: all 90 solution tests passed (44 unit, 31 PublicApi integration, 3 repository integration, 12 functional). Live sandbox checks passed for both offered monthly plans, active enrollment, repeat enrollment preserving the ID, account retrieval, a second shopper's isolation, all three routes requiring a bearer token, and unoffered-plan rejection. Four simultaneous alternate-plan enrollments produced one successful subscription and three claim conflicts. The host log recorded one customer POST and two subscription POSTs total, one per selected plan. The detached verification host was stopped afterward. SQL migration execution was not exercised because this environment has no SQL Server; the requested in-memory mode was used.
