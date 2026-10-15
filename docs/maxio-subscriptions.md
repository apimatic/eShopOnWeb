# Maxio subscriptions

Subscriptions run alongside Catalog → Basket → Order. PublicApi owns the authenticated HTTP surface, ApplicationCore defines the billing interface and DTOs, and Infrastructure calls the SDK shipped with the installed `maxio-advanced-billing` plugin. Maxio owns prices, invoices, subscription states, and billing dates.

## Verify locally

Use PowerShell 7 and the .NET 10 SDK. `global.json` permits major SDK roll-forward; set `DOTNET_ROLL_FORWARD=Major` because this machine lacks the ASP.NET Core 8 runtime.

1. Supply `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`, and `MAXIO_ENVIRONMENT` in your shell. Use a sandbox site. Run `./scripts/Set-MaxioSecrets.ps1`. It sends values through stdin into PublicApi's .NET user-secrets outside the repository. For a custom API address, pass `-BaseUrl`; it is used verbatim, including any path prefix. `MAXIO_ENVIRONMENT=EU` makes setup derive an EU override. The default is US hosting; sandbox is a site property, not an SDK region.
2. The SDK is a local project reference into the installed plugin, **not a NuGet package**. On another machine or CI, install this plugin and supply `dotnet build src/PublicApi/PublicApi.csproj -p:MaxioSdkProject="<absolute-path-to-plugin>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj"`. The reference's default path matches this workspace. Do not copy the SDK into the repository.
3. Run `./scripts/Verify-MaxioSubscriptions.ps1`. It builds, starts PublicApi detached on ports inside `APP_PORT_BLOCK_BASE` / `APP_PORT_BLOCK_SIZE`, waits for readiness, authenticates, lists plans, subscribes, checks replay and account ownership, then stops its process in `finally`. It uses the existing development seed user. The highest-price offered plan is selected by default (Pro in the supplied catalog); use `-ProductHandle basic-plan` to verify Basic. This creates a real sandbox customer, subscription, and invoice.
4. Expect `ReplayMatches`, `AccountMatches`, and `OwnershipVerified` to be true, plus the provider's plan, price, currency, state, and next billing date. Logs go outside the repository to the printed temporary paths. The script checks unauthenticated access receives 401. Its local HTTPS requests skip certificate validation; Maxio requests use normal TLS validation. For manual browser/Postman use, check/trust the local certificate with `dotnet dev-certs https --check --trust`.
5. Run `dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj` with `DOTNET_ROLL_FORWARD=Major`. Tests stub the SDK's HttpClient and never call Maxio. They cover concurrent submissions, both lost create responses, malformed success/read bodies, recovery, rejected writes, catalog bounds, currency, both invoicing architectures, user isolation, authentication, and the original commerce endpoints.

The verification script uses `UseOnlyInMemoryDatabase=true`. Both the identity users and billing claims disappear on restart, so the seed shopper receives a new immutable user ID and another verification run creates another sandbox customer/subscription. Production uses persistent identity and catalog databases. Deterministic references recover completed enrollments when claims are missing **and the immutable identity ID remains the same**.

## Manual HTTP flow

Start PublicApi detached using `Start-Process dotnet ... -WindowStyle Hidden -PassThru`, `ASPNETCORE_ENVIRONMENT=Development`, `UseOnlyInMemoryDatabase=true`, and ports in your assigned block. Stop its returned PID when done. Do not start a second instance on an occupied port. The verification script contains a complete example of this lifecycle.

POST `/api/authenticate` with the existing eShopOnWeb username/password to obtain `token`. Send `Authorization: Bearer <token>` on every subscription request; the Web cookie is a separate authentication model.

| Request | Result |
| --- | --- |
| `GET /api/subscription-plans` | Array of available, nonarchived, no-card plans in `Maxio:ProductFamilyHandle`: handle, name, description, integer price in cents, currency, interval and unit. |
| `POST /api/subscriptions` with `{"productHandle":"eshop-pro"}` | 200 with `status: complete` and the provider's subscription, or 202 with `status: pending`, `Location: /api/my-subscriptions`, and `Retry-After: 30` while a concurrent/unknown outcome is being settled. Select a handle actually returned by the plans endpoint. |
| `GET /api/my-subscriptions` | `subscriptions` read freshly from Maxio for this shopper and configured family; `hasPendingEnrollment` indicates unfinished local claims. |

No request accepts a user ID, Maxio customer ID, price, or billing state. PublicApi resolves the token's immutable identity ID; older tokens with only a username are resolved through UserManager. Since the app has no personal-name fields, Maxio receives display names `eShop Shopper` and the email from the authenticated account.

No-card enrollment explicitly uses **invoice collection**: `remittance` on Relationship Invoicing sites, `invoice` on legacy sites, selected from `ReadSite`. The sandbox's default automatic collection rejected the initial balance without a card even though the product itself did not require card capture. Invoice collection keeps the configured recurring price and outstanding balance in Maxio; an active subscription is not a claim that its invoice was paid. The integration neither captures cards nor marks invoices paid. Development checks `ReadSite.Test` before enrollment.

## Configuration and operations

The only Maxio section keys are `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. Required blank values and invalid overrides stop startup. Secrets/options are captured at registration; restart after rotation. User-secrets are for local development; production supplies the same configuration keys from its secret store. No catalog numeric IDs or credential values are embedded in the application.

Production requires the existing SQL Server CatalogContext and identity stores. Apply migration `AddBillingClaims` before serving billing traffic; it adds only `BillingClaims` and its indexes. PublicApi's existing catalog startup migration also applies it. The design-time context factory builds the SQL model without starting a host or contacting any service. No additional database or infrastructure is introduced.

Each customer and user/plan enrollment has a deterministic primary-key claim in CatalogContext. A preliminary read optimizes normal replay; the **database primary-key insert before the provider create** enforces concurrency. SQL duplicate-key errors and EF InMemory duplicate keys return the prior claim. The SDK may retry reads once; customer/subscription POSTs are excluded from retries. Neither operation exposes a real request idempotency key, so the SDK's generated header is not used as a guarantee.

Transport loss, timeout, cancellation, or malformed successful creates trigger an immediate reference lookup with a separate recovery deadline. Unresolved claims remain claimed and return pending. Every 30 seconds a background worker re-reads up to 100 unfinished claims, rotates unresolved/error rows, and records provider IDs when found. It never sends creates. A definitive provider rejection releases its claim. A preliminary lookup failure before a create also safely releases the new claim.

Unresolved records deliberately do not expire into another create: without a provider idempotency key that could duplicate billing. Monitor `Pending`/`Unknown` claims and warning logs. A crash before sending can leave a claim with no provider record. An operator must establish provider absence and that no original call can still finish before removing that claim; otherwise retain it for reconciliation. Back up both databases and keep identity IDs stable. One subscription per user/site/product is the current policy; cancellation/reactivation/resubscription and usage reporting are outside this feature.

An interactive operation has a shared 30-second deadline and 10-second attempt timeout. A lost create can spend up to 15 additional seconds reconciling, for a maximum 45-second provider-work budget, plus database/HTTP overhead. Catalog reads stop after 100 pages of 100 products and fail explicitly at the cap; enrollment never validates against a partial catalog. The SDK/customer-list operations used for account reads have no pagination in the plugin contract.

Provider errors become sanitized HTTP problems: validation/conflict/not-found retain actionable 4xx statuses; provider credentials become 502, throttling becomes 503, and read timeouts become 504. SDK URLs/types, raw bodies and credentials are not returned. Request-body logging and header logging are off, an explicit logger factory disables environment-triggered body logging, and form-sensitive keys are denied. Logs include opaque claim IDs, operation/status, and typed subscription-validation descriptions; no provider correlation field exists in the used error models. The reference app's pre-existing JWT signing secret/demo password remain development settings and must be configured appropriately for a production deployment.
