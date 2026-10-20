# Implementer notes: Maxio subscriptions in eShopOnWeb PublicApi

## Files
- `global.json`: rollForward `latestMajor` (only .NET 10 SDK installed). Run everything with `DOTNET_ROLL_FORWARD=Major`.
- `src/Infrastructure/Infrastructure.csproj`: ProjectReference to the SDK source in the plugin, as a RELATIVE path (`..\..\..\marketplace\plugins\maxio-advanced-billing\sdk\dotnet\src\MaxioAdvancedBilling\MaxioAdvancedBilling.csproj`). The SDK is not on NuGet (getting-started); local to this machine layout (marketplace is a sibling of repo).
- `src/ApplicationCore/Subscriptions/*`: `ISubscriptionService`, models (`SubscriptionPlan`, `SubscriptionInfo`, ...), `SubscriptionException` (carries HTTP status + caller-safe message).
- `src/ApplicationCore/Entities/MaxioSubscriptionLink.cs` + `src/Infrastructure/Data/Config/MaxioSubscriptionLinkConfiguration.cs` + DbSet in `CatalogContext`: claim row, PK = user id. No EF migration (in-memory only here; SQL Server needs one for table `MaxioSubscriptionLinks`).
- `src/Infrastructure/Maxio/MaxioSettings.cs`, `MaxioServiceCollectionExtensions.cs` (`AddMaxioSubscriptions`), `MaxioSubscriptionService.cs` (all SDK calls, claim, reconcile, error boundary, budgets).
- `src/PublicApi/SubscriptionEndpoints/*`: 3 endpoints (GET api/subscription-plans, POST api/subscriptions, GET api/my-subscriptions), DTOs, `SubscriptionApi` (resolves user from JWT `ClaimTypes.Name` via `UserManager.FindByNameAsync`).
- `src/PublicApi/MaxioEnvironmentConfiguration.cs`: maps MAXIO_API_KEY / MAXIO_SITE_SUBDOMAIN / MAXIO_DEFAULT_PRODUCT_FAMILY onto `Maxio:*` only when the key is unset (names only, no values). Called in `Program.cs` after `AddEnvironmentVariables()`.
- `src/PublicApi/Middleware/ExceptionMiddleware.cs`: `SubscriptionException` -> its status.
- Tests: `tests/UnitTests/Fakes/FakeMaxioHandler.cs` (fake Maxio HTTP API; also compiled into the PublicApi integration tests via linked file), `tests/UnitTests/Infrastructure/MaxioSubscriptionServiceTests.cs` (17 tests), `tests/PublicApiIntegrationTests/SubscriptionEndpoints/SubscriptionEndpointsTest.cs` (5 tests, replaces the singleton SDK client with one over the fake), dummy `Maxio` section in `tests/PublicApiIntegrationTests/appsettings.test.json` (obvious placeholders).

## SDK calls / config / registrations
- Calls: `ProductFamilies.ListProductsForProductFamily` (family id `handle:<Maxio:ProductFamilyHandle>`, PerPage 200, cap 5 pages, `IsPartial`), `Customers.ReadCustomerByReference` (404 = absent), `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` (ProductHandle, CustomerId, Reference, PaymentCollectionMethod=Remittance), `Subscriptions.ReadSubscription`.
- Keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional, verbatim into `Server.Production.Us.BaseUrl`). Validated at host start (`ValidateOnStart`, message names the key, never the value).
- DI: named HttpClient "MaxioAdvancedBilling" (10s timeout, pooled connection lifetime 5 min), ONE singleton `MaxioAdvancedBillingClient` (Retry timeout 8s, MaxRetries 2, POST never resent, LoggerFactory explicit, no body logging), `TimeProvider` singleton, scoped `ISubscriptionService`, scoped `SubscriptionApi`, `AddHttpContextAccessor`.
- Budgets: reads 20s, subscribe 30s, reconcile reads 10s fresh token.

## Build / test / run
- `export DOTNET_ROLL_FORWARD=Major; dotnet test eShopOnWeb.sln` (61 unit, 3 integration, 12 functional, 20 PublicApi integration; all pass).
- Secrets: `dotnet user-secrets set Maxio:ApiKey <value> --project src/PublicApi` (same for Maxio:Subdomain, Maxio:ProductFamilyHandle); done from env vars without echo.
- Run: `DOTNET_ROLL_FORWARD=Major UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --project src/PublicApi --launch-profile PublicApi` (https://localhost:39743, http 39744; inside APP_PORT_BLOCK range). Token: POST /api/authenticate {"username":"demouser@microsoft.com","password":"Pass@word1"}.

## Pitfalls
- EF in-memory provider throws a raw `ArgumentException` ("An item with the same key has already been added") for a duplicate key across contexts, not `DbUpdateException`. `ClaimAsync` catches `DbUpdateException`, `InvalidOperationException` and `ArgumentException`.
- Live sandbox: `CreateSubscription` without `PaymentCollectionMethod` returned 422 "No payment method was on file for the $299.00 balance" although the plan does not require a card. Fixed with `CollectionMethod.Remittance` (plan A5 corrected).
- Identity DB is in-memory, so the seeded user gets a NEW id each process start; since the Maxio customer reference is the user id, each app restart creates a new Maxio customer for demouser (expected here, not in production). The claim store is in-memory too, so double-subscribe protection across restarts needs a real DB.
- Heredocs containing apostrophes in comments broke one bash call; use the Write tool for files.
- `dotnet dev-certs https --check` says valid but not trusted; curl -k used for live checks.
- Maxio-side reference uniqueness is still UNVERIFIED; protection is the app claim row.

## Live verification (sandbox)
Plans listed (basic-plan, eshop-pro isDefault); concurrent double POST -> one 201 + one 409; repeat POST -> 200 created=false same subscription id; different plan -> 409; my-subscriptions returns live state active with next billing date; unknown plan -> 400; no token -> 401. Verification left a few demo customers/subscriptions in the sandbox.

## Open
- No EF migration for SQL Server.
- Stopped the app after verification (no instance left running).
