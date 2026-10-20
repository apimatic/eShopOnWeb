# Maxio Advanced Billing: implementer notes

## Files

| File | Job |
| --- | --- |
| `src/PublicApi/PublicApi.csproj` | `ProjectReference` to the SDK through the `MaxioSdkRoot` MSBuild property (env var of the same name also works). Default: `$(MSBuildThisFileDirectory)..\..\..\marketplace\plugins\maxio-advanced-billing\sdk\dotnet\`. A target errors clearly if the SDK project is missing. Only PublicApi references the SDK, so Web is unaffected. |
| `src/PublicApi/Program.cs` | `builder.Services.AddMaxioSubscriptionBilling(builder.Configuration)` |
| `src/PublicApi/Maxio/MaxioOptions.cs` | `MaxioOptions` (section `Maxio`: `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`) and `MaxioOptionsValidator`. Messages name keys, never values. |
| `src/PublicApi/Maxio/MaxioServiceCollectionExtensions.cs` | Binds the options with `ValidateOnStart`. Registers the named HttpClient `MaxioAdvancedBilling` (Timeout 15s, `PooledConnectionLifetime` 5 min) and the singleton `MaxioAdvancedBillingClient` built at first resolution. Also registers `MaxioGateway` (singleton), the scoped services, and `TimeProvider.System` via TryAdd. |
| `src/PublicApi/Maxio/MaxioClientOptionsFactory.cs` | SDK options: `Environment = ServerEnvironment.Us`; `BasicAuth` (Username = ApiKey, Password = "x"); `RetryOptions.Default() with { Timeout = 10s, MaxRetries = 2 }`; `LoggingOptions` with an explicit LoggerFactory and body/header logging off. `BaseUrl` goes verbatim to `Server.Production.Us.BaseUrl`, otherwise `Subdomain` goes to `.Us.Site`. |
| `src/PublicApi/Maxio/MaxioGateway.cs` | The only SDK caller. It has a per-operation catch ladder (Case A/B, `ResponseDeserializationException`, `SdkConnectionException`, `AuthSchemeException`, OCE on writes). Reads throw `MaxioBillingException(status, safe message)`, and a 404 comes back as `null`. Writes throw `MaxioWriteRejectedException` (definite 4xx) or `MaxioWriteOutcomeUnknownException` (connection, cancel, 5xx, unreadable 2xx). `CreateSubscription` sends `PaymentCollectionMethod = Models.Enums.CollectionMethod.Remittance` (code constant, commented; no config key). |
| `src/PublicApi/Maxio/MaxioPlanCatalog.cs` | Walks `handle:{family}` with PerPage 200 and a cap of 5 pages, setting `Truncated`. On a 404 it falls back to `ListProductFamilies` and caches the resolved id in IMemoryCache for 30 min. Archived products are filtered out. |
| `src/PublicApi/Maxio/MaxioCustomerService.cs` | Claim (`MaxioCustomerLink` PK = user name), then lookup by reference, then create, then record. Handles the 422 re-read, unknown-outcome settlement, and takeover after the 2-minute lease. |
| `src/PublicApi/Maxio/MaxioSubscriptionService.cs` | Checks the invariant against the catalog, then claims (`MaxioSubscriptionClaim` PK = user + handle), ensures the customer, calls FindSubscription, then create, then record. Also holds the reconciliation and the 30s per-request budget (`MaxioCallBudget`). The reference is `eshop-` + the first 16 bytes of SHA-256(user, handle) in hex. `WithPlanFallback` fills handle, name, price, interval and unit from the offered catalog plan on every subscribe response (created, existing, adopted, settled) where `Subscription.Product`/`product_price_in_cents` are absent. `GET api/my-subscriptions` has no such fallback. |
| `src/PublicApi/Maxio/BillingClaimStore.cs` | Inserts the claim, or catches the duplicate and loads the existing row. Also `TrySaveAsync` (concurrency-safe) and `ReleaseAsync`. |
| `src/PublicApi/Maxio/MaxioCallBudget.cs`, `MaxioExceptions.cs`, `MaxioBillingModels.cs` | The budget constants (30s per request, 10s reconciliation, 15s HttpClient), the integration's exception types, and the result records. |
| `src/PublicApi/SubscriptionEndpoints/*` | `GET api/subscription-plans`, `POST api/subscriptions` (201 created, 200 existing, 202 pending, 400/422/409/5xx), `GET api/my-subscriptions`. All require JWT, the user comes from `ClaimTypes.Name`, and the e-mail comes from `UserManager.FindByNameAsync`. |
| `src/ApplicationCore/Entities/SubscriptionBilling/*` | `MaxioCustomerLink`, `MaxioSubscriptionClaim` and `BillingClaimStatus` (Pending/Completed/Unknown). `LeaseId` is the concurrency token. |
| `src/Infrastructure/Data/CatalogContext.cs`, `Data/Config/Maxio*Configuration.cs` | DbSets and keys. |
| `src/Infrastructure/Data/Migrations/20261020083046_AddMaxioSubscriptionClaims*.cs`, snapshot | SQL Server migration for the two tables only (see pitfalls). |
| `tests/MaxioBillingTests/*` (new xUnit project, added to `eShopOnWeb.sln` under `tests`) | 35 tests that run the real SDK over a stub `HttpMessageHandler`, with the EF in-memory store underneath. |
| `tests/PublicApiIntegrationTests/ProgramTest.cs`, `tests/FunctionalTests/PublicApi/ApiTestFixture.cs` | Inject non-secret placeholder `Maxio:` settings via `ConfigureAppConfiguration`. `BaseUrl` is `http://127.0.0.1:9`, so tests never reach Maxio. |

## Build / test / run

Every command needs `export DOTNET_ROLL_FORWARD=Major` first.
- Build: `dotnet build eShopOnWeb.sln`. Add `-p:MaxioSdkRoot=<plugin>/sdk/dotnet/` if the plugin is elsewhere.
- Test: `dotnet test eShopOnWeb.sln`. Results: UnitTests 44/44, MaxioBillingTests 35/35, IntegrationTests 3/3, FunctionalTests 12/12, and PublicApiIntegrationTests 3/15. That last result is the same as the pre-existing baseline: the same 3 tests pass and the host starts.
- Run: from the repo root, with `UseOnlyInMemoryDatabase=true`, run `dotnet run --project src/PublicApi --no-build --launch-profile PublicApi`. It listens on https 39703 and http 39704. Start it detached, then stop it by PID (`netstat -ano | grep 39704`, then `taskkill //PID <pid> //F`).
- Get a token: `POST /api/authenticate` with `{"username":"demouser@microsoft.com","password":"Pass@word1"}`, then send `Authorization: Bearer <token>`.

## Pitfalls hit

- **The EF in-memory provider raises `System.ArgumentException`** ("An item with the same key has already been added") on a duplicate PK at `SaveChangesAsync`, not `DbUpdateException`. `BillingClaimStore.IsInsertRefusal` accepts both, then confirms the duplicate by loading the row. A unit test pins this.
- **`AddConfigurationFile("appsettings.test.json")` in Program.cs resolves against the content root (src/PublicApi)**, so the test project's `appsettings.test.json` is never loaded. The placeholders therefore go through `WithWebHostBuilder(...ConfigureAppConfiguration)` instead. FunctionalTests uses environment "Testing", which means no user-secrets, so it needed the placeholders too.
- **`dotnet ef migrations add` also scaffolded pre-existing model drift**: five AlterColumn nullable-to-required changes on Orders, OrderItems and Catalog. I removed those from the migration and restored the snapshot hunks for them. The migration and snapshot now add only the two Maxio tables, and the old drift still shows up in the next migration.
- **I registered over a named HttpClient instead of using `AddMaxioAdvancedBillingClient`.** The extension uses the shared default client with a 100s timeout and no `PooledConnectionLifetime`, and its `configure` callback has no access to the service provider. The plan step 1 wording said to use the extension; this is the client-initialization skill's documented alternative.
- Raw string literals in the tests: three `}}}` in a row need `$$$` or concatenation (CS9007).
- Building the SDK project writes `bin/` and `obj/` under the plugin's `sdk/dotnet/src/MaxioAdvancedBilling/`. The SDK source itself is unedited.

## Live sandbox (read-only smoke run, process stopped)

- Main agent's live write run: `ReadCustomerByReference` returned 404, then `CreateCustomer` succeeded, then `CreateSubscription` **with no `payment_collection_method` returned 422 `["No payment method was on file for the $299.00 balance"]`**. The claim was released, and the concurrent second POST got 409. This falsified the old A7. Fix: send `remittance` (plan row and A7 revised). Test: `Subscribe_creates_customer_and_subscription_and_records_both` asserts `"payment_collection_method":"remittance"` in the body. `A_created_subscription_without_a_product_falls_back_to_the_offered_plan` covers the response with no product.

- The host fails fast: an empty `Maxio__ApiKey` throws `OptionsValidationException: Maxio:ApiKey is not configured` at start, and no listener comes up.
- `GET /api/subscription-plans` returned 200 with `basic-plan` ($29) and `eshop-pro` ($299), `truncated:false`. **Verified (A9): the `handle%3Aeshop-subscribe` path form is accepted (200).** Product ids have been re-seeded (7131000 and 7130999), but the handles are unchanged.
- `GET /api/my-subscriptions` for demouser returned 200 `[]`. **Verified: `ReadCustomerByReference` with no match returns 404.**
- An invalid `productHandle` returned 400 with no claim written and no POST sent.
- Unauthenticated calls return 401.
- The SDK log masks the `reference` and `per_page` query values. No bodies are logged.

## Still open / UNVERIFIED (needs the live write run)

- `CreateSubscription` with `payment_collection_method = remittance` against the sandbox (A7, revised). The site may be on legacy Statements Architecture, where only `invoice`/`automatic` are valid. Also unverified: the shape of `next_assessment_at` and `state`, whether `product` is present in the response, and the 422 text for a duplicate customer reference.
- The adopt-existing-customer path runs live on the next POST, because a Maxio customer for demouser@microsoft.com now exists.
- Whether Maxio rejects a duplicate subscription `reference`. The code does not rely on it.
- Whether `ListCustomerSubscriptions` has a server-side cap (A10).
- The migration was generated, but it has not been applied to a real SQL Server.
