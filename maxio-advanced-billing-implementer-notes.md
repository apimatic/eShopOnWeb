# Maxio Advanced Billing — implementer notes

## Files and their jobs

| File | Job |
| --- | --- |
| `global.json` | `rollForward: latestMajor` (the pinned 8.0.x SDK is not installed; the SDK project needs the .NET 10 SDK for C# 14) |
| `Directory.Build.props` (new) | Defines `MaxioSdkProjectPath`. Default: `<repo>/../marketplace/plugins/maxio-advanced-billing/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj`. You can override it with `/p:MaxioSdkProjectPath=...` or an env var of the same name |
| `src/PublicApi/PublicApi.csproj` | `<ProjectReference Include="$(MaxioSdkProjectPath)" />`. The SDK source is not copied into the repo |
| `src/ApplicationCore/Entities/BillingAggregate/{BillingClaimStatus,BillingCustomerLink,SubscriptionClaim}.cs` | Claim entities. Each primary key is the customer reference (`eshop:<lower-cased user name>`). Each has a `Version` concurrency token |
| `src/ApplicationCore/Interfaces/IBillingClaimStore.cs` | Claim store contract: TryClaim / Find / Update / Release, for customer and subscription |
| `src/ApplicationCore/Exceptions/BillingException.cs` | Caller-safe message, plus the HTTP status to return and the provider status |
| `src/Infrastructure/Data/BillingClaimStore.cs` | EF implementation. When the store refuses a duplicate key it is caught in `TryInsertAsync`, then the key is re-read to confirm it exists |
| `src/Infrastructure/Data/Config/{BillingCustomerLink,SubscriptionClaim}Configuration.cs`, `CatalogContext.cs` | Primary keys, lengths, concurrency tokens, DbSets |
| `src/Infrastructure/Data/Migrations/20261020121001_AddBillingClaims*.cs`, `CatalogContextModelSnapshot.cs` | SQL Server migration for the two tables |
| `src/PublicApi/Billing/MaxioSettings.cs` | `Maxio:` options plus `MaxioSettingsValidator` (fail-fast; messages name keys only) |
| `src/PublicApi/Billing/MaxioCallPolicy.cs` | Code defaults, not config keys: CallBudget 8s, RequestBudget 45s, AttemptTimeout 6s, MaxRetries 2, InFlightWindow 60s, page size 200, page cap 5 |
| `src/PublicApi/Billing/MaxioBillingServiceCollectionExtensions.cs` | `AddMaxioBilling(config)`: options + `ValidateOnStart`, named HttpClient `MaxioAdvancedBilling` (timeout CallBudget+2s, `PooledConnectionLifetime` 5 min), singleton SDK client (`CreateClient`), scoped gateway/store/service |
| `src/PublicApi/Billing/MaxioBillingGateway.cs` | The only class that touches the SDK. Per-call budget, error translation, unknown-outcome settlement inside the write catches, page walk with `Truncated` |
| `src/PublicApi/Billing/SubscriptionService.cs` | Orchestration: plan invariant check → customer claim → lookup/create → subscription claim → live check → create → record. Also settles stale Pending claims and lists my-subscriptions |
| `src/PublicApi/Billing/BillingModels.cs` | App-side DTOs (`BillingPlan`, `PlanCatalog`, `BillingSubscription`, `WriteResult<T>`, `Reconciliation<T>`) |
| `src/PublicApi/SubscriptionEndpoints/*` | `GET api/subscription-plans`, `POST api/subscriptions` (201 created / 200 existing / 202 pending), `GET api/my-subscriptions`. All use JWT `[Authorize]` and follow the existing endpoint convention |
| `src/PublicApi/Middleware/ExceptionMiddleware.cs` | `BillingException` → its HttpStatusCode + message. 409 for `DuplicateException` is unchanged |
| `src/PublicApi/Program.cs` | `builder.Services.AddMaxioBilling(builder.Configuration)` |
| `tests/UnitTests/UnitTests.csproj` | + ProjectReference to PublicApi |
| `tests/UnitTests/PublicApi/Billing/{MaxioStubHandler,FakeMaxio}.cs` | Stubbed HttpClient (route-based, stateful fake Maxio) and per-request scopes sharing one in-memory store |
| `tests/UnitTests/PublicApi/Billing/{DuplicateClaimTests,UnknownOutcomeTests,CatalogAndConfigurationTests}.cs` | 23 tests |
| `tests/PublicApiIntegrationTests/appsettings.test.json` | Fake `Maxio:` placeholders. BaseUrl is `https://maxio.test.invalid` |
| `tests/PublicApiIntegrationTests/SubscriptionEndpoints/SubscriptionEndpointsTest.cs` | 401 without a token on all three routes. 503 with no URL leak when Maxio is unreachable |
| `tests/FunctionalTests/PublicApi/ApiTestFixture.cs` | Same fake placeholders through `ConfigureAppConfiguration` (the Testing environment has no user-secrets) |

## SDK usage

- Client: `MaxioBillingServiceCollectionExtensions.CreateClient` builds `new MaxioAdvancedBillingClient(httpClient, options)` with these settings:
  - `BasicAuth = { Username = Maxio:ApiKey, Password = "x" }`, `Environment = ServerEnvironment.Us`.
  - `Retry = RetryOptions.Default() with { MaxRetries = 2, Timeout = 6s }`. The default verbs are kept, so the SDK never resends a POST.
  - `Logging` with an explicit `LoggerFactory` and all body/header logging off.
  - `Server.Production.{Us,Eu}.BaseUrl = Maxio:BaseUrl` when that key is set, otherwise `.Site = Maxio:Subdomain`.
- Operations used:
  - `ProductFamilies.ListProductsForProductFamily` (`ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`, `PerPage = 200`, `IncludeArchived = false`)
  - `Customers.ReadCustomerByReference`
  - `Customers.CreateCustomer`
  - `Customers.ListCustomerSubscriptions`
  - `Subscriptions.CreateSubscription` (`ProductHandle`, `CustomerId`, `Reference`, `PaymentCollectionMethod = CollectionMethod.Remittance`)
  - `Subscriptions.FindSubscription`
- Configuration keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional). In dev the values come from PublicApi user-secrets, which load only when `ASPNETCORE_ENVIRONMENT=Development`. Deployments use `Maxio__ApiKey` and the other keys the same way.
- DI: `MaxioAdvancedBillingClient` is a singleton built once, so a rotated key needs a restart. `MaxioBillingGateway`, `IBillingClaimStore` → `BillingClaimStore` and `SubscriptionService` are scoped. `MaxioCallPolicy` and `TimeProvider` are singletons via TryAdd.

## Build, test, run

- Build: `DOTNET_ROLL_FORWARD=Major dotnet build eShopOnWeb.sln`. It succeeds with only the 11 existing warnings.
- Test: `DOTNET_ROLL_FORWARD=Major dotnet test eShopOnWeb.sln`. Results:
  - UnitTests: 67/67, including 23 new billing tests.
  - IntegrationTests: 3/3.
  - FunctionalTests: 12/12.
  - PublicApiIntegrationTests: 7/19. The 4 new tests pass. The same 12 tests fail on the untouched baseline too (see pitfalls).
- Run: from `src/PublicApi`, start it detached with `UseOnlyInMemoryDatabase=true`, `ASPNETCORE_ENVIRONMENT=Development`, `DOTNET_ROLL_FORWARD=Major` and `ASPNETCORE_URLS=https://localhost:39843;http://localhost:39844`. Run `dotnet bin\Debug\net8.0\PublicApi.dll` through `Start-Process -PassThru` and keep the PID. Stop it by PID, or by resolving the owner of port 39843 with `Get-NetTCPConnection`.
- Token: `POST /api/authenticate` with `{"username":"demouser@microsoft.com","password":"Pass@word1"}`. `admin@microsoft.com` uses the same password. Both are seeded in `AppIdentityDbContextSeed`.
- EF migration was generated with: `dotnet ef migrations add AddBillingClaims --context CatalogContext --project src/Infrastructure/Infrastructure.csproj --startup-project src/PublicApi/PublicApi.csproj --output-dir Data/Migrations`.

## Live sandbox results (2026-10-20, in-memory DB)

- `GET /api/subscription-plans` → 200. Two plans: basic-plan 2900 cents and eshop-pro 29900 cents, monthly. `truncated` is false.
- `POST /api/subscriptions {"planHandle":"eshop-pro"}` as demouser → 201 `created`. Subscription 94908241, state active, 29900 USD, `nextBillingAt` 2026-11-20.
- The same POST again → 200 `existing`, same id. No second POST reached Maxio.
- `GET /api/my-subscriptions` → 200 with that one subscription.
- Two concurrent POSTs as admin (new user) → one 201 `created` (subscription 94908242) and one 409 ("billing account being set up"). The Maxio log shows exactly 1 POST customers.json and 1 POST subscriptions.json for admin. `GET my-subscriptions` shows one subscription.

## Pitfalls hit

- **Without a payment collection method, the subscription was refused.** The first live CreateSubscription got a 422: "No payment method was on file for the $299.00 balance". Fixed by setting `PaymentCollectionMethod = CollectionMethod.Remittance`. That is the documented field, and `remittance` is documented as valid for Relationship Invoicing. The plan's contract row is corrected (Step 4). Plan Assumption 10 still describes the old expectation.
- **What the in-memory EF provider raises on a duplicate primary key.** It throws `ArgumentException` ("An item with the same key has already been added"), not `DbUpdateException`. `TryInsertAsync` catches `DbUpdateException or ArgumentException or InvalidOperationException` and re-reads the key before treating the failure as a refusal.
- **Expected refusals log at error level.** EF Core logs an `Microsoft.EntityFrameworkCore.Update` "fail" line for each claim refusal. This is noise, not a fault.
- **Existing PublicApiIntegrationTests fail before any change.** 12 of them fail because `ResponseBodyPipeWriter does not implement PipeWriter.UnflushedBytes`: the net8 TestServer runs on the .NET 10 runtime under roll-forward and any JSON-serialising response returns 500. This is unrelated to billing. The new tests avoid JSON response bodies.
- **The first migration pulled in unrelated changes.** `dotnet ef` also scaffolded existing model drift as `AlterColumn` on Orders, OrderItems and Catalog. I removed those operations from the migration (unrelated and possibly data-losing). The snapshot does include the drift.
- **Startup validation breaks test hosts without settings.** `ValidateOnStart` means every test host needs `Maxio:` placeholders. PublicApiIntegrationTests runs in Development, so user-secrets load, but `appsettings.test.json` is added later and overrides them. As a result the real key is never used and `BaseUrl` points to `.invalid`.
- **Two test-only syntax problems.** C# raw interpolated strings need more `$` than the longest run of `}` in their content, which bit me in `FakeMaxio`. A stub `On` overload was ambiguous with throw-only lambdas, so I renamed it `OnSync`.
- **Stopping the app.** Killing by image name is blocked here. Stop by PID or by the owner of port 39843.

## Open items

- `ReadCustomerByReference` returns 404 when no customer exists. The live log confirms this, but the plan row still says UNVERIFIED (I left that row alone). Whether a duplicate `CreateCustomer` reference is rejected with 422, and whether subscription `reference` is unique, were not exercised live.
- This run created sandbox data: customers `eshop:demouser@microsoft.com` and `eshop:admin@microsoft.com`, and subscriptions 94908241 and 94908242, both active. A customer for demouser was also created by the first 422 attempt and later adopted.
- After a restart the in-memory claim tables are empty. Idempotency then relies on the Maxio customer lookup and the live-subscription check, which run after the claim is taken.
