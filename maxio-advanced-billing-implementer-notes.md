# Maxio Advanced Billing implementer notes

## Files (added/changed) and their job
- `global.json` rollForward -> `latestMajor` (net8 TFM kept; machine has only .NET 9/10).
- `src/Infrastructure/Infrastructure.csproj` ProjectReference to the plugin SDK csproj (machine-local path, re-point with `dotnet remove/add reference` elsewhere).
- `src/ApplicationCore/Entities/SubscriptionClaim.cs`, `CustomerLink.cs` (+ `ClaimStatus`): claim rows, string primary keys.
- `src/ApplicationCore/Interfaces/ISubscriptionService.cs`, `Subscriptions/SubscriptionModels.cs`, `Subscriptions/SubscriptionException.cs`: service contract, DTOs, domain failures.
- `src/Infrastructure/Data/CatalogContext.cs` (+2 DbSets), `Data/Config/SubscriptionClaimConfiguration.cs` (both EF configs).
- `src/Infrastructure/Billing/MaxioOptions.cs` (options + `IValidateOptions`), `MaxioBillingExtensions.cs` (`AddMaxioBilling`, `AddMaxioEnvironmentVariableMapping`), `MaxioSubscriptionService.cs` (all SDK calls, claims, reconcile, error translation).
- `src/PublicApi/Program.cs` (env-name mapping + `AddMaxioBilling`), `Middleware/ExceptionMiddleware.cs` (SubscriptionException -> HTTP), `SubscriptionEndpoints/*` (3 endpoints, JWT required on all three).
- Tests: `tests/UnitTests/Billing/FakeMaxio.cs` (in-memory Maxio HTTP fake, also linked into the integration project), `MaxioSubscriptionServiceTests.cs`; `tests/PublicApiIntegrationTests/SubscriptionEndpoints/*`, `appsettings.test.json` (dummy `Maxio:*`), `ApiTokenHelper.GetTokenFor`; `tests/FunctionalTests/PublicApi/ApiTestFixture.cs` (dummy `Maxio:*`).

## SDK calls, config, registrations
- Calls: `ProductFamilies.ListProductsForProductFamily` (`ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`, 200/page, cap 5 pages), `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` (sets `ProductHandle`, `CustomerId`, `Reference`, `PaymentCollectionMethod = CollectionMethod.Remittance`).
- Keys: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (verbatim override of `options.Server.Production.Us.BaseUrl`; then Subdomain is not required). `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` map onto the keys as the LOWEST-precedence source (inserted at index 0 of the configuration sources); user-secrets, appsettings, `Maxio__*` win. `MAXIO_ENVIRONMENT` is unused.
- Registered: `IOptions<MaxioOptions>` + validator + `ValidateOnStart`, named HttpClient `MaxioAdvancedBilling` (10 s timeout, `PooledConnectionLifetime` 5 min), singleton `MaxioAdvancedBillingClient` (options built once; GET retries 2, per-attempt 10 s, no body logging, explicit `LoggerFactory`), scoped `ISubscriptionService`, `TimeProvider.System`.
- Budgets: GET paths 25 s, subscribe 40 s, reconcile reads fresh 10 s token. Stale Pending 5 min, Unknown expiry 30 min.
- User secrets are only auto-loaded when `ASPNETCORE_ENVIRONMENT=Development` (launchSettings do that). Run the app in Development.

## Build / test / run
- `export DOTNET_ROLL_FORWARD=Major`; `dotnet build eShopOnWeb.sln`; `dotnet test eShopOnWeb.sln`.
- Run: `UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:39784 dotnet src/PublicApi/bin/Debug/net8.0/PublicApi.dll` (port block base 39780, size 20). Stop the app BEFORE rebuilding (the dll is locked otherwise).
- Results: UnitTests 78 passed; IntegrationTests 3 passed; FunctionalTests 12 passed; PublicApiIntegrationTests 16 passed / 12 failed. The 12 failures are the pre-existing original tests: verified identical on a pristine worktree of HEAD (12 of 15 fail). Cause: the net8 `TestServer` response pipe lacks `PipeWriter.UnflushedBytes`, which the .NET 9/10 runtimes need to write 2xx JSON bodies ("The PipeWriter 'ResponseBodyPipeWriter' does not implement PipeWriter.UnflushedBytes"). My own success-body tests therefore run the endpoint handlers into a MemoryStream (`SubscriptionEndpointHandlerTests`); error-path tests use TestServer (middleware writes strings).

## Pitfalls and how they were handled
- EF InMemory raises a raw `ArgumentException` (not `DbUpdateException`) for a duplicate primary key on `SaveChangesAsync`; the claim catch includes it (found by live concurrency test: first version returned 500 on a replay).
- `MinimalApi.Endpoint` has `IEndpoint` generics only up to 3 request/dependency args; ClaimsPrincipal + CancellationToken are taken from `HttpContext` (`User`, `RequestAborted`).
- Sandbox answered 422 "No payment method was on file for the $29.00 balance" for the default (automatic) collection; `PaymentCollectionMethod = Remittance` signs up without a card (201, state active). This is a product decision to confirm: constant `SignupCollectionMethod` in `MaxioSubscriptionService`.
- Sandbox: `ReadCustomerByReference` returns 404 for a missing reference (verified); X-Request-ID header is present on provider responses and is logged.
- Dotnet C# 14 / .NET 10 SDK: `string[].Reverse()` binds to `MemoryExtensions.Reverse` (void); use indexing.
- Heredocs through the shell mangled backslashes and apostrophes; project files were edited with the Edit/Write tools.
- Usernames are normalized (trim, lower-case) for keys, references and email, so token-name casing cannot create a second customer.

## Smoke test (live sandbox, in-memory DB, port 39784)
Plans listed eshop-pro + basic-plan (from provider). Subscribe eshop-pro: 201, second identical POST: 200 same subscription id; app restart (claims lost) then POST: 200 same id (provider pre-check, no duplicate); 4 concurrent POSTs for a new user: 1x201 and 3x409 (in progress), later replay 200, exactly one provider subscription; missing planHandle 400; unknown plan 422; non-email token name 422; my-subscriptions returns the user's subscriptions only.

## Open
- Confirm `Remittance` collection (invoice billing) is acceptable, or supply a payment profile flow.
- Plans/subscriptions/my-subscriptions all require a JWT (plans too); relax plans to anonymous if the UI needs it.
- No EF migration (SQL Server mode would need one for the two new tables).
- Sandbox now holds test customers/subscriptions created by the smoke test (demouser plus `smoke.*@example.com`).
