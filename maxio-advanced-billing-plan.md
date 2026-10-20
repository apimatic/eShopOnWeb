# Maxio Advanced Billing plan: eShopOnWeb Subscribe

SDK root (plugin-relative): `sdk/dotnet/`. All SDK paths below are relative to it. Namespaces follow the map's "Namespaces by content type" table.

## 1. Scope & sequence

| # | Step | Operations used |
| --- | --- | --- |
| 1 | Config + client: bind `Maxio:` section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`), validate at startup, register one singleton `MaxioAdvancedBillingClient` | none |
| 2 | `GET /api/subscription-plans` (anonymous allowed; no user data): list plans of the configured family, unarchived only | `ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` (JWT): validate plan handle against step 2's list, claim, ensure customer, create subscription, record result, return plan/price/state/next billing date | `ProductFamilies.ListProductsForProductFamily`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.CreateSubscription`, reconciliation: `Subscriptions.FindSubscription` |
| 4 | `GET /api/my-subscriptions` (JWT): resolve the caller's Maxio customer, list subscriptions from Maxio (system of record), map to DTO | `Customers.ReadCustomerByReference` (only when no local mapping), `Customers.ListCustomerSubscriptions` |
| 5 | Local mapping/claim store `SubscriptionClaims` (userId -> Maxio customer id / subscription id) in `CatalogContext` | none |

Repo conventions read (record pattern + ONE exemplar):

| Convention | Exemplar |
| --- | --- |
| Endpoint = class `IEndpoint<IResult, TRequest, ...>` with `AddRoute` using `app.MapGet/MapPost("api/...")`, `.Produces<T>()`, `.WithTags("...Endpoints")`; request/response in sibling files `X.Request.cs`/`X.Response.cs`, response ctor takes `request.CorrelationId()`; folder `src/PublicApi/<Name>Endpoints/`; auto-registered by `AddEndpoints()`/`MapEndpoints()` in `src/PublicApi/Program.cs` | `src/PublicApi/CatalogItemEndpoints/CatalogItemListPagedEndpoint.cs` |
| JWT-protected route: `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` on the lambda | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` (it also adds `Roles=`; the subscription routes need no role) |
| Token carries ONLY `ClaimTypes.Name` (= username) and role claims; there is no `NameIdentifier`/`sub`/email claim | `src/Infrastructure/Identity/IdentityTokenClaimService.cs` (read) |
| Records persisted via EF `CatalogContext`, entities implement `IAggregateRoot`, per-entity `IEntityTypeConfiguration<T>` auto-applied by `ApplyConfigurationsFromAssembly`; `IRepository<T>`/`EfRepository<T>` over `CatalogContext` | `src/Infrastructure/Data/CatalogContext.cs`, `src/Infrastructure/Data/Config/BasketConfiguration.cs`, `src/ApplicationCore/Entities/BuyerAggregate/Buyer.cs` |
| Errors: `ExceptionMiddleware` maps `DuplicateException` to 409 and everything else to 500 with the exception message; endpoints that need 400/404/502 must return `Results.*` themselves and never let SDK exception text reach the client | `src/PublicApi/Middleware/ExceptionMiddleware.cs` |
| `UseInMemoryDatabase` vs SQL Server chosen in `Dependencies.ConfigureServices`; migrations only apply to SQL | `src/Infrastructure/Dependencies.cs` |
| `PublicApi.csproj` already has `UserSecretsId` (user-secrets usable) | `src/PublicApi/PublicApi.csproj` |
| Tests: `tests/PublicApiIntegrationTests` (`ProgramTest.cs`, `ApiTokenHelper.cs`) and `tests/UnitTests` | `tests/PublicApiIntegrationTests/CatalogItemEndpoints/CreateCatalogItemEndpointTest.cs` |

Planned file layout (decision, `YOUR CALL — not in the map`): `src/ApplicationCore/Entities/SubscriptionAggregate/SubscriptionClaim.cs` (+ `IAggregateRoot`), `src/Infrastructure/Data/Config/SubscriptionClaimConfiguration.cs`, `DbSet<SubscriptionClaim>` on `CatalogContext`, `src/Infrastructure/Maxio/*` (options, client factory, `MaxioBillingService`), `src/PublicApi/SubscriptionEndpoints/*`. The SDK is referenced as a `ProjectReference` to `sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj` from the project that calls it (machine-local reference; say so on hand-over). The SDK source is never copied into the repo.

## 2. CONTRACT SHEET

> WARNING 1: signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer using the record's own property names, never flat arguments.
> WARNING 2: every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

Common to all rows: every method ends `(…, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)`; throw-only; server group `Production`; auth `options.BasicAuth`; no pagination type (see PAGED READS for the `Page`/`PerPage` members). Controller properties are on `MaxioAdvancedBilling.MaxioAdvancedBillingClient`.

| Controller property | Method signature | Request record + members | Body model + fields | Response envelope + inner fields read | Error case + accessors + payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, …)` | `ProductFamilyId: string, required` ("Either the product family's id or its handle prefixed with `handle:`", so pass `"handle:" + Maxio:ProductFamilyHandle`); `Page: int = 1` (min 1); `PerPage: int = 20` (max 200); `IncludeArchived: bool?`; `Include`, `Filter`, `DateField`, `Start/End*`: left out | none (GET) | `ProductResponse.Product` (`required`) of `MaxioAdvancedBilling.Models.Product`: `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): ProductFamily?` | Case A `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404, unknown family handle]; `TryGetRawError(out RawError)` fallback | none declared by map; request has `Page`/`PerPage`, drive it manually | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/Product.cs`; `Models/ProductResponse.cs` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, …)` | `Reference: string, required` (query `reference`) | none (GET) | `CustomerResponse.Customer` (`required`): `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | Case B `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `Error.StatusCode` (404 expected for "no such customer": UNVERIFIED, the map does not state the not-found status) | none | `map/operations/Customers.md`; `Models/CustomerResponse.cs`; `Models/Customer.cs`. Remarks: "returns a single match". |
| `client.Customers` | `Task<CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, …)` | `Body: CreateCustomerRequest?` (nothing marked required on the record; the endpoint is useless without it, so always set it) | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required`. `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` (set to the userId so the customer is findable); optional rest left out | `CustomerResponse.Customer.Id (id): int?` | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422]; `TryGetRawError` fallback | none | `map/operations/Customers.md`; `Models/CreateCustomer.cs`; `Requests/Customers/CreateCustomerOperationRequest.cs`. Real idempotency key member: none on the record. Whether `reference` is unique at Maxio is not in the map: UNVERIFIED. |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, …)` | `Body: CreateSubscriptionRequest?` (nothing required on the record; always set) | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required`. `CreateSubscription` fields carried: `ProductHandle (product_handle): string?` (doc: required unless `product_id` given; carry it, value = the validated plan handle); `CustomerId (customer_id): int?` (doc: required unless `customer_reference` or `customer_attributes`; carry it); `Reference (reference): string?` (app's reference for the subscription; carry a deterministic value for reconciliation). Doc-named fields deliberately left out: `ProductPricePointHandle/Id` (default price point of the product), `PaymentProfileId`/`PaymentProfileAttributes`/`CreditCardAttributes` (payment method not required per task), `CustomerReference`, `CustomerAttributes`, `Components`, `CouponCode(s)`, `NextBillingAt`, `InitialBillingAt`, `DeferSignup`, `PaymentCollectionMethod` (see Assumptions) | `SubscriptionResponse.Subscription` (`Subscription?`, optional): `Id (id): int?`, `State (state): SubscriptionState?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (the next billing date), `CurrentPeriodEndsAt (current_period_ends_at)`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `Product (product): Product?` (`Handle`, `Name`, `PriceInCents`, `Interval`, `IntervalUnit`), `Reference (reference): string?`, `Customer (customer): Customer?` | Case A `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422]; `TryGetRawError` fallback. Remarks: a 422 with an `action_link` is the 3DS flow (not reachable without a payment profile). | none | `map/operations/Subscriptions.md`; `Models/CreateSubscription.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/Subscription.cs`; `Api/Subscriptions.cs` remarks. Real idempotency key: none on the record (`Body` only). |
| `client.Subscriptions` | `Task<SubscriptionResponse> FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, …)` | `Reference: string?` (optional on the record, but the lookup is meaningless without it: always set) | none (GET) | `SubscriptionResponse.Subscription` as above | Case A `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404 = not found]; `TryGetRawError` fallback | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs` |
| `client.Customers` | `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, …)` | `CustomerId: int, required` | none (GET) | list of `SubscriptionResponse`; read `.Subscription` per element (fields as above) | Case B `ApiException<RawError>` | none | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs`. Remarks: with the new Catalog experience a subscription may have `product` null; handle null. |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The plan handle accepted by subscribe must be one the plan list returned (configured product family, not archived); a lookup by handle alone is not enough | `CreateSubscription` (`ProductHandle`) <- `ListProductsForProductFamily` (`handle:` + `Maxio:ProductFamilyHandle`) | in `MaxioBillingService.SubscribeAsync`, before the claim is written and before `CreateCustomer`/`CreateSubscription` run: it re-reads the plan list (the same `GetPlansAsync` the `GET /api/subscription-plans` endpoint uses) and rejects with 400 any handle not in it. The existing code never reads that list for subscribe, so the read is added as part of the work. |
| The customer id passed to `CreateSubscription` must be one this app created/found for the authenticated user (reference = userId from the token), never a caller-supplied id | `CreateSubscription` (`CustomerId`) <- `ReadCustomerByReference` / `CreateCustomer` | in `SubscribeAsync`; the request body of `POST /api/subscriptions` has no customer field at all |
| `my-subscriptions` lists only subscriptions of the customer mapped to the token's user | `ListCustomerSubscriptions` <- claim row / `ReadCustomerByReference` | in `ListMineAsync` |

### Enums needed

| Enum (file) | Members used |
| --- | --- |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`Models/Enums/SubscriptionState.cs`), open string enum | `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup` (C# members PascalCase, e.g. `Active`, `AwaitingSignup`) |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (`Models/Enums/IntervalUnit.cs`), open string enum | `day`, `month` (`Day`, `Month`); has `Match(onDay, onMonth, otherwise)` |

### Client construction / auth / server

| Fact | Value | Source |
| --- | --- | --- |
| Client | `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)` or DI `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>? configure)` | `sdk-map.md` |
| Auth | `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (map: username is the API key, password is `x`; `x` is a protocol constant of the scheme, not a configurable value) | `sdk-map.md` Servers & auth |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` default (`https://{site}.chargify.com`); `Eu` = `https://{site}.ebilling.maxio.com` | `sdk-map.md`, `Servers/ProductionOptions.cs` |
| Subdomain | `options.Server.Production.Us.Site = <Maxio:Subdomain>` (default value in SDK is the literal `"subdomain"`, which would silently hit a wrong host: always set) | `Servers/ProductionOptions.cs` |
| BaseUrl override | when `Maxio:BaseUrl` is set: `options.Server.Production.Us.BaseUrl = <Maxio:BaseUrl>` verbatim (a literal URL without `{placeholders}` is used as-is). `Environment` is captured at construction; set `Server` before constructing | `Servers/ProductionOptions.cs`; `dotnet-configuration-resilience` |
| Server groups touched | `Production` only. `Ebb` (events.chargify.com) not touched by this scope | `sdk-map.md` |

## 3. Trap notes

| Step | Hazard (cost) | Pointer |
| --- | --- | --- |
| Client DI | `Environment` is read once at construction while `Server` is live-read; setting the wrong one makes `Maxio:BaseUrl`/`Subdomain` silently inert and sends sandbox keys to the wrong host. The unset `Site` default is a literal placeholder host. | MUST load `maxio-advanced-billing:dotnet-client-initialization` |
| Client DI | HttpClient ownership/lifetime and singleton capture decide what rotation and socket reuse look like. | MUST load `maxio-advanced-billing:dotnet-client-initialization` |
| Auth | Basic credential has two parts; a blank part is not a missing one and surfaces only as a 401 at first call. | MUST load `maxio-advanced-billing:dotnet-authentication` |
| Every call | Operation inputs are request records, not flat args; `Body` is nullable on the record though the endpoint needs it. | MUST load `maxio-advanced-billing:dotnet-calling-endpoints` |
| Models | Open string enums are not C# enums; reading `State`/`IntervalUnit` for your DTO needs the generated accessors, and `Subscription`/`Product`/`Customer` members are all nullable. | MUST load `maxio-advanced-billing:dotnet-models` |
| Retry / timeout | Per-attempt timeouts are not a call budget, and the default 100s per attempt is far over any interactive budget; which verbs the SDK resends decides whether a POST can double. | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| Catch ladders | A Case A, a Case B and a deserialization failure each need a different catch; 404 from `ReadCustomerByReference` (Case B) and from `FindSubscription` (Case A) are read differently. | MUST load `maxio-advanced-billing:dotnet-error-handling` |
| Unknown outcome | A transport failure on `CreateCustomer`/`CreateSubscription` may have landed; reporting failure instead of re-reading is the defect. | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| Logging | SDK logs request lines by default under DI; body logging would write customer PII verbatim. | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| Tests | How to stub the HTTP layer and construct SDK exceptions to test the claim/unknown paths. | MUST load `maxio-advanced-billing:dotnet-testing` |

## 4. REQUIRED READING (load before implementation)

| Skill | Step governed |
| --- | --- |
| `maxio-advanced-billing:dotnet-client-initialization` | step 1 client + DI |
| `maxio-advanced-billing:dotnet-authentication` | step 1 credential binding + fail-fast |
| `maxio-advanced-billing:dotnet-calling-endpoints` | steps 2-4 every call |
| `maxio-advanced-billing:dotnet-models` | steps 2-4 request/response mapping |
| `maxio-advanced-billing:dotnet-error-handling` | steps 2-4 error boundary (always) |
| `maxio-advanced-billing:dotnet-configuration-resilience` | timeouts, retries, logging, unknown outcomes, paging |
| `maxio-advanced-billing:dotnet-testing` | tests |

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `Maxio:` section to an options class (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`) with `ValidateOnStart`; the host refuses to start when `ApiKey`, `Subdomain` or `ProductFamilyHandle` is null/empty/whitespace (every part checked; the Basic password `x` is a constant, not configuration). `BaseUrl` is optional; if present it must be an absolute http(s) URI else startup fails. |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` only from user-secrets (dev; `UserSecretsId` exists) or the deployment's configuration provider; never in `appsettings*.json` or the repo, and no default values for any `Maxio:` key in the repo. Options object is built once at registration and captured by the singleton client; a rotated key takes effect on process restart. No rotation-without-restart requirement stated. |
| 3 | Total timeout budget | `options.Retry.Timeout` = 10s per attempt and `HttpClient.Timeout` = 10s as a backstop; whole-operation budget 30s enforced in ONE place (`MaxioBillingService`, a linked `CancellationTokenSource` over `HttpContext.RequestAborted` with `CancelAfter(30s)`), one token passed to every SDK call in a handler (subscribe makes up to 4 SDK calls plus plan pages). Budget value is a constant in the service (`YOUR CALL — not in the map`; it is not a `Maxio:` key because the key set is fixed). |
| 4 | Write-retry ownership | `HttpMethodsToRetry` stays at SDK default (GET, HEAD, PUT, OPTIONS), so `CreateCustomer` and `CreateSubscription` (both POST by `Api/Customers.cs` line 104 and the Subscriptions page) are never resent by the SDK; GET reads (`ListProductsForProductFamily`, `ReadCustomerByReference`, `FindSubscription`, `ListCustomerSubscriptions`) may be resent. Nobody adds POST to the list. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no real key member; reconcile with `ReadCustomerByReference(Reference = userId)`. `CreateSubscription`: no real key member (record is `Body` only); reconcile with `FindSubscription(Reference = deterministic subscription reference)` and `ListCustomerSubscriptions`. The injected `Idempotency-Key` header is not a key. Local claim (row 9) is the actual duplicate guard. |
| 6 | Observability | Info: subscribe start/finish with userId + plan handle + Maxio subscription id; Warning: reconciliation taken, stale claim; Error: provider failures with HTTP status. From provider errors log `ApiException.StatusCode` and, for Case A, the 422 error list text (no PII beyond ids); for Case B `RawError.ReadAsString()` truncated. The provider's own correlation header name is not in the map, so the provider correlation id is `UNVERIFIED`; the app's request `CorrelationId()` (from `BaseMessage`) is logged on every line and returned to the caller. |
| 7 | Sensitive data | `CreateCustomer` carries name and email (PII); no card/bank data is in scope. `LogRequestBody` stays off, `options.Logging.LoggerFactory` is assigned explicitly from the host's `ILoggerFactory`, headers logging off. Own logs never echo request bodies. |
| 8 | Environment selection | Group `Production` only; `ServerEnvironment.Us` (default) with `Site = Maxio:Subdomain` (sandbox is site `cp-exp-4`, selected by subdomain + sandbox API key). `Eu` is not selectable through the fixed key set; an EU site is reached via `Maxio:BaseUrl`. `Ebb` group not touched. The SDK declares no sandbox environment; test traffic is kept off any live site by the subdomain/API key in user-secrets, and automated tests set `Maxio:BaseUrl` to a stub host. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS (plans list). |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `POST /api/subscriptions` (customer ensure + `CreateSubscription`, one claim per user) | table `SubscriptionClaims` on the existing `CatalogContext` (the store the codebase keeps its records in; works with `UseInMemoryDatabase`). Columns: `UserId` (string, max 256, the identity user id, **primary key, `ValueGeneratedNever`**), `State` (`Pending`/`Unknown`/`Completed`), `PlanHandle`, `MaxioCustomerId?`, `MaxioSubscriptionId?`, `CreatedUtc`, `UpdatedUtc`. Claim row written and saved BEFORE any `CreateCustomer`/`CreateSubscription` call. | primary-key violation on the second `SaveChanges` of an `Add` with the same `UserId` (a PK is refused by the in-memory provider too; a unique index is NOT relied on) | `catch (DbUpdateException)` around the claim `SaveChangesAsync` in `MaxioBillingService.SubscribeAsync`; on catch, load the existing row: `Completed` -> return the stored subscription (idempotent 200); `Pending`/`Unknown` -> reconcile via `FindSubscription` then return it or 409 "in progress". Verified live: the in-memory provider raises a raw `ArgumentException` (not `DbUpdateException`), so the catch is any non-cancellation exception confirmed by finding the existing row. Claim is released (row deleted) when the provider definitively refuses (4xx) so a corrected retry is possible. Order: validate plan (read) -> claim -> SDK calls -> record result. | `MaxioSubscriptionBilling.SubscribeAsync` (claim `SaveChangesAsync`), then `MaxioSubscriptionBilling.RunClaimedAsync` (`CreateCustomer`/`CreateSubscription`) |

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| Plans: `ListProductsForProductFamily` walked by `Page` with `PerPage = 200` (max 200) until a page returns fewer than 200 items | a hard page cap (`MaxPlanPages`, constant, e.g. 10) independent of the provider | `GET /api/subscription-plans` response field `IsTruncated: bool` (true when the cap stopped the walk); the service's `GetPlansAsync` returns a result type `{Plans, IsTruncated}`; subscribe treats a truncated list as unable to prove membership and answers 502 rather than accept an unlisted handle | `MaxioSubscriptionBilling.GetPlansCoreAsync` (sets `SubscriptionPlanList.IsTruncated`) |
| `ListCustomerSubscriptions` | map declares no pagination and the request has no page members | `none` (single response) | `MaxioSubscriptionBilling.GetSubscriptionsAsync` (no cap) |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `Reference` = identity user id (also sent as `CreateCustomer.reference`) | `MaxioSubscriptionBilling.EnsureCustomerAsync` `catch` (Indeterminate -> `TryReadCustomerIdAsync`; still unknown -> `RunClaimedAsync` marks the claim Unknown) | none dedicated; customer path shares the Indeterminate handler covered by `ConnectionFailureThatReReadCannotConfirm_LeavesTheClaimUnknown_AndTheNextSubscribeSettlesIt` |
| `CreateSubscription` | `Subscriptions.FindSubscription` (and `Customers.ListCustomerSubscriptions` for the claim's customer id) | `Reference` = deterministic `"eshop-sub-" + userId`, sent as `CreateSubscription.reference` | `MaxioSubscriptionBilling.RunClaimedAsync` `catch (BillingException) when Indeterminate` -> `ReconcileSubscriptionAsync`; unsettled -> `SetUnknownAsync`, re-run on next subscribe / my-subscriptions | `ConnectionFailureOnSubscriptionWrite_IsSettledByReReading_AndNotResent` and `ConnectionFailureThatReReadCannotConfirm_...` (tests/UnitTests/Infrastructure/MaxioSubscriptionBillingTests.cs) |

Settle rule (for both): in the write's own `catch` for `SdkConnectionException`/`SdkTimeoutException` (and `ResponseDeserializationException` on a 2xx), re-read before returning. Found -> claim `Completed` with the Maxio ids. Not found, or the re-read also fails -> claim state `Unknown` (not failed, claim kept) and the response is 202-style "pending confirmation"; the same reconciliation re-runs on the next `POST /api/subscriptions` and on every `GET /api/my-subscriptions` for that user.

## 6. Assumptions & Blockers

**Blockers**

1. None that stop planning. One risk the plan makes explicit: `CreateSubscription` with no payment profile and no `PaymentCollectionMethod`. The map says payment info "may be required ... depending on the options for the Product", and the task states these plans do not require a payment method, so the call carries none. **Resolved at implementation:** the sandbox rejected the default (automatic collection) with 422 "No payment method was on file"; the request now sets the SDK-documented `PaymentCollectionMethod = Remittance` (`Models/CreateSubscription.cs`), which succeeds without card capture. If the sandbox rejects it with 422, that is a provider-side catalog setting, outside this SDK/map; report it back rather than inventing a payment profile.

**Assumptions**

1. Identity: the JWT has only `ClaimTypes.Name` (username). The user id is resolved server-side by `UserManager<ApplicationUser>.FindByNameAsync(User.Identity.Name)` and its `Id` is the stable key (claim PK, Maxio customer `reference`). The caller never supplies user or customer ids.
2. One subscription per user (claim PK = `UserId`). A second subscribe by the same user, even to another plan, returns the existing subscription (200) rather than creating another; plan change is out of scope.
3. `POST /api/subscriptions` body is `{ "planHandle": "<handle>" }`, required. There is no `Maxio:` key for a default plan, and the key set is fixed and no handle may be hard-coded, so `eshop-pro` is not defaulted in code; the client takes it from `GET /api/subscription-plans`.
4. Customer names: `CreateCustomer` requires `FirstName`, `LastName`, `Email`. I did not read `ApplicationUser` for name fields; plan is `Email` = identity email, `FirstName`/`LastName` derived from the email local part when no name data exists (`YOUR CALL — not in the map`; confirm at implementation).
5. `GET /api/subscription-plans` is anonymous (catalog data); `POST` and `GET my-subscriptions` require the JWT scheme.
6. In-memory claim store is lost on restart; because Maxio is the system of record, `GET /api/my-subscriptions` and subscribe recover the mapping by `ReadCustomerByReference(userId)` / `FindSubscription(reference)` when no claim row exists. A production SQL deployment needs an EF migration for `SubscriptionClaims` (ignored under in-memory); to be added with the entity.
7. "Not found" statuses: `ReadCustomerByReference` is Case B and the map does not state its not-found status; treated as 404 via `RawError.StatusCode`, `UNVERIFIED` against live traffic. Whether Maxio enforces customer `reference` uniqueness is `UNVERIFIED`; the local claim is the guard regardless.
8. `Program.cs` shows `UseAuthorization` but no explicit `UseAuthentication`; the existing admin endpoints rely on `[Authorize(AuthenticationSchemes = Jwt)]`, and the new routes use the same attribute form, so no change to `Program.cs` auth pipeline is planned.
9. Price shown is `Product.PriceInCents` (plan) and `Subscription.ProductPriceInCents` (subscription); next billing date is `Subscription.NextAssessmentAt`; both come from the map-named model files.
