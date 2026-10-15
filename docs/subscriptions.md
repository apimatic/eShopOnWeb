# Subscription billing

PublicApi exposes three JWT-protected endpoints alongside the existing catalog/basket/order flow:

| Endpoint | Result |
| --- | --- |
| GET /api/subscription-plans | `plans` with stable handles, integer cents and billing interval; `isTruncated` flags an incomplete catalog |
| POST /api/subscriptions | Body: `productHandle`, `firstName`, `lastName`; 200 confirmed subscription or 202 pending reconciliation |
| GET /api/my-subscriptions | Current user's subscriptions read from Maxio, including plan, actual subscription price, currency, state and next billing date |

The shopper's email and identity come from Identity/JWT, never the request body. Newly issued JWTs include the immutable Identity user ID; subscription writes/account reads resolve that ID, so renaming or reassigning a username cannot transfer a subscription. Authenticate again if using an older token without a user-ID claim. First/last names are required because this app's Identity user has no name fields. Choose a handle from the configured product family's offered plans; archived plans are excluded. Prices and numeric IDs are never configured locally. Amounts are integer cents; next billing date is Maxio's next assessment timestamp and may be null when no assessment is scheduled.

Subscriptions use **remittance collection** for the no-card flow. Sandbox verification showed automatic collection rejects these paid plans without a payment profile despite their optional-card signup setting. Remittance enrollment creates an invoice balance; confirmation does not assert payment. Card capture, automatic payment collection, cancellation, plan changes and metered usage reporting are outside this feature.

## Verify locally (PowerShell 7)

1. Have the installed `maxio-advanced-billing` plugin available. The generated SDK is not on NuGet. `src/PublicApi/PublicApi.csproj` references the SDK in this machine's plugin directory. On another machine re-point that reference to `<plugin-root>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`; do not copy the SDK into this repo. .NET 10 builds the plugin's C# 14 source. `global.json` permits SDK major roll-forward.
2. Keep `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`, `APP_PORT_BLOCK_BASE`, and `APP_PORT_BLOCK_SIZE` in your environment. Use sandbox credentials. Run `dotnet dev-certs https --check --trust`. The demo script uses certificate bypass only for its readiness probe; use `-SkipCertificateCheck` below only when the local certificate cannot be trusted.
3. From the repository root run `./scripts/Start-SubscriptionsDemo.ps1`. It imports the three Maxio values into PublicApi .NET user-secrets, builds, starts a detached host within your assigned port block, and polls readiness. Logs/PID live in the OS temp directory. It refuses to start over an existing instance.
4. Authenticate and browse plans:

```powershell
$base = "https://localhost:$env:APP_PORT_BLOCK_BASE"
$auth = Invoke-RestMethod "$base/api/authenticate" -Method Post -ContentType application/json -Body (@{
    username = 'demouser@microsoft.com'; password = 'Pass@word1'
} | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($auth.token)" }
$plans = Invoke-RestMethod "$base/api/subscription-plans" -Headers $headers
$plans.plans | Format-Table
```

5. Subscribe to Pro in the seeded demo catalog (or pick a handle returned by your site's plan list). Repeat the POST: both confirmed responses must contain the same subscription ID.

```powershell
$body = @{ productHandle = 'eshop-pro'; firstName = 'Demo'; lastName = 'Shopper' } | ConvertTo-Json
Invoke-RestMethod "$base/api/subscriptions" -Method Post -Headers $headers -ContentType application/json -Body $body
Invoke-RestMethod "$base/api/subscriptions" -Method Post -Headers $headers -ContentType application/json -Body $body
Invoke-RestMethod "$base/api/my-subscriptions" -Headers $headers | Format-Table
```

6. A 202 includes `Retry-After: 5` and means enrollment is still being reconciled. Repeat the same POST after that delay or refresh the account view. Never interpret 202 as a failed signup. Try a request without the bearer header to verify 401; a handle outside the configured family must produce 400 without billing writes.
7. Stop the detached host with `./scripts/Start-SubscriptionsDemo.ps1 -Stop`. Run automated checks with `$env:DOTNET_ROLL_FORWARD='Major'; dotnet test eShopOnWeb.sln` and `dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj`. The API tests use a fake SDK transport and test-only settings; they make no live billing calls.

## Configuration and deployment

Only these `Maxio:` section keys are bound: `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`. Required values are validated before the host starts. `BaseUrl` is optional; when supplied its value is assigned verbatim to the SDK API base URL. Otherwise the SDK uses its US server template and configured subdomain. Use `Maxio__BaseUrl` for an environment override, including EU sites or local transport testing. HTTPS is required except loopback HTTP. Secrets/options are captured once; restart after rotating them. Never commit user-secrets or credential values.

Apply the `AddBillingClaims` CatalogContext migration for SQL deployments (`dotnet ef database update --project src/Infrastructure --startup-project src/PublicApi --context CatalogContext`, with production connection settings). It only creates `BillingClaims`; it does not change commerce tables. The existing application also applies catalog migrations during SQL seeding. Test/demo `UseOnlyInMemoryDatabase=true` ignores migrations and loses both users and claims on restart; seeded users receive new IDs, so cross-restart deduplication is not guaranteed in this mode. Production must use its persistent Identity and Catalog databases shared across instances.

Claims use database primary keys and are inserted **before** remote creates. One subscription per user per plan is supported for its lifetime; repeating a request for a canceled subscription returns its current state. Deterministic remote references allow recovery if local claims are lost while user IDs stay stable. A remote deletion or an unreadable lookup never authorizes another create behind an existing claim.

Transport failure, timeout or unreadable success triggers immediate reference lookup. Unresolved writes retain their claim and return 202. A background service rechecks pending claims older than one minute every 30 seconds; repeat POST also reconciles. No automatic create retry or blind claim expiration occurs. Monitor pending-claim warnings: a crash between claiming and sending can leave a claim pending even when nothing was created. An operator must establish that the original send cannot still complete before removing such a claim. Never delete pending claims merely to unblock a retry.

Every SDK call has a 15-second total token budget and a 10-second header timeout; reconciliation gets an independent 15-second budget. SDK retries are disabled. Catalog traversal is capped at 100 pages of 200 and exposes truncation; subscribing rejects an incomplete catalog. Logs omit SDK bodies/headers, customer names, email and credentials. Public failures use sanitized problem details; 422 distinguishes provider validation, 502 malformed/unexpected provider data, and 503 provider unavailability.

Sandbox self-verification: Pro returned active at 29900 USD cents; repeat returned the same ID. Four concurrent Basic requests produced one confirmed enrollment plus three pending responses; account view contained one Basic at 2900 cents and one Pro. Both included next billing dates. All verification hosts were stopped afterwards.

