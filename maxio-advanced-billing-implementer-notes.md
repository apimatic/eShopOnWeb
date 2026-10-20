# Implementer notes: Maxio subscriptions on PublicApi

## Files
- ApplicationCore: `Entities/SubscriptionEnrollment.cs` (one row per user, PK = user id, status Claimed/Active/Unknown), `Interfaces/ISubscriptionEnrollmentStore.cs`.
- Infrastructure: `Data/SubscriptionEnrollmentStore.cs` (claim = insert; PK refusal caught), `Data/Config/SubscriptionEnrollmentConfiguration.cs`, `CatalogContext.SubscriptionEnrollments`, hand-written migration `Data/Migrations/20261020094548_AddSubscriptionEnrollment.cs` + snapshot entry (the scaffolded one dragged in unrelated pre-existing model drift, so it was discarded).
- PublicApi `Subscriptions/`: `MaxioSettings` (+ validator), `MaxioServiceCollectionExtensions.AddMaxioSubscriptions` (options validation on start, named HttpClient 10s + PooledConnectionLifetime, singleton SDK client), `MaxioBillingGateway` (only class touching the SDK; maps every SDK failure to `MaxioException` Rejected/Unavailable/OutcomeUnknown), `SubscriptionService` (claim -> customer -> subscription -> record; 30s `Bounded`, settle by re-read with a fresh 10s token), `SubscriptionModels`.
- PublicApi `SubscriptionEndpoints/`: three MinimalApi `IEndpoint` classes (JWT scheme explicit) + DTOs. Caller id: JWT name claim -> `UserManager.FindByNameAsync`.
- `Program.cs`: `builder.Services.AddMaxioSubscriptions(builder.Configuration)`.
- `PublicApi.csproj`: ProjectReference to the SDK csproj by relative path (`..\..\..\marketplace\plugins\maxio-advanced-billing\sdk\dotnet\...`); machine-local. SDK is not under CPM so no package-version clash.
- `global.json`: rollForward set to latestMajor (SDK needs .NET 10 SDK, LangVersion 14).
- Tests: `tests/UnitTests/PublicApi/Subscriptions/*` (stubbed HttpClient + fake claim store + EF in-memory store tests); UnitTests.csproj references PublicApi. Dummy fake Maxio config in `tests/PublicApiIntegrationTests/appsettings.test.json` and `tests/FunctionalTests/PublicApi/ApiTestFixture.cs`.

## Config keys
`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional, verbatim base address). Values only in user-secrets (UserSecretsId in PublicApi.csproj) or env.

## Build / test / run
- `set DOTNET_ROLL_FORWARD=Major`; `dotnet test eShopOnWeb.sln`.
- Run: env `ASPNETCORE_ENVIRONMENT=Development` (loads user-secrets), `UseOnlyInMemoryDatabase=true`, `ASPNETCORE_URLS=http://localhost:<port in 39740..39759>`, `dotnet src/PublicApi/bin/Debug/net8.0/PublicApi.dll`. Login: POST /api/authenticate (demouser@microsoft.com, default seeded password from `AuthorizationConstants.DEFAULT_PASSWORD`).

## Pitfalls
- `FindSubscription` route is `/subscriptions/lookup.json` (a stub route of `/find` silently matched ReadSubscription).
- EF in-memory duplicate key throws a bare `ArgumentException` at SaveChanges; same-context duplicate throws `InvalidOperationException` at `Add`; SQL Server gives `DbUpdateException`. All three are caught in `TryClaimAsync` (Add is inside the try).
- Python/sed edits corrupted a `\t` in a Windows path once; edit csproj paths with the Edit tool.
- Baseline: 12 `PublicApiIntegrationTests` failures (catalog 500s, auth) exist on the untouched repo too (same count), unrelated to this work.
- `dotnet ef migrations add` scaffolds unrelated drift (Orders/Catalog columns); do not regenerate blindly.

## Live smoke (sandbox, user-secrets)
GET plans OK (2 plans, IsComplete true); no token -> 401; missing/unknown handle -> 400; parallel double POST -> one 409 `in_progress` (claim refused, no Maxio call) and one proceeds; (historical, fixed below) POST failed with 422 from Maxio: "No payment method was on file for the ... balance" even though `require_credit_card` is false, so the plan's assumption "no payment method needed" is refuted. Claim is released after the 422 (retry gets a fresh attempt). One customer was created in the sandbox (reused by reference on later attempts). Success path (state, next billing date) NOT verified live.

## Payment fix (follow-up)
- `MaxioBillingGateway.CreateSubscriptionAsync` sets `CreateSubscription.PaymentCollectionMethod = MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance` (SDK constant). Unit test asserts `"payment_collection_method":"remittance"` in the outgoing body. 71 UnitTests pass.
- Live sandbox: authenticate, GET plans, POST basic-plan twice (second returns same subscription, 200), GET my-subscriptions: state active, 2900 cents/month, next billing date 1 month out. After host restart (in-memory DB reset) the concurrent pair on eshop-pro gave one 200 (active, 29900) and one 409 in_progress. Remittance was accepted by the sandbox; `Invoice` not needed.
- Pitfall: `kill`/`$!` of a nohup'd dotnet in Git Bash is not the dotnet PID; find it via `netstat -ano` and `taskkill //PID`. A stale host on the port makes a restarted host fail to bind silently. Do not `wait` with a background host in the same shell.
- The Maxio sandbox customer now has two sandbox subscriptions (basic-plan, eshop-pro) from the smoke runs.

## Open
- `NextAssessmentAt` null in a 2xx is returned as null (not re-read); only a missing subscription/id triggers the re-read.
- No `Maxio:Environment` key: Eu only via `Maxio:BaseUrl`.
