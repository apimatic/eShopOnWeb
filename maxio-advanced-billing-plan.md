# Maxio Advanced Billing plan — eShopOnWeb subscriptions (Subscribe flow)

SDK root (plugin-relative): `sdk/dotnet/`. All map/source paths below are relative to it. Project: `src/PublicApi` (.NET 8, MinimalApi.Endpoint style), EF Core in-memory `CatalogContext` (`src/Infrastructure/Data/CatalogContext.cs`).

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Bind and validate `Maxio:` settings (ApiKey, Subdomain, ProductFamilyHandle, BaseUrl optional); register one singleton SDK client | none |
| 2 | `GET /api/subscription-plans` — list products of the configured family, drop archived, map to plan DTO | `client.ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — resolve caller from JWT; validate handle against step-2 list (before any write); claim customer link; ensure Maxio customer; claim subscription; create subscription; record result | `ListProductsForProductFamily`, `Customers.ReadCustomerByReference` (reconcile), `Customers.CreateCustomer`, `Subscriptions.FindSubscription` (reconcile), `Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — read caller's stored customer link, list that customer's subscriptions at Maxio, map plan/price/state/next billing date | `Customers.ListCustomerSubscriptions` |
| 5 | EF entities `MaxioCustomerLink`, `MaxioSubscriptionClaim` in `CatalogContext` (own store) | none |

No capability is missing from the map. Not used: components (`api-call` metered component is out of Subscribe scope), payment profiles (payment method not required).

Repo conventions (pattern + exemplar):
- Endpoint class `IEndpoint<IResult, ...>` with `AddRoute`/`HandleAsync`, `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`, `.Produces<T>().WithTags("...Endpoints")`, route without leading slash: `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs`; folder + `Endpoint.Request/Response.cs` + Dto files: `src/PublicApi/CatalogItemEndpoints/`; list endpoint with no input: `src/PublicApi/CatalogBrandEndpoints/CatalogBrandListEndpoint.cs`; Response base class `src/PublicApi/BaseResponse.cs`.
- Endpoints auto-registered by `AddEndpoints()/MapEndpoints()` in `src/PublicApi/Program.cs`; services registered in Program.cs.
- EF entity config: `IEntityTypeConfiguration<T>` in `src/Infrastructure/Data/Config/BasketConfiguration.cs` (picked up by `ApplyConfigurationsFromAssembly`); entities in `src/ApplicationCore/Entities/`.
- Error mapping: `src/PublicApi/Middleware/ExceptionMiddleware.cs` (only `DuplicateException` -> 409, everything else -> 500 with message).
- JWT identity (read in `src/Infrastructure/Identity/IdentityTokenClaimService.cs`): token carries ONLY `ClaimTypes.Name` (username) plus role claims; no NameIdentifier, no email claim. Identity from token = `ClaimTypes.Name` -> `UserManager<ApplicationUser>.FindByNameAsync` to get the stable user Id and Email.

## 2. CONTRACT SHEET

⚠ Signatures below are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own — never flat arguments.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All operations: auth `options.BasicAuth`; server group `Production`; throw-only; no pagination (defaults); trailing params `MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken cancellationToken = default`. Controllers are properties on `MaxioAdvancedBilling.MaxioAdvancedBillingClient` (namespace of controllers: `MaxioAdvancedBilling.Api`).

| Controller property · method | Request record (`MaxioAdvancedBilling.Requests.<Controller>.`) + members | Body model + fields | Response + fields read | Error case / accessors / payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` · `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, ...)` | `ProductFamilyId: string, required` (doc: family id OR handle prefixed `handle:` -> pass `"handle:" + Maxio:ProductFamilyHandle`); `Page: int` (default 1, min 1); `PerPage: int` (default 20, max 200); optional left out: DateField, Filter, Start/End Date(time), IncludeArchived (unset), Include | none | `ProductResponse.Product` (required) -> `MaxioAdvancedBilling.Models.Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `InitialChargeInCents (initial_charge_in_cents): long?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialInterval (trial_interval): int?`, `RequestCreditCard (request_credit_card): bool?`, `RequireCreditCard (require_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?` | A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` (ns `MaxioAdvancedBilling.Core.Exceptions`); `TryGetString(out string)` [404 family not found]; `TryGetRawError(out RawError)` fallback | Return is a plain list; `Page`/`PerPage` are manual query params (no `Pageable`) -> see PAGED READS | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/Product.cs` |
| `client.Customers` · `Task<MaxioAdvancedBilling.Models.CustomerResponse> ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, ...)` | `Reference: string, required` (wire `reference`, query) | none | `CustomerResponse.Customer` (required) -> `Customer`: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | B: `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `.Error.StatusCode` (404 expected when absent — the 404 status for this op is not stated in the map, see UNVERIFIED below) | none | `map/operations/Customers.md`; `Api/Customers.cs` (route `/customers/lookup.json`, `<remarks>` "returns a single match") |
| `client.Customers` · `Task<CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, ...)` | `Body: MaxioAdvancedBilling.Models.CreateCustomerRequest?` (optional on record but must be set) | `CreateCustomerRequest`: `Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer, required`. `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` (set = deterministic user key; the reconciliation handle); optional others left out (cc_emails, organization, address..., tax_exempt, etc.) | `CustomerResponse.Customer.Id` (`int?`) — stored as the link's customer id | A: `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422]; `TryGetRawError(out RawError)` | none | `map/operations/Customers.md`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs`. No idempotency-key member on the record |
| `client.Subscriptions` · `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, ...)` | `Body: MaxioAdvancedBilling.Models.CreateSubscriptionRequest?` (must be set) | `CreateSubscriptionRequest`: `Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription, required`. `CreateSubscription` (nothing required): carry `ProductHandle (product_handle): string?` (validated handle), `CustomerId (customer_id): int?` (the Maxio customer id; the `<remarks>`/doc on `CustomerReference` says a customer is required unless `customer_id` or `customer_attributes` is given), `Reference (reference): string?` (deterministic subscription reference = reconciliation handle). Left out: product_id, price point fields, custom_price, coupons, components, payment profile/credit card/bank attributes, payment_collection_method, trial/billing dates, defer_signup | `SubscriptionResponse.Subscription` (`Subscription?`) -> `MaxioAdvancedBilling.Models.Subscription`: `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `Product (product): Product?`, `Customer (customer): Customer?`, `Reference (reference): string?` | A: `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422]; `TryGetRawError(out RawError)` | none | `map/operations/Subscriptions.md`; `Models/CreateSubscription.cs`; `Models/Subscription.cs`. No real idempotency-key member |
| `client.Subscriptions` · `Task<SubscriptionResponse> FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, ...)` | `Reference: string?` (query `reference`; not marked required — pass it) | none | `SubscriptionResponse.Subscription` as above | A: `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404]; `TryGetRawError(out RawError)` | none | `map/operations/Subscriptions.md` (record member optionality: `Requests/Subscriptions/FindSubscriptionRequest.cs`, read at implementation) |
| `client.Customers` · `Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, ...)` | `CustomerId: int, required` | none | each `SubscriptionResponse.Subscription` as above | B: `ApiException<RawError>` | none (default) | `map/operations/Customers.md` |

**CROSS-OPERATION INVARIANTS**

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A product handle the caller subscribes to must be one of the products the configured family offers (non-archived) | `CreateSubscription` (and `CreateCustomer`) ← `ListProductsForProductFamily` | In the subscribe service, before the customer claim, `CreateCustomer` or `CreateSubscription`: it reads the family list (`handle:`+`Maxio:ProductFamilyHandle`) and rejects (400) a handle not in it. A reject here makes no store write and no Maxio write. |
| The `CustomerId` passed to `CreateSubscription` must be a customer this application created/stored for the caller | `CreateSubscription` ← `CreateCustomer` / `ReadCustomerByReference` (stored in `MaxioCustomerLink`) | In the subscribe service: the id comes only from the caller's own `MaxioCustomerLink` row, never from the request body |
| `my-subscriptions` reads only the caller's customer id | `ListCustomerSubscriptions` ← `MaxioCustomerLink` | In the my-subscriptions service: id read from the link row of the JWT-derived user |

Enum values needed: `MaxioAdvancedBilling.Models.Enums.SubscriptionState` — open string enum, static members PascalCase (`Active`, `Canceled` confirmed in `Models/Enums/SubscriptionState.cs`); surface the wire string to callers via the enum's value, `TryGetKnownValue`/`Match` otherwise (read the file at implementation). `IntervalUnit` — `Models/Enums/IntervalUnit.cs` (read at implementation).

Client / auth / server facts:
- `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)` (only constructor); DI: `services.AddMaxioAdvancedBillingClient(options => ...)` (`MaxioAdvancedBilling` ns).
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (map: username is the API key, password is `x`).
- Environment: `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (value `US`, default). Operations in scope all use server group `Production`: `https://{site}.chargify.com` for Us. Site: `options.Server.Production.Us.Site = Maxio:Subdomain`. When `Maxio:BaseUrl` is set: `options.Server.Production.Us.BaseUrl = Maxio:BaseUrl` (literal URL without placeholders is used as-is; verbatim). Group `Ebb` is not touched. Set both before client construction (Environment is captured at construction).

### Trap notes (each hazard binds to a step)
- Step 1 client registration: when the options object is built, which fields are read at construction vs live, and what the DI extension does with `Logging.LoggerFactory` -> MUST load maxio-advanced-billing:dotnet-client-initialization.
- Step 1 credentials: the credential object has two parts and an unset part fails only on first call -> MUST load maxio-advanced-billing:dotnet-authentication.
- Step 1/3 server selection: `Environment` and `Server.*` are read at different times; mis-set one and it is silently ignored -> MUST load maxio-advanced-billing:dotnet-configuration-resilience.
- Every call: request record vs flat arguments, `Body` property on the `...OperationRequest` wrappers, response envelope one level down -> MUST load maxio-advanced-billing:dotnet-calling-endpoints.
- Create calls: `required` members, optional/nullable members, wire names versus C# names, open enums -> MUST load maxio-advanced-billing:dotnet-models.
- Step 3 catch ladders: which exception types reach which catch and which are not `ApiException<TError>` -> MUST load maxio-advanced-billing:dotnet-error-handling.
- Step 3 retries and total budget: which verbs the SDK may resend, what `Timeout` bounds, what bounds a whole call -> MUST load maxio-advanced-billing:dotnet-configuration-resilience.
- Tests: how to stub the SDK's `HttpClient` -> MUST load maxio-advanced-billing:dotnet-testing.

### REQUIRED READING (load all before implementing)
- maxio-advanced-billing:dotnet-client-initialization · step 1 client/DI registration
- maxio-advanced-billing:dotnet-authentication · step 1 credentials
- maxio-advanced-billing:dotnet-calling-endpoints · steps 2-4 calls
- maxio-advanced-billing:dotnet-models · steps 2-4 request/response models
- maxio-advanced-billing:dotnet-error-handling · steps 2-4 error boundary
- maxio-advanced-billing:dotnet-configuration-resilience · retries, budget, server selection, paging
- maxio-advanced-billing:dotnet-testing · tests

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 3. Own-store model (EF, `CatalogContext`)

| Entity | Key | Columns | Purpose |
| --- | --- | --- | --- |
| `MaxioCustomerLink` | PK `UserId` (string, max 256; the identity user Id) | `MaxioCustomerId: int?`, `Reference: string`, `ClaimedAtUtc`, `Status` (Pending/Ready) | one Maxio customer per eShop user |
| `MaxioSubscriptionClaim` | composite PK (`UserId`, `ProductHandle`) | `MaxioSubscriptionId: int?`, `Reference`, `ClaimedAtUtc`, `Status` (Pending/Active/Unknown) | one subscription per user per plan |

Both enforced by PRIMARY KEY (refused even by the EF in-memory provider; no migrations needed). Added as `DbSet`s on `CatalogContext` with `IEntityTypeConfiguration` classes in `src/Infrastructure/Data/Config/`.

## 4. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bind `Maxio:` section to an options class; `ValidateOnStart` (or explicit check in Program.cs before `Build`) requires non-blank `ApiKey` and `ProductFamilyHandle`, and non-blank `Subdomain` unless `BaseUrl` is set (Subdomain is still the documented key; if both blank, refuse start). Binding is from `IConfiguration`, not the SDK's own env fallback, because the code sets `BasicAuth` itself; the SDK password is the literal `x` so only ApiKey needs checking. Host refuses to start rather than raising `AuthSchemeException` at first call. Decision: `MaxioSettings.Validate` + `AddMaxioSubscriptions` (`ValidateOnStart`); Subdomain required unless BaseUrl is set. |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` from `IConfiguration` (user-secrets/env/Key Vault at deploy; never in `appsettings*.json` in repo — those carry empty placeholders only). Options object built once at DI registration and captured by the singleton client; rotation takes effect only after process restart. No rotation without restart required. |
| 3 | Total timeout budget | Per attempt `options.Retry = RetryOptions.Default() with { Timeout = 10s }` and the `HttpClient` timeout 10s; whole-call budget 30s enforced in the single service class fronting the SDK (`Bounded(...)` with a token linked to `HttpContext.RequestAborted` and `CancelAfter(30s)`). The subscribe handler makes up to 4 SDK calls (list, read-customer reconcile, create customer, create subscription) and uses ONE shared 30s deadline token across them, so the worst case the caller sees is 30s. Decision: 30s in `SubscriptionService.Budget`, per-attempt 10s in `AddMaxioSubscriptions`. |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POST: the SDK default `HttpMethodsToRetry` (GET, HEAD, PUT, OPTIONS) does not resend them on transient failures; keep defaults and do NOT widen the list. Reads (GET) are retried by the SDK (`MaxRetries` lowered to 2 to fit the budget). No OAuth2 scheme here (Basic), so no 401 re-send. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no real key member on its request record; key is deterministic `customer.reference` = `eshop-user-{UserId}` stored beside the claim, reconcile via `ReadCustomerByReference`. `CreateSubscription`: no real key member; deterministic `subscription.reference` = `eshop-sub-{UserId}-{ProductHandle}`, reconcile via `FindSubscription`. The injected `Idempotency-Key` header is not a key and is not relied on. |
| 6 | Observability | Information: request/response lines from the SDK logger (path only is unmasked; keys like `reference` are in the path/query and masked by allow-list); app logs claim won/lost, outcome settled, per request. Warning: 422/rejections, stale Pending claim reclaimed. Error: unknown outcome, unexpected exceptions. Correlation: the typed 422 payload (`ErrorListResponse1`/`CustomerErrorResponse1`) and `RawError.StatusCode` text are logged by status and error message only, plus the eShop `CorrelationId`/`HttpContext.TraceIdentifier`; Maxio error bodies carry no named correlation id in the models read (UNVERIFIED whether response headers carry one; headers reachable only via `SdkHook.OnResponse`). |
| 7 | Sensitive data | Request carries personal data (customer name, email). `LogRequestBody` stays off; `options.Logging.LoggerFactory` assigned explicitly from the DI `ILoggerFactory` (DI extension does this when null, but set explicitly so `MAXIO_ADVANCED_BILLING_LOG=trace` cannot force body logging); our own logs never echo request bodies or email. No card data is in scope (payment method not required). |
| 8 | Environment selection | Server groups in the SDK: `Production` (Us `https://{site}.chargify.com`, Eu `https://{site}.ebilling.maxio.com`) and `Ebb` (events). Scope touches only `Production`. Every deployment runs `ServerEnvironment.Us` (task: MAXIO_ENVIRONMENT=US); sandbox traffic is separated by site (`Maxio:Subdomain` = the sandbox site `cp-exp-1`, supplied by config, never in code) or by `Maxio:BaseUrl` (used verbatim). The SDK declares no sandbox environment member. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `MaxioCustomerLink` row in `CatalogContext` (EF), inserted `Pending` before the SDK call | PRIMARY KEY on `UserId`: the second `SaveChangesAsync` of an insert for the same user throws | `catch (Microsoft.EntityFrameworkCore.DbUpdateException)` around the claim insert in the subscribe service; on catch, the loser loads the existing row: `Ready` -> reuse its customer id; `Pending` younger than the claim TTL -> reply 409 "in progress"; `Pending` older than TTL -> reconcile via `ReadCustomerByReference` instead of creating. A 422 from Maxio removes the claim row (release). | `SubscriptionService.EnsureCustomerAsync`: claim = `_db.MaxioCustomerLinks.Add` + `SaveChangesAsync` (catch `IsClaimConflict`: DbUpdateException, or the bare ArgumentException the in-memory provider raises); SDK call = `MaxioGateway.CreateCustomerAsync` |
| `Subscriptions.CreateSubscription` | `MaxioSubscriptionClaim` row, inserted `Pending` before the SDK call (after the customer is Ready) | PRIMARY KEY (`UserId`, `ProductHandle`) | `catch (DbUpdateException)` around the claim insert; loser loads existing: `Active` -> return the stored subscription (200, same body, no write); `Pending` within TTL -> 409; stale/`Unknown` -> reconcile via `FindSubscription` by reference. A 422 removes the claim row (release). | `SubscriptionService.EnsureSubscriptionAsync`: claim = `_db.MaxioSubscriptionClaims.Add` + `SaveChangesAsync` (same `IsClaimConflict` catch); SDK call = `MaxioGateway.CreateSubscriptionAsync` |

Order for each row: claim -> SDK call -> record result.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ProductFamilies.ListProductsForProductFamily` (manual `Page`/`PerPage=200`; loop until a page returns fewer than 200 items) | page cap of 5 pages (1000 products) | the plan-list response carries `IsPartial: bool`; the subscribe path treats a capped list as unable to prove "not offered" and fails closed with 502/503 rather than rejecting or accepting | `MaxioGateway.ListPlansAsync` returns `IsPartial`; `SubscriptionService.ListPlansAsync` sets `PlanListDto.IsPartial`; `SubscribeAsync` fails closed with 503 |
| `Customers.ListCustomerSubscriptions` | none: operation has no pagination (default) | n/a — single response | `MaxioGateway.ListCustomerSubscriptionsAsync` |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `Customers.ReadCustomerByReference` | `customer.reference` = `eshop-user-{UserId}` | `SubscriptionService.EnsureCustomerAsync` `catch (MaxioOutcomeUnknownException)` (re-read `FindCustomerIdByReferenceAsync`; else claim stays Pending and the stale-claim path re-reads next request) | `MaxioGatewayTests.CreateCustomer_ConnectionFailure_IsUnknownOutcome` |
| `Subscriptions.CreateSubscription` | `Subscriptions.FindSubscription` | `subscription.reference` = `eshop-sub-{UserId}-{ProductHandle}` | `SubscriptionService.EnsureSubscriptionAsync` `catch (MaxioOutcomeUnknownException)` (re-read `FindSubscriptionByReferenceAsync`; else Pending, 409) | `MaxioGatewayTests.CreateSubscription_ConnectionFailure_IsUnknownOutcome_AndSentOnce` |

Settled in the failing write's own catch for `SdkConnectionException` / `SdkTimeoutException` (namespace `MaxioAdvancedBilling.Core.Exceptions`): re-read by reference; found -> record Ready/Active; not found / re-read fails -> mark claim `Unknown`/`Pending`, reply 202/409 "outcome pending", and the next request for the same user settles it by the same re-read (never by blind re-create).

## 5. Endpoints (decided: the SubscriptionEndpoints folder in PublicApi; errors mapped by `SubscriptionEndpointHelpers.Failure`)

| Route | Auth | Request | Response |
| --- | --- | --- | --- |
| `GET api/subscription-plans` | JWT (`[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`) | none | list of `{ handle, name, description, priceInCents, interval, intervalUnit }` + `isPartial` |
| `POST api/subscriptions` | JWT | `{ productHandle }` (required; caller identity only from token) | `{ subscriptionId, productHandle, planName, priceInCents, state, nextBillingAt }`; 201 on first create, 200 when the existing claim is returned; 400 unknown handle; 409 in progress; 422->400/409 mapping of Maxio refusal |
| `GET api/my-subscriptions` | JWT | none | list of same subscription shape; empty list when caller has no link |

Errors: add domain exceptions or return `Results.*` directly; `ExceptionMiddleware` only maps `DuplicateException` -> 409, others -> 500, so extend it or return results from the endpoint (decide at implementation).

## 6. Assumptions & Blockers

- Assumption (decision needed, minor): the request body for `POST /api/subscriptions` requires `productHandle`. The task names `eshop-pro` as the "default subscribe target", but the four allowed `Maxio:` keys include no default-plan key and "nothing hard-coded", so the default is a client preselection (the plan list), not a server default. If a server-side default is wanted it needs a fifth config key, which contradicts "exactly keys".
- Assumption: the JWT carries only `ClaimTypes.Name` (username) (read in `IdentityTokenClaimService`); the stable user key is the identity user Id obtained by `UserManager<ApplicationUser>.FindByNameAsync(name)`; the Maxio customer email is that user's `Email`. `CreateCustomer` requires `first_name`/`last_name` and the Identity user has none in the claims read: decision: username as first name, `Shopper` as last name (`SubscriptionService.EnsureCustomerAsync`).
- Assumption: `MAXIO_ENVIRONMENT=US` is satisfied by `ServerEnvironment.Us` fixed in code; no `Maxio:Environment` key is added (not in the allowed key set).
- Assumption: the product family handle lookup uses `"handle:" + Maxio:ProductFamilyHandle` as `ProductFamilyId` (documented on the request member).
- Assumption: "no trial, no setup fee, payment method not required" are properties of the seeded plans; the code sends no trial/price-point/payment fields, so Maxio applies the plan defaults.
- SETTLED LIVE: `CreateSubscription` with default (automatic) collection returned 422 "No payment method was on file" for the Pro plan. Decision: send `PaymentCollectionMethod = Remittance` (field on `Models/CreateSubscription.cs`) in `MaxioGateway.CreateSubscriptionAsync`; the subscription then returns `active` with a next-billing date.
- UNVERIFIED (original list): (a) whether `CreateSubscription` without a payment profile or `payment_collection_method` succeeds for the seeded plans — if Maxio answers 422 the typed `ErrorListResponse1` is surfaced as a 4xx with its message and nothing is retried; do not add speculative fields; (b) the HTTP status `ReadCustomerByReference` returns for an unknown reference is expected 404 (Case B gives `RawError.StatusCode`; treat 404 as "not found", any other status as failure); (c) whether Maxio enforces uniqueness of `customer.reference`/`subscription.reference` — not relied upon: our PRIMARY KEY claim is the duplicate refusal; (d) whether the returned `Subscription.State` is `active` immediately.
- Persistence note: the app's store is in-memory in this run, so claims do not survive process restart; the PRIMARY KEY is enforced by EF's in-memory provider and by SQL Server in non-in-memory configs. After a restart the reconcile-by-reference path (re-read Maxio) recovers a missing link. This is not a Blocker because the codebase has an EF store (`CatalogContext`) to hold the claim.
- Blockers: none.
