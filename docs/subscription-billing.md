# Subscription billing

PublicApi now exposes a parallel recurring billing flow. Basket, order and checkout behavior is unchanged. All three subscription routes require a **PublicApi bearer JWT**; storefront cookies do not authorize them.

| Route | Result |
| --- | --- |
| `GET /api/subscription-plans` | Array of live, non-archived, optional-card plans in `Maxio:ProductFamilyHandle`; handle, name, description, cents, currency and billing interval |
| `POST /api/subscriptions` | JSON `{ "productHandle": "<offered handle>" }`; `201` created, `200` existing, `202` pending reconciliation |
| `GET /api/my-subscriptions` | `subscriptions` with plan, actual subscription price, currency, provider state and `nextBillingDate`; `hasPendingOperations` |

Authenticate at `POST /api/authenticate` with `username` and `password`; use the response's `token` as `Authorization: Bearer <token>`. Tokens issued before this change need to be refreshed: billing uses their stable name-identifier claim and resolves the actual Identity account. Posted customer/user IDs have no authority.

## Verify locally

1. Install the **maxio-advanced-billing plugin** and .NET 10 SDK. `global.json` permits major SDK roll-forward; runtime execution uses `DOTNET_ROLL_FORWARD=Major`. The SDK source stays in the plugin, with a project reference controlled by the `MaxioSdkRoot` MSBuild property. Its default matches this workspace's plugin location. On another machine or CI, pass `-p:MaxioSdkRoot=<plugin-root>/sdk/dotnet` to build/test, or set the `MaxioSdkRoot` environment variable. A CI machine without that plugin source cannot build this integration.
2. Supply `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and `MAXIO_DEFAULT_PRODUCT_FAMILY` in the environment. Their values have already been loaded into PublicApi's .NET user-secrets on this machine. The verification script refreshes those secrets directly from the environment. It never creates a repository credential file. Do not run `user-secrets list` into a shared log.
3. Keep `APP_PORT_BLOCK_BASE` and `APP_PORT_BLOCK_SIZE` set to your assigned block. Stop any previous PublicApi instance first. From the repository root in PowerShell, run:

   ```powershell
   ./scripts/Verify-Subscriptions.ps1 -ProductHandle eshop-pro
   ```

   For a different catalog, supply its offered handle, or omit `ProductHandle` to select the highest-priced offered plan. If the plugin is elsewhere, also pass `-MaxioSdkRoot '<plugin-root>/sdk/dotnet'`.
4. The script checks the trusted dev certificate, builds, starts a **detached** Development PublicApi host on the assigned base port + 3, waits for readiness, authenticates the seeded demo shopper, lists plans, subscribes, repeats enrollment, checks account ownership and verifies an anonymous `401`. It prints plan/price/state/next billing date, then stops its own process in `finally`. Logs go outside the repository to the temporary directory. The default demo credentials are `demouser@microsoft.com` / `Pass@word1`; alternate credentials can be passed as parameters. This creates a real sandbox subscription and leaves it in Maxio.
5. Run the automated regressions:

   ```powershell
   $env:DOTNET_ROLL_FORWARD = 'Major'
   dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj
   ```

   Billing tests use the actual plugin SDK with a stub HTTP handler and isolated in-memory stores. They never need real credentials or contact Maxio.

## Configuration and billing behavior

Only these keys are bound from the `Maxio` section:

| Key | Local source |
| --- | --- |
| `Maxio:ApiKey` | user-secret initialized from `MAXIO_API_KEY` |
| `Maxio:Subdomain` | user-secret initialized from `MAXIO_SITE_SUBDOMAIN` |
| `Maxio:ProductFamilyHandle` | user-secret initialized from `MAXIO_DEFAULT_PRODUCT_FAMILY` |
| `Maxio:BaseUrl` | optional configuration/user-secret override |

Missing/blank required settings prevent host startup. When supplied, `BaseUrl` is assigned verbatim to the SDK's API base address; it is validated as HTTPS (HTTP loopback is permitted for tests). Without it, the SDK's US server uses the configured subdomain. Use the override for another hosting region or a proxy. `MAXIO_ENVIRONMENT` is not treated as a sandbox selector: the SDK exposes US/EU regions, and **Development verifies the site's `test` flag before billing**, failing closed for a live site. Credentials and SDK options are captured once at startup; restart the host after rotation.

Enrollment uses full-price **remittance collection** on Relationship Invoicing sites, or **invoice collection** on legacy sites. This enables the requested card-free flow without waiving charges or shifting billing dates. Maxio owns invoices, outstanding balances and future renewal assessments; collection and payment settlement remain in Maxio. Optional-card product settings alone did not allow an automatic-collection subscription with a nonzero opening balance in the sandbox.

One subscription per shopper per plan is intentional. Repeating a request returns that subscription even if it later becomes canceled; this API does not implement cancellation, reactivation, plan changes, usage reporting, payment capture or webhooks. The seeded metered component is left unchanged. Plan handles and the configured family determine eligibility, with no seeded numeric IDs in application code. Account reads fetch current provider state rather than using a cached subscription snapshot. A catalog above 10,000 products returns `503` rather than offering an incomplete list; unavailable/malformed provider responses return sanitized ProblemDetails.

## Durability and operation recovery

`CatalogContext.BillingClaims` stores a deterministic, primary-key claim **before** each provider write. SQL Server rejects concurrent duplicate inserts; the local EF in-memory provider enforces its primary keys too. Customer claims are per site/user; subscription claims are per site/user/plan. Reference lookups recover existing provider records. SDK POST retries are disabled by verb policy; GET has at most one retry. Interactive service work has a 45-second budget, with a separate 15-second reconciliation budget after an ambiguous write. Each SDK attempt has a 10-second timeout.

A lost response, timeout, unreadable success body or other ambiguous write keeps its claim pending. The failing request immediately tries a reference lookup; a worker repeats those lookups every 30 seconds. A second request receives `202` with `Retry-After: 5` and `/api/my-subscriptions` as the status location while work is unresolved. A documented `422` rejection releases the claim if reconciliation finds no subscription/customer. Pending writes are never blindly resent.

If no record ever becomes visible, the outcome cannot be proved from a lookup alone. An operator must establish in Maxio that the original write was refused or never committed before deleting that exact pending claim to allow a fresh attempt. Do not clear pending claims just because they are old. This preserves duplicate safety through delayed commits and provider outages. Monitor pending claim age and reconciliation warning counts.

Production uses the existing SQL Server catalog database and the additive `AddBillingClaims` EF migration; the app's existing startup migration path applies it. Deploy with persistent Catalog and Identity databases, standard secret-store configuration, and existing JWT/Identity production hardening. SQL Server could not be exercised on this machine. Local verification uses `UseOnlyInMemoryDatabase=true`: **claims and seeded Identity user IDs disappear on restart**, so a fresh local run can create another sandbox customer/subscription. In-memory mode is for local verification only.

SDK body/header logging is off with an explicit logger factory. Diagnostics record operation outcome, HTTP status, trace identifier and a provider request-ID header when available; no names, emails, tokens, API keys or raw error bodies are emitted by integration code. Existing repository package-vulnerability warnings are outside this billing change.
