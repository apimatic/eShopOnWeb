# Maxio Advanced Billing: implementer notes

Plan: `maxio-advanced-billing-plan.md` (same folder). Run 1 changed only its `TBD` cells. Fix round 1 made a Step 4 correction: the CreateSubscription row now carries `PaymentCollectionMethod`, the `CollectionMethod` enum table was added, and §6 assumption 6 records the live 422.

## Files

Added
- `src/ApplicationCore/Entities/BillingClaimAggregate/BillingClaim.cs`: abstract claim (string PK `Reference`, `Status`, `ProviderId`, timestamps, `AttemptStartedAt`, `Version` concurrency token; `MarkCreated` / `MarkUnknown` / `TakeOver` / `IsStale`).
- `.../BillingClaimStatus.cs` (`Pending`, `Created`, `Unknown`), `.../BillingCustomerClaim.cs`, `.../BillingSubscriptionClaim.cs` (+ `CustomerReference`, `PlanHandle`).
- `src/ApplicationCore/Interfaces/IBillingClaimStore.cs`: `TryInsertAsync` / `FindAsync` / `TryUpdateAsync` / `ReleaseAsync`.
- `src/Infrastructure/Data/BillingClaimStore.cs`: EF implementation over `CatalogContext`. Duplicate PK is caught as `DbUpdateException` (SQL 2627/2601), or as `ArgumentException` on the in-memory provider. Concurrency conflict is caught as `DbUpdateConcurrencyException`.
- `src/Infrastructure/Data/Config/BillingClaimConfiguration.cs`: tables `BillingCustomerClaims` and `BillingSubscriptionClaims`, PK = `Reference`, `Version` set with `IsConcurrencyToken`.
- `src/Infrastructure/Data/Migrations/20261020052157_AddBillingClaims(.Designer).cs`: creates the two tables only. The Designer target model equals the snapshot (see Pitfalls).
- `src/PublicApi/Billing/`
  - `MaxioSettings.cs`: settings plus `MaxioSettingsValidator` (`IValidateOptions`). Error messages name the key and never include its value.
  - `MaxioServiceCollectionExtensions.cs`: `AddMaxioSubscriptionBilling(IServiceCollection, IConfiguration)` and `CreateClientOptions(...)`, which DI and the tests share.
  - `MaxioBillingGateway.cs`: the only class that touches the SDK. It also holds `MaxioGatewayLimits`. `IMaxioBillingGateway.cs` holds the interface and `SubscriptionLookup`.
  - `SubscriptionBillingService.cs` (`ISubscriptionBillingService`): runs the claim, then the SDK call, then records the result.
  - `BillingModels.cs`: provider-neutral records plus `WriteOutcome<T>` (`Created`, `AlreadyExisted`, `Refused`, `Unknown`).
  - `BillingExceptions.cs`: `BillingProviderException` (`Kind`), `InvalidSubscriptionRequestException`, `BillingOperationPendingException` (`InFlight`, `OutcomeUnknown`).
  - `BillingReferences.cs`: `eshop-user-` + 32 hex chars of SHA-256(upper-invariant trimmed user name); `eshop-sub-` + 32 hex chars of SHA-256(customerRef + "|" + lowercase plan handle).
  - `ShopperProfile.cs`: `ShopperProfile.FromEmail` (name from the email local part, falling back to "eShop" "Customer") and `IdentityShopperProfileProvider`. Email comes from `UserManager.FindByNameAsync`; if that has none, the user name is used when it is an email.
- `src/PublicApi/SubscriptionEndpoints/`: `SubscriptionPlanListEndpoint`, `CreateSubscriptionEndpoint`, `MySubscriptionListEndpoint` (+ request/response files in the repo's `Endpoint.Xxx.cs` style), `SubscriptionDtos.cs` (DTOs + `PriceFormatter`).
- `tests/PublicApiUnitTests/` (xUnit, new; added to `eShopOnWeb.sln` and `Everything.sln` under `tests`): `Billing/MaxioStub.cs` (StubHandler routes on METHOD + unescaped path), `BillingClaimStoreTests`, `MaxioBillingGatewayTests`, `SubscriptionBillingServiceTests`, `MaxioSettingsAndReferencesTests`.
- `tests/PublicApiIntegrationTests/SubscriptionEndpoints/SubscriptionEndpointsTest.cs`: 401 without a token, 400 without `planHandle`, the swagger document lists the routes, and the host refuses to start with a blank `Maxio:ApiKey`.

Changed
- `global.json`: `rollForward: latestMajor`.
- `src/PublicApi/PublicApi.csproj`: SDK `<ProjectReference Include="$(MaxioSdkProjectPath)">`, where the property defaults to `$(MSBuildThisFileDirectory)..\..\..\marketplace\plugins\maxio-advanced-billing\sdk\dotnet\src\MaxioAdvancedBilling\MaxioAdvancedBilling.csproj`. The `EnsureMaxioSdkProjectExists` target fails with a clear error if the path is missing.
- `src/PublicApi/Program.cs`: `builder.Services.AddMaxioSubscriptionBilling(builder.Configuration);`
- `src/PublicApi/Middleware/ExceptionMiddleware.cs`: billing exception arms plus `BillingErrorDetails`.
- `src/Infrastructure/Data/CatalogContext.cs`: two DbSets.
- `src/Infrastructure/Data/Migrations/CatalogContextModelSnapshot.cs`: the HEAD snapshot plus the two billing entity blocks (+76 lines, nothing else changed). This was fixed in round 1.
- `tests/PublicApiIntegrationTests/ProgramTest.cs`, `tests/FunctionalTests/PublicApi/ApiTestFixture.cs`: non-secret placeholder `Maxio:*` values via `ConfigureAppConfiguration` + `AddInMemoryCollection`. They are appended last, so they override local user-secrets.
- `src/PublicApi/README.md`: endpoints, error statuses, config keys, user-secrets commands with placeholders, and the machine-local SDK note.

## SDK usage

- Client: `new MaxioAdvancedBillingClient(httpClient, options)` as a singleton over the named HttpClient `"MaxioAdvancedBilling"` (`HttpClient.Timeout` 15 s, `SocketsHttpHandler.PooledConnectionLifetime` 5 min). `AddMaxioAdvancedBillingClient` is not used: it would put the SDK on the shared default client.
- Options: `Environment = ServerEnvironment.Us`; `Retry = RetryOptions.Default() with { Timeout = 10s }` (verbs unchanged, so POST is never resent); `Logging = new LoggingOptions { LoggerFactory = <container>, LogRequestBody = false, headers off }`; `BasicAuth = new BasicAuthCredentials { Username = Maxio:ApiKey, Password = "x" }`. The base address is `Server.Production.Us.BaseUrl = Maxio:BaseUrl` (verbatim) when set, otherwise `Server.Production.Us.Site = Maxio:Subdomain`.
- Calls:
  - `ProductFamilies.ListProductsForProductFamily` (`ProductFamilyId = "handle:" + family`, `Page`, `PerPage = 200`, cap 5 pages).
  - `Customers.ReadCustomerByReference` (404 means null).
  - `Customers.CreateCustomer` (`CreateCustomerOperationRequest { Body = CreateCustomerRequest { Customer = CreateCustomer {FirstName, LastName, Email, Reference} } }`).
  - `Subscriptions.FindSubscription` (`TryGetNoContent` means not found).
  - `Subscriptions.CreateSubscription` (`ProductHandle`, `CustomerId`, `Reference`, `PaymentCollectionMethod = CollectionMethod.Remittance`). Remittance comes from the gateway constant `SubscriptionCollectionMethod`, not from config. The response's `Subscription.PaymentCollectionMethod?.Value` is mapped to `BillingSubscription.PaymentCollectionMethod` and then to `SubscriptionDto.PaymentCollectionMethod`.
  - `Customers.ListCustomerSubscriptions`.
- Budget: the gateway is scoped, and one `CancellationTokenSource(30 s)` is linked with `HttpContext.RequestAborted` for every SDK call in the request (`Bounded`). The settlement re-read gets a fresh 10 s token.
- DI registrations:
  - `AddOptions<MaxioSettings>().Bind("Maxio").ValidateOnStart()` + `IValidateOptions<MaxioSettings>`
  - the SDK client singleton
  - `MaxioGatewayLimits` singleton
  - scoped `IMaxioBillingGateway`, `IBillingClaimStore`, `IShopperProfileProvider`, `ISubscriptionBillingService`
  - `TryAddSingleton(TimeProvider.System)`
- Config keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`. `MAXIO_ENVIRONMENT` is not bound (see plan §6).
- Error mapping (middleware):
  - missing or unknown plan: 400
  - provider refusal: 422 with `Errors`
  - auth/config/5xx/unreadable: 502
  - throttled, or catalog truncated: 503
  - timeout: 504
  - unknown outcome: 202 `pending_confirmation` + reference
  - in flight: 409 `in_progress`
  - stray `SdkException`: 502 generic

## Build / test / run

- Build: `dotnet build eShopOnWeb.sln` from the repo root (the .NET 10 SDK is picked through `latestMajor`). Elsewhere, add `-p:MaxioSdkProjectPath=<path>/MaxioAdvancedBilling.csproj`.
- Tests: `DOTNET_ROLL_FORWARD=Major dotnet test eShopOnWeb.sln`. Last run (fix round 1): UnitTests 44, IntegrationTests 3, PublicApiUnitTests 56, PublicApiIntegrationTests 21, FunctionalTests 12, all passing (baseline before changes: 44/3/12/15).
- Migration check (no DB needed): `dotnet ef migrations has-pending-model-changes -p src/Infrastructure/Infrastructure.csproj -s src/PublicApi/PublicApi.csproj -c CatalogContext --no-build`. It reports pending changes, and those changes are the pre-existing drift that was already pending at HEAD. A throwaway `migrations add ProbeDrift` scaffolded only 5 AlterColumns (Orders.ShipToAddress_State, OrderItems.ItemOrdered_ProductName/PictureUri/CatalogItemId, Catalog.Description) and no billing ops. The probe was deleted and the snapshot restored.
- Run (orchestrator): `UseOnlyInMemoryDatabase=true`, `ASPNETCORE_ENVIRONMENT=Development` (loads user-secrets), port 39683/39684 from launchSettings. Get a token from `POST api/authenticate`, then call `GET api/subscription-plans`, `POST api/subscriptions {"planHandle":"eshop-pro"}`, `GET api/my-subscriptions`.

## Pitfalls hit

- Bash heredocs containing C# with quotes broke the shell parse. Used the Write tool for source files.
- The SDK percent-encodes the path template value: `handle:eshop-subscribe` goes on the wire as `/product_families/handle%3Aeshop-subscribe/products.json`. The test stub matches on the unescaped path.
- C# raw interpolated strings with `}}}` endings need `$$$` or concatenation (CS9007).
- EF in-memory duplicate PK from a second context throws `ArgumentException`, not `DbUpdateException`. This is pinned by a test, and the store catches it only when `Database.IsInMemory()`.
- `dotnet ef migrations add` (dotnet-ef 9.0.7, startup project PublicApi, no DB needed) also scaffolds unrelated older nullability drift (AlterColumn on Orders/OrderItems/Catalog) and rewrites annotations (ProductVersion 8.0.2, UseIdentityColumn without seed/increment). Run 1 dropped the ops but kept the regenerated snapshot, so the snapshot and the migrations disagreed. Round 1 fix: the snapshot is now `git show HEAD:` snapshot + the billing entity blocks, and the Designer `BuildTargetModel` body is the same text. The drift stays visible to a future `migrations add`. Repeat this hand-merge whenever a migration is scaffolded on top of this snapshot.
- On Windows, the Python in PATH does not see Git Bash `/tmp`. Pipe `git show` through Python's subprocess, or use a Windows path.
- Live sandbox: `CreateSubscription` with the default (automatic) collection was refused with 422 "No payment method was on file for the $299.00 balance". The fix is `PaymentCollectionMethod = Remittance`. The 422 path releases the claim, and this is pinned by `SubscriptionBillingServiceTests.Provider422OnCreateSubscriptionReleasesTheClaimSoTheNextAttemptReachesTheProviderAgain`.
- `dotnet sln add` rewrote the whole platform matrix (x64/x86). Reverted it and added the project entries by hand (7 lines per .sln).
- The SDK's own `Directory.Build.props`/`Directory.Packages.props` (C# 14, CPM, TreatWarningsAsErrors) apply to the SDK project only, because MSBuild walks up from the SDK's own directory. It builds cleanly next to the repo's CPM. The only new warning is NU1507 (two package sources).
- Settings binding is lazy, so the test placeholders (added through `ConfigureAppConfiguration` at Build time) are seen by `ValidateOnStart`.

## Open / UNVERIFIED (check on the live run)

- Verified live: the encoded `handle%3A<family>` path is accepted, and `ReadCustomerByReference` 404 gives an empty list. The concurrent double POST gives one 409 `in_progress`.
- Maxio stores the subscription `reference` and `FindSubscription?reference=` finds it. If not, the code falls back to scanning `ListCustomerSubscriptions`.
- `CreateSubscription` with `payment_collection_method: remittance` and no payment profile is accepted for `eshop-pro`/`basic-plan` (the site must be on Relationship Invoicing; if it is on legacy Statements, the valid value would be `invoice`).
- The new subscription's state and `next_assessment_at` values.
- The `currency` field is present.
- A double-click returns one 201 and one 200 (or a 409 `in_progress` if truly concurrent), with a single Maxio customer and subscription.
- `ListCustomerSubscriptions` completeness (no paging inputs).
- Claims are process-local under `UseOnlyInMemoryDatabase=true`. The migration is generated but has not been applied to a SQL Server.
- The PublicApi Dockerfile cannot see the SDK.
