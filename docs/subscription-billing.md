# Subscription billing

PublicApi exposes an additive JWT-authenticated subscription flow. The existing basket and order checkout are unchanged. Maxio Advanced Billing owns plans, prices, subscription state and billing dates. All provider traffic uses the SDK shipped with the **maxio-advanced-billing** plugin; no direct REST implementation is included.

## Run and verify on this machine

1. Keep the supplied sandbox environment variables available. Their values have already been loaded into PublicApi's .NET user-secrets store. On another machine, load them without putting values in repository files or command arguments:

   ```powershell
   @{
     'Maxio:ApiKey' = $env:MAXIO_API_KEY
     'Maxio:Subdomain' = $env:MAXIO_SITE_SUBDOMAIN
     'Maxio:ProductFamilyHandle' = $env:MAXIO_DEFAULT_PRODUCT_FAMILY
   } | ConvertTo-Json -Compress | dotnet user-secrets set --project src/PublicApi
   ```

2. Run from the repository root:

   ```powershell
   ./scripts/Verify-SubscriptionBilling.ps1
   ```

   The script builds PublicApi, checks the HTTPS certificate, starts it detached on `APP_PORT_BLOCK_BASE`, authenticates the seeded demo shopper, lists plans, submits two concurrent enrollments, checks account details, repeats enrollment, and stops its own host by PID. It defaults to the highest-priced offered plan (Pro in the supplied demo); use `-ProductHandle <handle-from-the-list>` for another plan. **This creates a real subscription on the configured sandbox site.** Logs are outside the repository in the temporary directory. A demo success includes the configured plan's amount in cents, currency, state and next billing date.

3. Run the tests:

   ```powershell
   dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj
   dotnet test tests/UnitTests/UnitTests.csproj
   ```

The SDK pin rolls forward to the installed major SDK. The verification script sets `DOTNET_ROLL_FORWARD=Major` and `UseOnlyInMemoryDatabase=true`. In-memory identity and claims are lost on restart; restarting creates new seeded user identities and may create new sandbox billing records. Use persistent SQL Server storage for production and for identity continuity across restarts.

Verified in this workspace: the solution builds; all 86 tests pass (including 12 billing tests); live concurrent enrollment produced one active Pro subscription at $299/month with a next billing date; repeat enrollment returned the same subscription. The SQL migration script was generated and inspected, but no SQL Server instance was available for executing it.

## HTTP contract

Obtain a bearer token using `POST /api/authenticate` with `{"username":"demouser@microsoft.com","password":"Pass@word1"}`. Send `Authorization: Bearer <token>` on each billing request. Storefront cookie authentication does not apply.

| Request | Result |
| --- | --- |
| `GET /api/subscription-plans` | `{items:[{handle,name,description,priceInCents,interval,intervalUnit}],isPartial,nextPage}`; offered products belong to the configured family, recur, are unarchived, and allow signup without a payment profile/address. |
| `POST /api/subscriptions` with `{"productHandle":"<offered handle>"}` | `201` on new confirmed enrollment; `200` on an existing or reconciled subscription; `202` with `status:"pending"`, `Location` and `Retry-After` when processing or reconciliation is outstanding. Confirmed results include a `subscription` with plan, amount, currency, state and `nextBillingDate`. |
| `GET /api/my-subscriptions` | `{subscriptions:{items:[...],isPartial,nextPage},hasPendingEnrollment}`; reads current Maxio state for the caller's customer and configured family. |

Identity comes exclusively from the validated token and the application's identity store. Caller-supplied user IDs, customer IDs, prices and subscription IDs are never used. Choose a plan handle from the offered catalog; do not use seeded numeric IDs. Amounts remain integer minor units. A null next billing date is returned as null for a provider state without a scheduled assessment; no date is invented.

This no-card flow explicitly uses the SDK's `CollectionMethod.Remittance`. The supplied site's default automatic collection rejected signup with “No payment method was on file”; remittance enrollment was verified successfully. Maxio creates the recurring billing obligation and invoices; this API does not claim to have collected payment. Card capture, cancellation, plan changes and metered usage submission are outside this capability.

## Configuration and deployment

| Configuration key | Source |
| --- | --- |
| `Maxio:ApiKey` | User-secrets, `Maxio__ApiKey`, or `MAXIO_API_KEY` fallback |
| `Maxio:Subdomain` | User-secrets, `Maxio__Subdomain`, or `MAXIO_SITE_SUBDOMAIN` fallback |
| `Maxio:ProductFamilyHandle` | User-secrets, `Maxio__ProductFamilyHandle`, or `MAXIO_DEFAULT_PRODUCT_FAMILY` fallback |
| `Maxio:BaseUrl` | Optional user-secret or `Maxio__BaseUrl`; a complete HTTP(S) API base address used verbatim, including any path prefix |

Missing/blank required settings prevent startup. `MAXIO_ENVIRONMENT` selects SDK hosting region (`US` default, or `EU`); the SDK has no sandbox enum. Sandbox isolation comes from the configured sandbox site and credentials. No site, family or product value is hardcoded. Credentials and SDK options are captured once; restart after rotation. Use HTTPS for deployed provider traffic; HTTP overrides support local test servers. No request bodies, authentication headers or raw provider errors are exposed in billing responses.

The SDK is generated with the installed plugin and is not published on NuGet. Infrastructure references it **in place**. Teammates and CI must install that plugin and point the build at their copy if its location differs:

```powershell
dotnet build eShopOnWeb.sln -p:MaxioSdkProject="<plugin-root>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj"
```

The SQL migration adds only `BillingClaims` and its index. The existing CatalogContext migration mechanism applies it at startup; deploy migrations through your normal migration process before serving production traffic. No additional database or infrastructure dependency was introduced. Existing application defaults for JWT signing and seeded demo credentials remain reference-app defaults and need the application's usual production configuration.

## Duplicate prevention and recovery

One subscription per stable application user and offered plan is the enrollment policy. A database primary-key claim is committed **before** each provider create. Concurrent callers lose that insert before reaching Maxio. Claims and deterministic provider references include billing target and region; subscription references also include family and plan. SQL persists claims across instances/restarts; in-memory storage coordinates requests within one process only.

The SDK retries GET at most once and never retries POST. The main workflow has a shared 30-second deadline, eight-second SDK attempt timeouts and a ten-second HTTP timeout. Each uncertain create gets its own ten-second reconciliation window, making the enrollment network budget at most 50 seconds. A dropped connection, timeout, provider 5xx or unreadable successful response triggers lookup by the original reference. Confirmed rejections release the claim; uncertain writes retain it and return pending.

The reconciliation worker runs every 30 seconds, processing at most 25 stale claims per batch. Abandoned `Reserved` claims older than two minutes can be released because dispatch requires a persisted `Dispatching` transition. Dispatched/unknown claims are only reconciled by reads; they are **never** replayed or expired into another charge. If Maxio continues to report no result, the claim stays pending: operators must establish the provider outcome before removing such a claim. Polling account subscriptions never authorizes another write. A crash between marking dispatch and sending cannot be distinguished safely from a lost response without provider idempotency; this integration favors preventing duplicate billing.

Plan and account reads stop at 100 pages of 200 records and mark a capped response `isPartial:true` with `nextPage`. Enrollment refuses to validate against a partial catalog. This version exposes the continuation position for diagnosis but has no public page parameter; narrow the configured family or increase the reviewed service bound if a complete catalog exceeds it. Provider authentication failures map to 502, rate limits to 503, enrollment validation failures to 400/409/422; other provider failures are sanitized. Logs carry safe status/type and the application's trace ID, without raw provider validation messages.
