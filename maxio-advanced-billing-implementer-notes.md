# Implementer notes — Maxio recurring subscriptions (eShopOnWeb)

No secret values are recorded here (names only).

## Files added / changed and their job
- `global.json`: `rollForward` -> `latestMajor` (only SDK 9/10 installed).
- `src/Infrastructure/Infrastructure.csproj`: ProjectReference to the plugin-local SDK csproj (machine-local path under `marketplace/plugins/maxio-advanced-billing/sdk/dotnet`). `tests/UnitTests/UnitTests.csproj`: reference to Infrastructure.
- ApplicationCore: `Billing/BillingModels.cs` (app DTOs), `Billing/BillingExceptions.cs` (request / in-progress / rejected / provider-kind exceptions), `Entities/BillingAggregate/{SubscriptionClaim,MaxioCustomerClaim}.cs`, `Interfaces/{IBillingGateway,IBillingClaimStore}.cs`, `Services/SubscriptionService.cs` (use cases, claim -> provider call -> record, per use-case time budget 30 s subscribe / 15 s reads, linked to `RequestAborted`).
- Infrastructure: `Billing/MaxioSettings.cs` (section `Maxio`, validation naming keys only), `Billing/MaxioServiceCollectionExtensions.cs` (`AddMaxioBilling`: eager fail-fast validation, named HttpClient 10 s + `PooledConnectionLifetime` 5 min, singleton SDK client, retry 2x/10 s per attempt, explicit `LoggerFactory`, no body/header logging), `Billing/MaxioBillingGateway.cs` (all SDK calls + exception translation + unknown-outcome re-reads), `Data/BillingClaimStore.cs`, `Data/Config/SubscriptionClaimConfiguration.cs` (both entity configs), `Data/CatalogContext.cs` (two DbSets).
- PublicApi: `SubscriptionEndpoints/` (`ListSubscriptionPlansEndpoint`, `SubscribeEndpoint`, `ListMySubscriptionsEndpoint`, DTOs, `BillingCaller`), `Middleware/ExceptionMiddleware.cs` (billing exception -> 400/409/422/502/503/504, fixed generic messages), `Program.cs` (MAXIO_* alias mapping, `AddMaxioBilling`).
- Tests: `tests/UnitTests/ApplicationCore/Billing/*`, `tests/UnitTests/Infrastructure/Billing/*`, `tests/PublicApiIntegrationTests/SubscriptionEndpoints/*`; `tests/PublicApiIntegrationTests/appsettings.test.json` carries dummy placeholder `Maxio` values so the host boots.

## SDK calls, config, registrations
- Calls: `ProductFamilies.ListProductsForProductFamily` (`handle:{Maxio:ProductFamilyHandle}`, 200/page, max 10 pages), `Customers.ReadCustomerByReference` / `CreateCustomer` / `ListCustomerSubscriptions`, `Subscriptions.FindSubscription` / `CreateSubscription`.
- Config keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional, verbatim `Server.Production.<Env>.BaseUrl`). Environment: `MAXIO_ENVIRONMENT` (`us`/`eu`, default us) read as a plain key (not a `Maxio:` key). Aliases `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` fill blank `Maxio:*` keys.
- User-secrets (PublicApi, `UserSecretsId` already present) set with `dotnet user-secrets set` from the env vars: the four `Maxio:`/alias mappings plus key `MAXIO_ENVIRONMENT`. `Maxio:BaseUrl` not set.
- DI: `AddMaxioBilling` registers `MaxioSettings` singleton, named HttpClient, `MaxioAdvancedBillingClient` singleton, `IBillingGateway` singleton, `IBillingClaimStore` + `SubscriptionService` scoped. `IAppLogger<>` is registered by PublicApi `Program.cs` already.

## Build / test / run
- Build: `dotnet build eShopOnWeb.sln`. Tests: `DOTNET_ROLL_FORWARD=Major dotnet test eShopOnWeb.sln` (88 unit, 3 integration, 12 functional, 20 PublicApi integration: all pass).
- Run: `ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true ASPNETCORE_URLS="https://localhost:39803;http://localhost:39804" DOTNET_ROLL_FORWARD=Major dotnet src/PublicApi/bin/Debug/net8.0/PublicApi.dll` (detached; Development so user-secrets load). Demo user from seed data authenticates at `POST /api/authenticate`.

## Pitfalls / decisions
- EF in-memory provider raises `ArgumentException` ("same key already added") at save for a duplicate PK (SQL Server: `DbUpdateException`; same-context tracking: `InvalidOperationException`). `BillingClaimStore` catches all three, detaches the failed entity and reads the holder with `AsNoTracking`.
- Claim store is an interface (`IBillingClaimStore`) rather than `IRepository<>` so the failed insert can be detached and stale claims released only if their `ClaimedAtUtc` still matches.
- `ValidateOnStart` was not used (Infrastructure has no hosting package); validation is eager inside `AddMaxioBilling`.
- `IEndpoint<...>` of MinimalApi.Endpoint allows at most 3 handler parameters; the `UserManager<ApplicationUser>` is resolved from `HttpContext.RequestServices` in `BillingCaller`.
- Raw string literals in tests: single-line `"""[...` is invalid; used normal strings.
- Live finding: Maxio refused `CreateSubscription` for the priced plan with 422 "No payment method was on file". Fix: `PaymentCollectionMethod = CollectionMethod.Remittance` (YOUR CALL; legacy-architecture sites would need `Invoice`). Sandbox then answered 201, state active.
- Live results: list plans 200; subscribe 201; repeat subscribe 200 same id; 3 parallel subscribes of a new plan: one 201, two 409 (claim in progress); my-subscriptions 200 lists both; unknown plan 400; no token 401 on all three; after restart (claims lost) repeat subscribe 200 same id via reference reconciliation. No key/e-mail in logs. Sandbox now holds one customer and two subscriptions (eshop-pro, basic-plan) for the demo user.
- Customer first name = e-mail local part, last name = fixed placeholder (token has no name data).
- `PHASE-BUILD.md` (pre-existing, committed before this work) contains the site/family values; not touched.

## Open
- Real SQL Server deployment needs an EF migration for `SubscriptionClaims` and `MaxioCustomerClaims` (in-memory ignores migrations).
- SDK reference is a machine-local path into the plugin install.
