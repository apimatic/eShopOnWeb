# Maxio Advanced Billing plan - eShopOnWeb recurring subscriptions (Subscribe hero flow)

SDK root (plugin-relative): `sdk/dotnet/` (all `Source` cells below are relative to it). Namespace root `MaxioAdvancedBilling`.

## 1. Scope & sequence

Repo survey (read only; each = pattern + the ONE exemplar to imitate):

| Concern | Pattern | Exemplar |
| --- | --- | --- |
| Endpoint style | `MinimalApi.Endpoint` `IEndpoint<IResult, TRequest, TDeps>` class, `AddRoute` with `app.MapGet/MapPost("api/...")`, `.Produces<T>().WithTags(...)`; auto-registered by `AddEndpoints()`/`MapEndpoints()` in `src/PublicApi/Program.cs` | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` (auth attr `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` on the lambda; drop the `Roles =` part, any logged-in user may call) |
| Request/response DTOs | one file per type, `BaseRequest`/`BaseResponse` with `CorrelationId()` | `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.CreateCatalogItemRequest.cs` and `.CreateCatalogItemResponse.cs` |
| Error mapping | `ExceptionMiddleware` maps `DuplicateException` -> 409, everything else -> 500 with `exception.Message` | `src/PublicApi/Middleware/ExceptionMiddleware.cs` (extend with new mappings; do NOT let SDK exception messages reach callers as 500 text) |
| Local store | `CatalogContext` (EF Core, in-memory DB "Catalog" when `UseOnlyInMemoryDatabase`, else SQL Server; migrations ignored in-memory), entity configs in `Infrastructure/Data/Config`, applied by `ApplyConfigurationsFromAssembly` | `src/Infrastructure/Data/Config/CatalogBrandConfiguration.cs`, `src/Infrastructure/Data/CatalogContext.cs`, `src/Infrastructure/Data/EfRepository.cs` |
| DI wiring | `builder.Services...` in `Program.cs`; `Infrastructure/Dependencies.cs` for data | `src/PublicApi/Program.cs` |
| Caller identity | JWT minted by `IdentityTokenClaimService` carries ONLY `ClaimTypes.Name` (user name) and `ClaimTypes.Role` claims (read in `src/Infrastructure/Identity/IdentityTokenClaimService.cs`). No NameIdentifier / sub / email claim exists in the token. `ApplicationUser : IdentityUser` (`src/Infrastructure/Identity/ApplicationUser.cs`), so email is obtainable by `UserManager<ApplicationUser>.FindByNameAsync(name)` (already registered in `Program.cs`). | - |

**Identity decision (DECIDED (implemented)):** the caller's id is the `ClaimTypes.Name` claim value (user name) read from `HttpContext.User`, never from a body/query. It is the Maxio customer `reference`. A request with no such claim -> 401. Email for customer creation comes from the `ApplicationUser` looked up by that name; first name = user name; last name = a fixed literal chosen at implementation. If the user has no email -> 400 and no provider call.

Steps (in order):

1. Settings: `MaxioSettings` record bound from section `Maxio:` with exactly `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` (optional). No default values in code. Validated at startup (see PR #1).
2. Client registration (singleton `MaxioAdvancedBillingClient`) - no operations.
3. `GET /api/subscription-plans` - `ProductFamilies.ListProductsForProductFamily` (family = `handle:` + `Maxio:ProductFamilyHandle`), page-walked.
4. `POST /api/subscriptions` body `{ planHandle }`: validate handle against step 3's list -> claim (local PK insert) -> ensure customer (`Customers.ReadCustomerByReference`, on 404 `Customers.CreateCustomer`, on 422-duplicate re-read) -> `Subscriptions.CreateSubscription` -> record result on claim row -> return plan/price/state/next-billing date from the `SubscriptionResponse`.
5. `GET /api/my-subscriptions` - `Customers.ReadCustomerByReference` (404 = provider-confirmed no customer = empty list) then `Customers.ListCustomerSubscriptions`. Never reads local rows to decide the answer.
6. Persistence: `SubscriptionClaim` entity + `IEntityTypeConfiguration` in `CatalogContext`.
7. Tests (unit with stubbed `HttpClient`, plus PublicApiIntegrationTests style) - see `dotnet-testing`.

Not in scope: cancel, payment profiles, components (`api-call` metered component is NOT attached on subscribe: not requested; DECIDED).

## 2. CONTRACT SHEET

> Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own - never flat arguments.
>
> Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All operations: throw-only, no `Pageable`, server group `Production`, auth `options.BasicAuth`, trailing params `RequestOptions? requestOptions = null, CancellationToken cancellationToken = default`. Controller properties are on `MaxioAdvancedBilling.MaxioAdvancedBillingClient` (`client.ProductFamilies` etc.).

| Controller.property · method | Request record (`MaxioAdvancedBilling.Requests.<Controller>`) members | Body model (wire) | Response + fields read | Error case · accessors · payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily(ListProductsForProductFamilyRequest)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` ("id or handle prefixed `handle:`" per member doc); optional `Page: int = 1 [Min 1]`, `PerPage: int = 20 [Max 200]`, `IncludeArchived: bool?` (leave unset), `Filter`, `Include`, date filters (left out) | none | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; each `.Product` (required) -> `MaxioAdvancedBilling.Models.Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ArchivedAt (archived_at): DateTimeOffset?`, `RequireCreditCard (require_credit_card): bool?`, `TrialInterval`, `InitialChargeInCents` | **A** `MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404 unknown family]; `TryGetRawError(out RawError)` fallback | none built in; manual `Page`/`PerPage` (default no-`Pageable` applies) - see PAGED READS | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`, `Models/Product.cs` |
| `client.Customers.ReadCustomerByReference(ReadCustomerByReferenceRequest)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` (query `reference`) | none | `MaxioAdvancedBilling.Models.CustomerResponse`: `.Customer` (required) -> `Customer`: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | **B** `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `.Error.StatusCode` (404 expected when absent - the status for "not found" is **UNVERIFIED** in the map: the page lists no typed 404), `ReadAsString()`, `ReadAsJson<T>()` | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| `client.Customers.CreateCustomer(CreateCustomerOperationRequest)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` (optional, but the call is meaningless without it) | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required` -> `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` - carried because the method docs state it must be unique ("only one customer for a given reference value"); left out: all address/tax/org fields | `CustomerResponse` -> `.Customer.Id: int?` | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422 - includes duplicate reference]; `TryGetRawError` fallback | none | `map/operations/Customers.md`; `Api/Customers.cs` `<remarks>` (reference unique); `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs` |
| `client.Customers.ListCustomerSubscriptions(ListCustomerSubscriptionsRequest)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | none | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; `.Subscription` (optional, null-check) -> `MaxioAdvancedBilling.Models.Subscription`: `Id: int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?` (the next billing date), `CurrentPeriodEndsAt: DateTimeOffset?`, `Product: Product?` (Handle, Name), `Customer: Customer?`, `Currency: string?` | **B** `ApiException<RawError>` | no `Pageable`; page behaviour of this operation **UNVERIFIED** (no page params on its record) - see PAGED READS | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs`; `Models/SubscriptionResponse.cs`, `Models/Subscription.cs` |
| `client.Subscriptions.CreateSubscription(CreateSubscriptionOperationRequest)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required` -> `CreateSubscription` (nothing required): carry `ProductHandle (product_handle): string?` (method docs: specify product with product_id or product_handle) and `CustomerId (customer_id): int?` (docs: identify existing customer with customer_id or customer_reference). Left out: price point, coupons, payment profile / card attributes (payment not required for these plans), `CustomerAttributes`, components, trial/billing-date fields, `Reference` | `SubscriptionResponse` -> `.Subscription` (fields as above) | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422]; `TryGetRawError` fallback | none | `map/operations/Subscriptions.md`; `Api/Subscriptions.cs` `<remarks>` on CreateSubscription; `Models/CreateSubscription.cs` |

Enums needed (`MaxioAdvancedBilling.Models.Enums`, open string enums - not C# enums; resolve with `TryGetKnownValue`/`Match`, read declaring file `src/MaxioAdvancedBilling/Core/Enum/TypedEnum.cs` for the raw-value accessor before coding): `SubscriptionState` (`Models/Enums/SubscriptionState.cs`): Pending, FailedToCreate, Trialing, Assessing, Active, SoftFailure, PastDue, Suspended, Canceled, Expired, Paused, Unpaid, TrialEnded, OnHold, AwaitingSignup. `IntervalUnit` (`Models/Enums/IntervalUnit.cs`, not read; open file before mapping). Pass the state through to the caller as its raw string.

Client construction / auth / server facts (`sdk-map.md` Getting a client, Servers & auth):
- Only constructor `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)`; DI helper `services.AddMaxioAdvancedBillingClient(options => ...)`.
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (map: username is the API key, password is `x`; the `"x"` is an SDK-documented constant, not a configurable value).
- Environment `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default; DECIDED: keep Us, sandbox site is on the Chargify host family, no key for environment was specified).
- Base address: all operations here use server group `Production`; template `https://{site}.chargify.com`. When `Maxio:BaseUrl` is set, assign it verbatim to `options.Server.Production.Us.BaseUrl` (`string BaseUrl`, `src/MaxioAdvancedBilling/Servers/ProductionOptions.cs`); otherwise set `options.Server.Production.Us.Site = Maxio:Subdomain`. The `Ebb` group is not touched by any operation here.
- `RetryOptions` (`MaxioAdvancedBilling.Core.Configuration`) has all-required members; start from `RetryOptions.Default()`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `planHandle` accepted by `POST /api/subscriptions` must be a plan the product family offers (non-archived `Product.Handle` in the list) | `Subscriptions.CreateSubscription` <- `ProductFamilies.ListProductsForProductFamily` | in the subscribe service, before the claim write and before `CreateSubscription` is called: it reads the plans list (same helper as `GET /api/subscription-plans`) and rejects (400) a handle not present, case-sensitive exact match; a single `ReadProductByHandle` is NOT used (proves existence, not that the family offers it) |
| Customer id passed to `CreateSubscription` must be the customer whose `reference` is the caller's user name | `Subscriptions.CreateSubscription` <- `Customers.ReadCustomerByReference` / `Customers.CreateCustomer` | in the subscribe service, the id comes from the response of those calls in the same request, never from a request body |

(The `GET /api/my-subscriptions` read is keyed by the token identity and is not an invariant row.)

## 3. Trap notes

| Step | Hazard and pointer |
| --- | --- |
| Client/DI | Client and `HttpClient` lifetime and what the options object captures; a per-request client leaks sockets, and the DI helper has registration subtleties. MUST load maxio-advanced-billing:dotnet-client-initialization |
| Auth | The credential is a two-part Basic pair and a blank part is not a missing one; a wrong/blank value surfaces as `AuthSchemeException` or 401. MUST load maxio-advanced-billing:dotnet-authentication |
| Every call | Operation input is ONE request record, optional-looking `Body` members, and `requestOptions`/`cancellationToken` position. MUST load maxio-advanced-billing:dotnet-calling-endpoints |
| Customer/subscription bodies | Required vs optional members, nullable `?` fields, open enums, `AdditionalProperties`; wire names differ from C# names. MUST load maxio-advanced-billing:dotnet-models |
| All catches | Which exception reaches which catch, Case A vs B, a 404 on a Case B operation, and the deserialization failure type. MUST load maxio-advanced-billing:dotnet-error-handling |
| Timeouts/retries/base URL/paging | What `Timeout` bounds vs the whole call, which verbs the SDK resends (POSTs here), base-URL override semantics, hand-paged lists. MUST load maxio-advanced-billing:dotnet-configuration-resilience |
| Tests | How to stub the HTTP seam and build SDK exceptions directly. MUST load maxio-advanced-billing:dotnet-testing |

## REQUIRED READING (load before implementing; plugin-qualified)

- `maxio-advanced-billing:dotnet-client-initialization` - client options, HttpClient lifetime, DI registration (step 2)
- `maxio-advanced-billing:dotnet-authentication` - BasicAuth credential and startup check (steps 1-2)
- `maxio-advanced-billing:dotnet-calling-endpoints` - request records, response shapes (steps 3-5)
- `maxio-advanced-billing:dotnet-models` - request payloads, nullable members, open enums (steps 3-5)
- `maxio-advanced-billing:dotnet-error-handling` - catch ladders around every call (steps 3-5); always required
- `maxio-advanced-billing:dotnet-configuration-resilience` - retries, timeout, base URL, paging (steps 2-5)
- `maxio-advanced-billing:dotnet-testing` - HttpClient stub seam (step 7)

Hazard row (verbatim): a body that does not match its declared type - a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape - surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Credentials are bound from `IConfiguration` section `Maxio:` (not the SDK's own env vars; `Program.cs` already calls `AddEnvironmentVariables()`, so `Maxio__ApiKey` etc. override as usual). A startup validation (options validated on start, `ValidateOnStart`, or an explicit check in `Program.cs` before `Build()`) refuses to start when `Maxio:ApiKey`, `Maxio:Subdomain` or `Maxio:ProductFamilyHandle` is null/empty/whitespace; `Maxio:BaseUrl` is optional but, if set, must be an absolute URI. BasicAuth has two parts: Username = ApiKey (checked), Password = the SDK-documented constant `x` (never blank). DECIDED: no value is placed in any committed `appsettings*.json`; values come from user-secrets/env. |
| 2 | Secret sourcing & rotation | ApiKey from `IConfiguration` (user-secrets in dev, env/secret store in prod). Options object is built once at DI registration and captured in the singleton client; a rotated key takes effect only after a process restart. No rotation without restart required (DECIDED). |
| 3 | Total timeout budget | Target: a Subscribe request must complete or fail within ~30 s end to end, a read within ~15 s. Enforced by (a) `options.Retry.Timeout` per attempt set explicitly (value to be chosen after loading `dotnet-configuration-resilience`, which says what `Timeout` bounds), and (b) a `CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted)` with `CancelAfter(budget)` per endpoint, passed as `cancellationToken` to every SDK call. Subscribe makes up to 4 sequential provider calls, so the budget is enforced on the whole handler, not per call. |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POSTs. Whether the SDK resends them is determined by `RetryOptions.HttpMethodsToRetry` (see `dotnet-configuration-resilience`, *Retries*). DECIDED: the plan sets `HttpMethodsToRetry` to GET-only on this client so the application, not the SDK, owns write retries (the claim + reconciliation below is the only retry path); reads (GET) may be retried by the SDK. |
| 5 | Idempotency & ambiguous writes | Neither `CreateCustomerOperationRequest` nor `CreateSubscriptionOperationRequest` carries an idempotency-key/request-id member (read both record files' shapes: only `Body`). The injected `Idempotency-Key` header is a fresh Guid per call and is not a key. Real keys: customer = `Customer.Reference` set to the user name (provider-enforced uniqueness, docs on `CreateCustomer`); subscription = the local primary-key claim (DUPLICATE CLAIMS). Reconciliation = UNKNOWN OUTCOMES. |
| 6 | Observability | Information: subscribe accepted (user name, plan handle, resulting subscription id), plan list size. Warning: claim conflict (duplicate subscribe), plan handle rejected, provider 422. Error: provider 5xx/connection/timeout/deserialization failures, with HTTP status and, for Case A, the error body's content (not request bodies); the request correlation id from `BaseRequest.CorrelationId()` is attached to every log line and returned in the response. The provider's own correlation id: **UNVERIFIED** - no request-id member is declared on the error types read; if the response `Headers` on `ApiException` carry one, log it (read `Core/Exceptions/ApiException.cs` at implementation). Use `IAppLogger<T>` (`LoggerAdapter`) as the repo does. |
| 7 | Sensitive data | Scope carries personal data (email, name) in `CreateCustomer` bodies and the API key in the auth header. `options.Logging.LogRequestBody` stays off, `options.Logging.LoggerFactory` is assigned explicitly (so the `MAXIO_ADVANCED_BILLING_LOG` env var cannot arm body logging), no request body is echoed in own logs. No card data is handled (payment not required). Form-body deny-list: N/A - all bodies in scope are JSON. |
| 8 | Environment selection | Server groups in the map: `Production` (`https://{site}.chargify.com` Us / `https://{site}.ebilling.maxio.com` Eu) and `Ebb` (`https://events.chargify.com/{site}`). This scope touches only `Production`. Environment `Us`. SDK declares no sandbox environment; test traffic is kept off the live system by pointing `Maxio:Subdomain` at the sandbox site (or `Maxio:BaseUrl` at a sandbox/mocked host) per deployment; the sandbox subdomain/API key are supplied by config, never committed. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |
| 12 | Reads that start from local state | `GET /api/my-subscriptions` does not start from local rows: it resolves the customer at the provider by `Customers.ReadCustomerByReference(reference = token user name)`, then `Customers.ListCustomerSubscriptions(CustomerId)`. A 404 from the reference lookup (provider-confirmed no customer) returns an empty list; any other failure is an error, never an empty list. An empty/reset in-memory store, a restart, or a new user therefore returns correct data. The local `SubscriptionClaim` table is used only as the duplicate guard. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `POST /api/subscriptions` -> `Subscriptions.CreateSubscription` (and the customer creation inside it) | New EF entity `SubscriptionClaim` in `CatalogContext` (the store this codebase already keeps its records in; in-memory DB at runtime, so the guard is a primary key, which the in-memory provider enforces). Key = composite primary key `(UserName, PlanHandle)` (DECIDED: one subscription per user per plan; a user may hold different plans; the key is the token user name + validated plan handle). Columns: `UserName`, `PlanHandle`, `Status` (Claimed/Completed), `MaxioCustomerId?`, `MaxioSubscriptionId?`, `CreatedAt`. Order: claim row inserted and saved BEFORE any SDK call. A provider idempotency key is not available (see row 5), so none is placed beside it. | The store refusing a second insert of the same key (primary key violation). Not a read-then-write. | In the subscribe service around the claim `SaveChanges`: `catch (DbUpdateException)` (and `InvalidOperationException`/`ArgumentException` which the in-memory provider may raise for a duplicate tracked key - **UNVERIFIED**: exact exception type of the in-memory provider is settled by a test) -> throws `DuplicateException`, which `ExceptionMiddleware` already maps to 409; on 409 for an existing `Completed` claim the endpoint may return the earlier result (read from the provider via the my-subscriptions path), for `Claimed` it returns 409 "in progress". The claim is released (row deleted) when the provider definitively refuses (422/4xx) so the user can retry; it is KEPT on unknown outcome until settled. Implemented: the in-memory provider raises ArgumentException (not DbUpdateException) on a duplicate key, so the catch covers both. | `MaxioSubscriptionService.ClaimAsync` (claim write: `SubscriptionClaims.Add` + `SaveChangesAsync`) then `_client.Subscriptions.CreateSubscription` in `SubscribeAsync` |

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| Plans list (`ListProductsForProductFamily`) | `PerPage` max 200 (map record: `[Maximum(200)]`); service walks `Page` 1..n until a page returns fewer than `PerPage` items, with a hard ceiling of 10 pages (DECIDED) | response field `IsTruncated: bool` on the `GET /api/subscription-plans` response DTO (true only if the ceiling was hit with a full last page); the same flag makes subscribe validation fail closed (500-class "plans list incomplete") rather than rejecting a possibly-valid handle | `MaxioSubscriptionService.ReadPlansAsync` (sets `truncated`), `SubscriptionPlanList.IsTruncated`, checked in `SubscribeAsync` |
| `ListCustomerSubscriptions` | none documented on the record (no page members); **UNVERIFIED** whether the provider caps the response | response DTO field `IsTruncated: bool` set true when the returned count reaches 200 (DECIDED defensive heuristic) | `MaxioSubscriptionService.ListMySubscriptionsAsync` sets `SubscriptionList.IsTruncated` |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `Customers.ReadCustomerByReference` | `Reference` = token user name | `MaxioSubscriptionService.EnsureCustomerAsync` catch (`ApiException or SdkConnectionException`) re-reads by reference | Covered by live run only; no stubbed-connection unit test was written (gap) |
| `Subscriptions.CreateSubscription` | `Customers.ListCustomerSubscriptions` | `CustomerId` of the customer from the same request, then match `Subscription.Product.Handle == planHandle` and a state other than Canceled/Expired | `MaxioSubscriptionService.SubscribeAsync` final `catch (SdkException ...)` calls `TrySettleAsync` (re-reads `ListCustomerSubscriptions`); unresolved leaves the claim Claimed and throws `SubscriptionException(Pending)` -> 202 | Covered by live run only; no stubbed-connection unit test was written (gap) |

## 6. Assumptions & Blockers

Blockers: none.

Assumptions (all DECIDED (implemented) unless stated):
1. Caller identity: the only claims the JWT carries are `ClaimTypes.Name` and `ClaimTypes.Role` (read in `IdentityTokenClaimService`); the user name is the identity and the Maxio customer `reference`. No NameIdentifier/sub/email claim exists. Email is obtained from `UserManager<ApplicationUser>` by that name. Customer first name = user name, last name = a fixed literal chosen at implementation (the SDK requires `FirstName`, `LastName`, `Email` on `CreateCustomer`; the token holds none). If the user has no email the endpoint returns 400 and no provider call is made.
2. "Plans" = Maxio products of the family `Maxio:ProductFamilyHandle`, addressed as `handle:<value>` in `ProductFamilyId` (per the member doc on the request record). Plan handle = `Product.Handle`; archived products (`ArchivedAt != null`) are excluded from the offered list. The default price point is used (no price point passed); price comes back as `Subscription.ProductPriceInCents`.
3. Metered component `api-call` is not attached at subscribe (not requested).
4. Payment not required: no payment profile/card fields are sent; if the provider nevertheless answers 422, it is surfaced as a 4xx to the caller (the plan expects no rejection since `RequireCreditCard` is stated false; verify from `Product.RequireCreditCard` in the plans list at runtime and log a warning if true).
5. Claim key = `(UserName, PlanHandle)`; double-click on the same plan returns 409 (or the earlier result when the claim is Completed).
6. Status of a missing customer on `ReadCustomerByReference` is assumed 404 (map shows Case B only) - **UNVERIFIED**; code treats `ApiException<RawError>` with `StatusCode == 404` as "no customer" and every other failure as an error.
7. Endpoint DTO shape: plan = `{ handle, name, description, priceInCents, interval, intervalUnit }`; subscription = `{ subscriptionId, planHandle, planName, priceInCents, state, nextBillingAt }` where `nextBillingAt = Subscription.NextAssessmentAt` (fallback `CurrentPeriodEndsAt`).
8. Config keys appear in no committed settings file with values; the `Maxio:` section is absent from `appsettings.json`.

### Decisions settled at implementation (code members)

- Identity: `ClaimTypes.Name` from `ClaimsPrincipal` in `SubscribeEndpoint.HandleAsync` / `MySubscriptionsEndpoint.HandleAsync`; customer reference = user name; email via `UserManager` in `MaxioSubscriptionService.SubscribeAsync`; last name literal `CustomerLastName`.
- Payment: `PaymentCollectionMethod = CollectionMethod.Remittance` on `CreateSubscription` (the sandbox returned 422 "No payment method was on file" with the default automatic collection; the model docs list `remittance` as the no-card option). Plan assumption 4 was wrong about the default behaviour.
- Env: `ServerEnvironment.Us`, `Maxio:BaseUrl` verbatim to `options.Server.Production.Us.BaseUrl`, else `Site = Maxio:Subdomain` (`MaxioServiceCollectionExtensions.AddMaxioSubscriptions`); startup validation in the same method throws on blank ApiKey / ProductFamilyHandle / Subdomain (when no BaseUrl).
- Retries: `HttpMethodsToRetry = GET only`, per-attempt timeout 10s, HttpClient timeout 15s, whole-call budget via linked token (`Bounded`): 30s subscribe, 15s reads.
- Claim lifecycle: released on definitive provider refusal / before-provider failures; stale (5 min) or Completed-but-not-live claims are freed on conflict (`ClaimAsync`); extra guard after restart: `FindLiveInCustomerAsync` before `CreateSubscription` (the in-memory store loses claims on restart).
- Error mapping: `SubscriptionException` -> 400 / 409 / 202 / 502 in `ExceptionMiddleware`; SDK text never reaches callers; 401/403/429 from Maxio map to 502.
- 404 on `ReadCustomerByReference` was confirmed live as "no customer" (previously UNVERIFIED).
- Gaps: no stubbed-HttpClient unit tests for the unknown-outcome paths; `ListCustomerSubscriptions` page cap remains UNVERIFIED.
