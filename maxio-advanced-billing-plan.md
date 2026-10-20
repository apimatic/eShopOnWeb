# Maxio Advanced Billing plan: eShopOnWeb "Subscribe"

SDK root (plugin-relative): `sdk/dotnet/` (map: `sdk/dotnet/sdk-map.md`, pages `sdk/dotnet/map/operations/*.md`). Every `src/...` path in the sheet is relative to that root. Project root: `C:\claude-runs\t1sub4hamza-t1sub-control-cc-auto-sonnet55medium-002\repo` (net8.0, `global.json` 8.0.x).

## 0. Repo survey (read-only; pattern -> ONE exemplar to imitate)

| Convention | Exemplar |
| --- | --- |
| PublicApi endpoint = MinimalApi.Endpoint `IEndpoint<IResult, TRequest, TDep>` with `AddRoute` (MapGet/MapPost, `.Produces<>().WithTags("...Endpoints")`) plus `HandleAsync`; one folder per area, files `XEndpoint.cs`, `XEndpoint.XRequest.cs`, `XEndpoint.XResponse.cs`, shared `XDto.cs` | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` (+ `.CreateCatalogItemRequest.cs`, `.CreateCatalogItemResponse.cs`); read-only list: `CatalogItemListPagedEndpoint.cs` |
| JWT-protected route: `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` placed on the route lambda (no `UseAuthentication()` in Program; the attribute's scheme does it) | `CreateCatalogItemEndpoint.cs` (`AddRoute`) |
| Request/response base types: `BaseRequest`, `BaseResponse(correlationId)`, `request.CorrelationId()` | `src/PublicApi/BaseRequest.cs`, `BaseResponse.cs`, `BaseMessage.cs` |
| Endpoint auto-registration: `builder.Services.AddEndpoints()` / `app.MapEndpoints()` | `src/PublicApi/Program.cs` |
| Error boundary: `ExceptionMiddleware` maps only `DuplicateException` -> 409, everything else -> 500 echoing `exception.Message` (would leak SDK/provider text; needs new typed mappings) | `src/PublicApi/Middleware/ExceptionMiddleware.cs`, `src/ApplicationCore/Exceptions/DuplicateException.cs` |
| JWT identity: the token carries ONLY `ClaimTypes.Name` (username/email) and `ClaimTypes.Role`; there is no NameIdentifier/sub claim | `src/Infrastructure/Identity/IdentityTokenClaimService.cs`; test twin `tests/PublicApiIntegrationTests/ApiTokenHelper.cs` |
| Identity user type: `ApplicationUser : IdentityUser` (Id GUID, Email, UserName); `UserManager<ApplicationUser>` is already registered in PublicApi | `src/Infrastructure/Identity/ApplicationUser.cs`, `src/PublicApi/Program.cs` |
| Persistence: single `CatalogContext` (EF Core), `DbSet`s, config via `ApplyConfigurationsFromAssembly`; per-entity `IEntityTypeConfiguration<T>` in `Data/Config/`; repository `EfRepository<T>` needs `IAggregateRoot` + `int Id` (`BaseEntity`) | `src/Infrastructure/Data/CatalogContext.cs`, `Data/Config/BasketConfiguration.cs`, `Data/EfRepository.cs`, `ApplicationCore/Entities/BaseEntity.cs` |
| DB selection: `UseOnlyInMemoryDatabase` switch (InMemory vs SqlServer); migrations only under `Data/Migrations` for CatalogContext | `src/Infrastructure/Dependencies.cs`, `src/Infrastructure/Data/Migrations/CatalogContextModelSnapshot.cs` |
| Config binding: `builder.Services.Configure<T>(section)` + `GetRequiredSection`; PublicApi already has `UserSecretsId` (no secret in appsettings) | `src/PublicApi/Program.cs`, `src/PublicApi/PublicApi.csproj` |
| App services in ApplicationCore behind interfaces in `ApplicationCore/Interfaces`, logged through `IAppLogger<T>` | `src/ApplicationCore/Services/BasketService.cs`, `ApplicationCore/Interfaces/IAppLogger.cs` |
| Integration tests (MSTest, `WebApplicationFactory<Program>`, in-memory DB via `appsettings.test.json`, bearer tokens from `ApiTokenHelper`) | `tests/PublicApiIntegrationTests/ProgramTest.cs`, `CatalogItemEndpoints/CreateCatalogItemEndpointTest.cs`, `appsettings.test.json` |
| Unit tests (xunit + NSubstitute) | `tests/UnitTests/ApplicationCore/Services/BasketServiceTests/AddItemToBasket.cs` |

Repo facts that shape the plan: (a) Program.cs has no Maxio code; adding `Maxio:` options with fail-fast would make the existing `PublicApiIntegrationTests` host refuse to start unless those tests supply fake in-memory config (see section 5 row 1). (b) `DbUpdateException` is what the EF in-memory provider raises on a duplicate primary key (UNVERIFIED for the pinned EF version: assert it in a test before relying on it).

## 1. Scope & sequence

| # | Step | Operations | Notes |
| --- | --- | --- | --- |
| 1 | `MaxioSettings` bound from `Maxio:` (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`), validated at startup; register one `MaxioAdvancedBillingClient` singleton | none | Section 5 rows 1-3, 8 |
| 2 | Persistence: two claim tables in `CatalogContext` (see DUPLICATE CLAIMS) + migration for the SQL Server path | none | `YOUR CALL - not in the map` for table shape |
| 3 | `GET /api/subscription-plans`: list plans of the configured family | `ProductFamilies.ListProductsForProductFamily` (family given as `handle:` + `Maxio:ProductFamilyHandle`; the request record doc says id OR `handle:`-prefixed handle, so no id lookup and no hardcoded id) | JWT required. Returns handle, name, price in cents, interval, interval unit. Cut-short signal: PAGED READS |
| 4 | `POST /api/subscriptions` body `{ planHandle }`: validate plan against step 3's set, claim customer, ensure Maxio customer, claim subscription, create, record, confirm | `Customers.CreateCustomer`, `Customers.ReadCustomerByReference` (reconcile), `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription` (reconcile) | Identity from the token's `ClaimTypes.Name`, resolved through `UserManager<ApplicationUser>` to the stable `Id`. Response confirms plan handle/name, price, state, next billing date from the `CreateSubscription` response |
| 5 | `GET /api/my-subscriptions`: from the stored customer id, list the caller's subscriptions | `Customers.ListCustomerSubscriptions` | No stored customer -> empty list, no Maxio call |
| 6 | Error mapping + tests (stub `HttpClient` under the SDK; EF in-memory for claims) | none | Section 3 + REQUIRED READING |

No capability is missing from the map for these steps. The metered component `api-call` is not part of Subscribe (see Assumptions).

## 2. CONTRACT SHEET

> Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
>
> Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All operations: controller property on `MaxioAdvancedBilling.MaxioAdvancedBillingClient`; trailing parameters `MaxioAdvancedBilling.Core.Configuration`-level `RequestOptions? requestOptions = null, System.Threading.CancellationToken cancellationToken = default`; throw-only; auth `options.BasicAuth`; server group `Production`.

| Controller property | Method signature | Request record + members | Body model + fields | Response envelope + fields read | Error case + accessors + payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, ...)` | `ProductFamilyId: string, required` (doc: id OR handle prefixed `handle:`); `Page: int = 1 (min 1)`; `PerPage: int = 20 (max 200)`; optional `Filter`, `IncludeArchived: bool?`, `Include`, date filters (left unset) | none | `ProductResponse.Product` (required) -> `MaxioAdvancedBilling.Models.Product`: `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `RequireCreditCard (require_credit_card): bool?`, `TrialPriceInCents`, `TrialInterval`, `InitialChargeInCents` (for the "no trial, no setup fee" check) | Case A `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback] | page-based via `Page`/`PerPage`, returns a plain list (no `Pageable`): caller walks pages | `map/operations/ProductFamilies.md`; `src/.../Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `src/.../Models/Product.cs` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, ...)` | `Body: CreateCustomerRequest?` (optional on the record, but the call is meaningless without it: always set) | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required`. `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` (carry it: it is the user key the sheet reconciles by). Left out: address/phone/vat/tax/org/locale/parent fields | `CustomerResponse.Customer` (required) -> `MaxioAdvancedBilling.Models.Customer`: `Id (id): int?`, `Reference (reference): string?` | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], `TryGetRawError` [fallback] | none | `map/operations/Customers.md`; `src/.../Models/CreateCustomer.cs`, `Customer.cs` |
| `client.Customers` | `Task<CustomerResponse> ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, ...)` | `Reference: string, required` | none | `CustomerResponse.Customer.Id` | Case B `ApiException<RawError>`: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` (a missing reference is read from `StatusCode`; confirm the status is 404, `UNVERIFIED`) | none | `map/operations/Customers.md` |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, ...)` | `Body: CreateSubscriptionRequest?` (always set) | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required`. `CreateSubscription` (nothing required): carry `ProductHandle (product_handle): string?` (plan, the handle the invariant checked), `CustomerId (customer_id): int?` (doc: customer_id OR customer_reference OR customer_attributes is required: send `CustomerId`), `Reference (reference): string?` (doc: "reference value provided by your app for the subscription"; used to reconcile). Left out on purpose: payment profile/credit-card/bank attributes, `PaymentCollectionMethod`, components, coupons, trial/billing-date overrides, `CustomPrice`, `ProductPricePointHandle` | `SubscriptionResponse.Subscription?` -> `MaxioAdvancedBilling.Models.Subscription`: `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (next billing date), `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Product (product): Product?` (`Handle`, `Name`), `Reference (reference): string?`, `Customer?` | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422; `Errors: IReadOnlyList<string>, required`, wire `errors`], `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `<remarks>` in `src/.../Api/Subscriptions.cs` (CreateSubscription: payment information "may be required ... depending on the options for the Product"; 3DS 422 flow); `src/.../Models/CreateSubscription.cs`, `Subscription.cs` |
| `client.Subscriptions` | `Task<SubscriptionResponse> FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, ...)` | `Reference: string?` (optional on the record; always set) | none | `SubscriptionResponse.Subscription` (same fields as above) | Case A `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404], `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md` |
| `client.Customers` | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, ...)` | `CustomerId: int, required` | none | each item `.Subscription` (fields above) | Case B `ApiException<RawError>` | none (map silent -> no pagination; the request record has no page members) | `map/operations/Customers.md` |

### Enum value tables actually needed

| Enum (`MaxioAdvancedBilling.Models.Enums`, open string enum, not a C# enum; read via `TryGetKnownValue`/`Match`) | Values |
| --- | --- |
| `SubscriptionState` (`src/.../Models/Enums/SubscriptionState.cs`) | `Pending`, `FailedToCreate`, `Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold` (wire values are the snake_case forms, e.g. `failed_to_create`); the file may list more: re-read it at implementation |
| `IntervalUnit` (`src/.../Models/Enums/IntervalUnit.cs`) | `Day`, `Month` |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A plan handle accepted by `POST /api/subscriptions` must be one of the non-archived plans the configured family offers (`Product.Handle` of an item with `ArchivedAt == null`) | `Subscriptions.CreateSubscription` (`ProductHandle`) <- `ProductFamilies.ListProductsForProductFamily` | in the subscribe service, before the customer/subscription claims are written (it reads the full family list itself; it does not trust a handle the client saw earlier). The existence of a single product (`Products.ReadProductByHandle`) is NOT used: it would prove existence, not that this app offers it |
| The `CustomerId` passed to `CreateSubscription` and `ListCustomerSubscriptions` must be the one this application stored for the caller | `Subscriptions.CreateSubscription` / `Customers.ListCustomerSubscriptions` <- `Customers.CreateCustomer` (result stored in the claim row) | in the subscribe service and the my-subscriptions service, which read the id from the caller's own claim row and never from the request |
| The family used for the list is the configured one, written `handle:` + `Maxio:ProductFamilyHandle`, never an id | `ListProductsForProductFamily` <- configuration | in the single settings object (no literal handle or id in code) |

### Client construction / auth / server facts

| Fact | Value | Source |
| --- | --- | --- |
| Client | `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)`; DI helper `services.AddMaxioAdvancedBillingClient(options => ...)` | `sdk-map.md` Getting a client |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = <the password the map documents> }` (both `required`). Map: "The `username` is a Maxio Chargify API key. The `password` is `x`." | `sdk-map.md` Servers & auth |
| Environments | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) / `.Eu`. The SDK declares NO sandbox environment: sandbox is a different site (subdomain + key), so test traffic is kept away from live purely by the `Maxio:Subdomain`/`ApiKey` the deployment supplies | `sdk-map.md` Servers & auth |
| Server groups | 2: `Production` (Us `https://{site}.chargify.com`, Eu `https://{site}.ebilling.maxio.com`; site via `options.Server.Production.Us.Site` / `.Eu.Site`; base override `options.Server.Production.Us.BaseUrl`) and `Ebb` (events host). This scope touches only `Production`; `Ebb` is not used (metered usage events are out of scope) | `sdk-map.md`; `src/.../Servers/ProductionOptions.cs` |
| `Maxio:BaseUrl` | when set, assign it verbatim to `options.Server.Production.<env>.BaseUrl` (the property is a URL template whose `{site}` placeholder is simply absent). Whether a placeholder-free template is used unchanged is `UNVERIFIED`: assert the outgoing request URL in a test | `src/.../Servers/ProductionOptions.cs`; UNVERIFIED |
| Retry default | `RetryOptions.Default()`: retries statuses 408/429/500/502/503/504, methods GET/HEAD/PUT/OPTIONS only, 3 retries, exponential. `POST` is NOT in the list | `src/.../Core/Configuration/RetryOptions.cs` |
| Response metadata | success returns only the body; no status/headers | `dotnet-getting-started` |
| Request-side idempotency | neither `CreateCustomerOperationRequest` nor `CreateSubscriptionOperationRequest` has a key-shaped member (only `Body`). The injected `Idempotency-Key` header is a fresh Guid per call and is not a key | request record files |

## DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Ensure customer (`CreateCustomer`) triggered by a double-clicked `POST /api/subscriptions` | table `MaxioCustomerLinks` in `CatalogContext` (the store this codebase keeps its records in): primary key = `UserId` (the `ApplicationUser.Id`), plus nullable `MaxioCustomerId`. Claim row is inserted (`SaveChanges`) BEFORE `CreateCustomer`. The Maxio `Reference` set to `UserId` sits beside the claim, not instead of it | primary-key violation on the claim insert (enforced by the EF in-memory provider and by SQL Server) | the subscribe service's `catch (DbUpdateException)` around the claim save: it then reads the existing row; if `MaxioCustomerId` is set it proceeds with it, if the row is still unset (first caller in flight) it throws a new "in progress" application exception mapped to 409. A lookup-then-insert is NOT the refusal | SubscriptionService.EnsureCustomerAsync: claim = `_db.MaxioCustomerLinks.Add` + `SaveChangesAsync`, then `_client.Customers.CreateCustomer` (duplicate refusal caught by `IsDuplicateClaim` -> `ResolveExistingCustomerClaimAsync`) |
| Create subscription (`CreateSubscription`) | table `MaxioSubscriptionLinks`: composite primary key (`UserId`, `PlanHandle`), nullable `MaxioSubscriptionId`, `Reference` (`eshop-sub:<UserId>:<PlanHandle>`), `Status` (`Claimed`/`Created`/`Unknown`), written BEFORE `CreateSubscription`, after the customer id is known. Released (row deleted) when the provider refuses the call (typed 422 / `ApiException` with a 4xx) | primary-key violation on the claim insert | the same `catch (DbUpdateException)` pattern in the subscribe service; the second caller gets the stored result when `Status == Created`, otherwise 409 in-progress. One subscription per (user, plan) is the rule (`YOUR CALL - not in the map`; see Assumptions) | SubscriptionService.EnsureSubscriptionAsync: claim = `_db.MaxioSubscriptionLinks.Add` + `SaveChangesAsync`, then `_client.Subscriptions.CreateSubscription` (duplicate refusal caught by `IsDuplicateClaim` -> `ResolveExistingSubscriptionClaimAsync`) |

## PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (plans for `GET /api/subscription-plans` and for the plan-validation invariant) | `PerPage` (max 200 per page) and a service-side page ceiling (`YOUR CALL - not in the map`: e.g. 10 pages) | response field `IsTruncated: bool` on the plans response, set true when the last page fetched was full (count == `PerPage`) and the ceiling was hit; the validation path treats a truncated set as "cannot confirm" and fails closed (503), never as "not found" | `SubscriptionService.ReadFamilyProductsAsync` sets `truncated`; `ListPlansAsync` returns it as `PlanListResult.IsTruncated` -> `SubscriptionPlansResponse.IsTruncated`; `SubscribeAsync` fails closed with `BillingUnavailableException` |
| `ListCustomerSubscriptions` | none (map: no pagination; record has `CustomerId` only) | N/A: returns the provider's whole list; UNVERIFIED that the provider does not cap it silently, so the response also carries `Count` | `SubscriptionService.ListMySubscriptionsAsync` returns `SubscriptionListResult.Count` -> `MySubscriptionsResponse.Count` |

## UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `Reference` = `UserId` (the value sent in `CreateCustomer.Customer.Reference`) | `SubscriptionService.EnsureCustomerAsync` catch (SdkException) when `IsAmbiguous`: `TryReadCustomerByReferenceAsync` -> `RecordCustomerAsync` or `ReleaseAsync`; a claim left unset is settled by `ResolveExistingCustomerClaimAsync` | `SubscriptionServiceTests` (customer path shares the same ladder; subscription variant below exercises it)
| `CreateSubscription` | `Subscriptions.FindSubscription` (Case A, 404 via `TryGetNoContent`) | `Reference` = `eshop-sub:<UserId>:<PlanHandle>` (the `CreateSubscription.Subscription.Reference` sent) | `SubscriptionService.EnsureSubscriptionAsync` catch (SdkException) when `IsAmbiguous`: `TryFindSubscriptionAsync` -> `RecordSubscriptionAsync` (found), `ReleaseAsync` (404), `Status = Unknown` + `SubscriptionInProgressException` (re-read failed too); `ListMySubscriptionsAsync` settles Unknown rows | `Subscribe_ConnectionFailureOnCreate_IsNotResent_AndOutcomeIsReconciled`, `Subscribe_ConnectionFailureThenFound_RecordsTheSubscription`

## 3. Trap notes

| Step | Hazard | Pointer |
| --- | --- | --- |
| 1 client | The SDK client owns or borrows an `HttpClient`, and options (credentials, base URL) are read when the client is built; how lifetime and registration interact with rotation and DI decides stale connections and a disposed client | MUST load `maxio-advanced-billing:dotnet-client-initialization` |
| 1 auth | Basic credentials are a two-part pair and a blank part is not a missing one; an unset credential is skipped silently and the request is sent anyway, surfacing later as 401 | MUST load `maxio-advanced-billing:dotnet-authentication` |
| 1/3/4 base URL | `Maxio:BaseUrl` vs `Subdomain` vs `Environment` interact through the server-group templates; two groups exist | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 3 | The plan list is a plain list returned page by page; a full page does not mean the end | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 3/4/5 | Per-operation request records, how optional members and `Body` behave, and the shape of returned envelopes | MUST load `maxio-advanced-billing:dotnet-calling-endpoints` |
| 4 | Request/response models use open enums, `required` members and wire names that differ from C# names; mapping `SubscriptionState`/`IntervalUnit` to your DTO is not a cast | MUST load `maxio-advanced-billing:dotnet-models` |
| 4/5 | The two error cases differ per operation, and a drifted body does not arrive as the typed exception; what you catch decides what leaks as a 500 | MUST load `maxio-advanced-billing:dotnet-error-handling` |
| 4 | What a timeout bounds is not what bounds a whole call, and which verbs the SDK may resend decides whether a double create can come from the SDK itself | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 6 | Stubbing the transport, building SDK exceptions and asserting the outgoing request each have a seam that is not obvious | MUST load `maxio-advanced-billing:dotnet-testing` |

## 4. REQUIRED READING (load all before implementing)

| Skill | Step it governs |
| --- | --- |
| `maxio-advanced-billing:dotnet-client-initialization` | 1 client, DI, HttpClient lifetime |
| `maxio-advanced-billing:dotnet-authentication` | 1 credentials, startup check |
| `maxio-advanced-billing:dotnet-calling-endpoints` | 3, 4, 5 operation calls |
| `maxio-advanced-billing:dotnet-models` | 3, 4, 5 payloads and enums |
| `maxio-advanced-billing:dotnet-error-handling` | 3, 4, 5 error boundary (always listed) |
| `maxio-advanced-billing:dotnet-configuration-resilience` | 1, 3, 4 retries, timeout, base URL, paging, logging |
| `maxio-advanced-billing:dotnet-testing` | 6 tests |

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound with `Configure<MaxioSettings>(section "Maxio")` + `ValidateOnStart`: the host refuses to start when `Maxio:ApiKey` or `Maxio:ProductFamilyHandle` is null/whitespace, when `Maxio:Subdomain` is blank AND `Maxio:BaseUrl` is blank, or when a set `BaseUrl` is not an absolute http(s) URI; the Basic password the SDK needs is a fixed value from the map (not a setting) and is checked together with the key when the credentials object is built. Existing `tests/PublicApiIntegrationTests` must keep starting: their `ProgramTest` factory supplies fake in-memory `Maxio:*` values via `WithWebHostBuilder`/`ConfigureAppConfiguration` (no real credentials in any repo file, `appsettings*.json` carry no Maxio values). `YOUR CALL - not in the map` |
| 2 | Secret sourcing & rotation | Local/dev: `dotnet user-secrets` on `src/PublicApi` (existing `UserSecretsId`), set from `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` into `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`; deployed: configuration/env provider (`Maxio__ApiKey` form is the binder's concern). The options object and client are built once at registration and captured by the singleton: a rotated key needs a process restart. No rotation without restart is required |
| 3 | Total timeout budget | 20 s whole-call budget per endpoint request, enforced by a `CancellationTokenSource` linked to the request's `RequestAborted` and passed as the `cancellationToken` of every SDK call in that request; `RetryOptions.Timeout` is tuned separately and must not be assumed to be that budget (`dotnet-configuration-resilience`). Number is `YOUR CALL - not in the map` |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POST: not in `RetryOptions.Default().HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS), so the SDK never resends them; the application owns any re-attempt via the claim tables + reconciliation, and nothing re-sends a POST automatically. Reads (`ListProductsForProductFamily`, `ListCustomerSubscriptions`, `ReadCustomerByReference`, `FindSubscription`) are GET and may be retried by the SDK; do not add POST to the list |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no request-record key; real guard = claim row PK(`UserId`) + reconciliation by `Reference`. `CreateSubscription`: no request-record key; real guard = claim row PK(`UserId`,`PlanHandle`) + reconciliation by `Reference` through `FindSubscription`. The `Idempotency-Key` header is not cited anywhere as a key |
| 6 | Observability | `IAppLogger`/`ILogger`: Information on claim won/lost, customer created, subscription created, reconciliation outcome (ids and handles only); Warning on provider 4xx/422 (log `ErrorListResponse1.Errors` count and message text, no user input echoed); Error on 5xx/connection/timeouts/deserialization failures. Provider correlation id: the generated errors expose `StatusCode`/`ContentType`/`Headers` on `ApiException`; log a request-id-style response header from `ApiException.Headers` when present (the header name is `UNVERIFIED`; never dump all headers, they may carry secrets: log only the one name confirmed at implementation) plus our endpoint `CorrelationId` |
| 7 | Sensitive data | Customer create carries first/last name and email (PII); subscription create here carries no card data (payment fields deliberately not set). `LogRequestBody` stays off, `options.Logging.LoggerFactory` is set explicitly (this also disarms the `MAXIOADVANCEDBILLINGCLIENT_LOG` env var), and our own logs never print request bodies or the email. If form-body logging is ever turned on, no form deny-list key applies (all bodies here are JSON) |
| 8 | Environment selection | Server groups: `Production` (used) and `Ebb` (not touched). `ServerEnvironment.Us` by default; `Maxio:BaseUrl` overrides the `Production` base verbatim; otherwise the site comes from `Maxio:Subdomain`. The SDK has no sandbox environment: dev/test deployments set the sandbox subdomain (the sandbox site) and its key; production deployments never get those. There is no `Maxio:` key for EU vs US: EU would need `BaseUrl` (assumption below) |
| 9 | Duplicate prevention under concurrency | Rows in DUPLICATE CLAIMS above: both writes claim-before-call in `CatalogContext` with primary-key refusal; works under `UseOnlyInMemoryDatabase=true` (the in-memory provider enforces keys). Persistence exists in this codebase, so no Blocker |
| 10 | Partial results | PAGED READS table (`IsTruncated` on the plans response; fail closed for validation) |
| 11 | Unknown outcomes | UNKNOWN OUTCOMES table (reconcile in the failing write's own `catch`; `Unknown` state settled on the next call) |

## 6. Assumptions & Blockers

**Blockers**: none. Every operation in scope is in the map, the codebase has a durable store (`CatalogContext`, in-memory or SQL Server) for claims, and the provider offers a re-read for both writes (`ReadCustomerByReference`, `FindSubscription`).

**Assumptions** (flag to the user; the first two are the major ones):
1. **Required customer names.** `CreateCustomer` requires `first_name`, `last_name`, `email` (`src/.../Models/CreateCustomer.cs`). eShopOnWeb identity holds only a username/email, so names are derived from the email (e.g. local part, with a fixed placeholder surname) - `YOUR CALL - not in the map`. Confirm with the user.
2. **"Default subscribe target" `eshop-pro` is not stored in code or settings.** The `Maxio:` section is fixed to four keys and handles must not be hardcoded, so `POST /api/subscriptions` requires `planHandle` in the body; the "default" is the client's selection among the listed plans.
3. **Payment method not required.** `CreateSubscription` is called with no payment profile/card; the plan's `require_credit_card`/`request_credit_card` is read from the listed product and a plan that requires a card is rejected before any claim (the SDK doc says payment info "may be required ... depending on the options for the Product"). That `eshop-pro`/`basic-plan` really have it off is `UNVERIFIED` until a live call.
4. **No trial / no setup fee** are asserted by plan configuration at Maxio, not set by us; the plans response exposes trial/initial-charge fields so a violation is visible.
5. **Metered component `api-call`** is not part of Subscribe (no components are sent; no usage reporting; `Ebb` group untouched). Component usage would be a separate scope.
6. **`MAXIO_ENVIRONMENT`** has no `Maxio:` binding key (the four keys are fixed and the SDK has no sandbox environment). It is treated as a provisioning guard only (refuse to provision real credentials unless it says sandbox); nothing in code reads it.
7. **Identity**: the JWT carries only `ClaimTypes.Name`; the service resolves it with `UserManager<ApplicationUser>` and uses `ApplicationUser.Id` as the stable key and Maxio customer `Reference`. A token whose user no longer exists gets 401.
8. **One subscription per (user, plan)**: a user may hold different plans simultaneously; a second subscribe to the same plan returns the existing one (200) rather than a duplicate. A user switching plans is out of scope.
9. **Currency** is not on `Product`; prices are returned as cents with no currency code.
10. **US host** is assumed unless `Maxio:BaseUrl` is set; EU sites must use `BaseUrl`.
11. **SDK reference is machine-local**: `dotnet add src/Infrastructure/Infrastructure.csproj reference <plugin-root>/sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj` (not NuGet); teammates/CI need the plugin installed or the reference re-pointed. Which project (Infrastructure for the gateway) hosts it is `YOUR CALL - not in the map`.
12. **Migrations**: SQL Server path needs a new migration under `src/Infrastructure/Data/Migrations` (the dev path with `UseOnlyInMemoryDatabase=true` does not).
13. `ExceptionMiddleware` currently turns everything except `DuplicateException` into 500 with `exception.Message`; new application exceptions (in progress -> 409, plan not offered -> 400/404, provider unavailable -> 502/503) and no raw provider text in responses are added as part of step 6.
14. `UNVERIFIED` live-traffic facts, each with a defensive test: placeholder-free `BaseUrl` is used as given; `DbUpdateException` is raised by the in-memory PK conflict; 404 status for a missing customer reference (read from `RawError.StatusCode`); provider list may cap silently.


## 7. Implementation deviations (recorded after live verification)

- `CreateSubscription` also sends `PaymentCollectionMethod = Remittance` (`MaxioAdvancedBilling.Models.Enums.CollectionMethod`): a live call without it was rejected by Maxio (422, no payment method on file for the first charge) even though the plan has `require_credit_card` off. Invoice-style collection makes subscribe work without card capture.
- The EF in-memory provider raises the raw duplicate-key exception (not `DbUpdateException`): `IsDuplicateClaim` accepts both.
- Only `RequireCreditCard` gates a plan; `RequestCreditCard` (card is asked for but optional) is allowed.
- Before each write the service re-reads Maxio by reference and adopts an existing customer/subscription (survives a wiped local store).
