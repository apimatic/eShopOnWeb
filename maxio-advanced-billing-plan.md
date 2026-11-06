# Maxio Advanced Billing plan - eShopOnWeb recurring subscriptions (Subscribe flow)

SDK source (plugin-relative): `sdk/dotnet/` (map: `sdk/dotnet/sdk-map.md`, pages `sdk/dotnet/map/operations/*.md`). Referenced from `src/PublicApi/PublicApi.csproj` by `<ProjectReference>` to `sdk/dotnet/src/MaxioAdvancedBilling/MaxioAdvancedBilling.csproj` in the plugin (never copied; machine-local path, a teammate/CI must re-point it).

## 1. Scope & sequence

| # | Step | Operations used |
| --- | --- | --- |
| 1 | Bind and validate `Maxio:` settings at startup (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl` optional); register one singleton `MaxioAdvancedBillingClient`. | none |
| 2 | `GET /api/subscription-plans`: list the plans Maxio returns for the configured product family (handle-addressed). | `client.ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` (body: plan handle; identity = JWT `ClaimTypes.Name`): validate plan handle is in the offered set (step 2 read) -> claim customer link -> ensure Maxio customer (lookup by reference, else create) -> claim subscription -> create subscription -> record result -> return plan/price/state/next billing date. | `ListProductsForProductFamily`, `client.Customers.ReadCustomerByReference`, `client.Customers.CreateCustomer`, `client.Subscriptions.CreateSubscription`, (reconcile) `client.Subscriptions.FindSubscription`, `client.Customers.ListCustomerSubscriptions` |
| 4 | `GET /api/my-subscriptions`: resolve caller's Maxio customer by reference = caller identity (local row is cache only), list their subscriptions. | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` |
| 5 | Local cache/claim tables in `CatalogContext` (EF; in-memory under `UseOnlyInMemoryDatabase=true`). | none |

Repo survey (pattern -> ONE exemplar to imitate):
- Endpoint: one class per route implementing `MinimalApi.Endpoint.IEndpoint<IResult, TRequest, TDeps...>`, `AddRoute` does `app.MapGet/MapPost("api/...")`, `.Produces<TResponse>().WithTags("...Endpoints")`, request/response in sibling files `X.XRequest.cs`/`X.XResponse.cs` deriving `BaseRequest`/`BaseResponse` (CorrelationId) -> `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` (+ its `.CreateCatalogItemRequest.cs`/`.CreateCatalogItemResponse.cs`). Auto-registered by `AddEndpoints()`/`MapEndpoints()` in `src/PublicApi/Program.cs`.
- JWT auth on an endpoint: `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` on the route lambda (same file; that one adds `Roles =`, ours must not).
- Caller identity: the token minted by `src/Infrastructure/Identity/IdentityTokenClaimService.cs` carries only `ClaimTypes.Name` (user name) and `ClaimTypes.Role`; there is NO NameIdentifier/email claim. Caller identity = `ClaimTypes.Name`.
- Settings: `builder.Services.Configure<T>(builder.Configuration...)` in `src/PublicApi/Program.cs`; user-secrets id already in `src/PublicApi/PublicApi.csproj`.
- Persistence: `src/Infrastructure/Data/CatalogContext.cs` (DbSets + `ApplyConfigurationsFromAssembly`), entity config exemplar `src/Infrastructure/Data/Config/BasketConfiguration.cs`; in-memory switch in `src/Infrastructure/Dependencies.cs`.
- Error mapping: `src/PublicApi/Middleware/ExceptionMiddleware.cs` (`DuplicateException` -> 409, anything else -> 500 with `exception.Message`, so SDK/internal messages must never escape it).
- Tests: `tests/UnitTests`, `tests/PublicApiIntegrationTests`.

## 2. CONTRACT SHEET

⚠ Signatures below are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer using the record's own property names - never flat arguments.
⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

Common tail of every signature: `, MaxioAdvancedBilling.Core.RequestOptions? requestOptions = null, System.Threading.CancellationToken cancellationToken = default`. All operations: `Auth: options.BasicAuth`; server group `Production`; throw-only; no `Pageable`.

| Controller property | Method signature | Request record + members | Body model + fields | Response envelope + fields read | Error case / accessors / payload | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` (`MaxioAdvancedBilling.Api.ProductFamilies`) | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>> ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, ...)` | `ProductFamilyId: string, required` (doc: family id OR its handle prefixed `handle:`); `Page: int = 1`; `PerPage: int = 20`; `IncludeArchived: bool?`; others (DateField, Filter, Start/End*, Include) left out | none | `ProductResponse.Product` (`product`, required) -> `MaxioAdvancedBilling.Models.Product`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `TrialInterval (trial_interval): int?`, `InitialChargeInCents (initial_charge_in_cents): long?`, `RequireCreditCard (require_credit_card): bool?`, `ArchivedAt (archived_at): DateTimeOffset?`, `ProductFamily (product_family): MaxioAdvancedBilling.Models.ProductFamily?` (`Handle (handle): string?`) | A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] (family not found); `TryGetRawError(out RawError)` fallback | plain list; manual `Page`/`PerPage` params (no `Pageable`); stop on a page shorter than `PerPage` | `map/operations/ProductFamilies.md`; `Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/Product.cs` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, ...)` | `Reference: string, required` (query `reference`) | none | `CustomerResponse.Customer` (`customer`, required) -> `Customer`: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?` | B: `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `StatusCode` (404 = no such customer) | none | `map/operations/Customers.md` |
| `client.Customers` | `Task<MaxioAdvancedBilling.Models.CustomerResponse> CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, ...)` | `Body: MaxioAdvancedBilling.Models.CreateCustomerRequest?` (nothing marked required on the record; the endpoint needs a body) | `CreateCustomerRequest.Customer (customer): CreateCustomer, required` -> `MaxioAdvancedBilling.Models.CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` (carry it: it is the lookup key); optional others left out | `CustomerResponse.Customer.Id` | A: `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422]; `TryGetRawError` fallback | none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`; `Models/CreateCustomer.cs` |
| `client.Customers` | `Task<IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>> ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, ...)` | `CustomerId: int, required` | none | each `SubscriptionResponse.Subscription` (`Subscription?`) -> `MaxioAdvancedBilling.Models.Subscription`: `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `Reference (reference): string?`, `Product (product): Product?` | B: `ApiException<RawError>` | none (single list; no page params on the record) | `map/operations/Customers.md`; `Models/Subscription.cs` |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, ...)` | `Body: MaxioAdvancedBilling.Models.CreateSubscriptionRequest?` | `CreateSubscriptionRequest.Subscription (subscription): CreateSubscription, required` -> `MaxioAdvancedBilling.Models.CreateSubscription`: `ProductHandle (product_handle): string?` (docs: required unless `product_id`); `CustomerReference (customer_reference): string?` (docs: required unless `customer_id`/`customer_attributes`) - or `CustomerId (customer_id): int?`; `Reference (reference): string?` (subscription's own app reference - carry it, it is the reconcile key). Deliberately left out: `PaymentProfileId`, `PaymentProfileAttributes`/`CreditCardAttributes`/`BankAccountAttributes` (payment not required), `NextBillingAt`, `InitialBillingAt`, `Components`, `CouponCode(s)`, `PaymentCollectionMethod`, `ProductPricePointHandle`, `CustomPrice` | `SubscriptionResponse.Subscription` (fields as above, plus `Customer.Id/Reference`) | A: `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422]; `TryGetRawError` fallback | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`; `Models/CreateSubscription.cs` |
| `client.Subscriptions` | `Task<MaxioAdvancedBilling.Models.SubscriptionResponse> FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, ...)` | `Reference: string?` (query `reference`; per map, not marked required) | none | `SubscriptionResponse.Subscription` | A: `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404]; `TryGetRawError` fallback | none | `map/operations/Subscriptions.md` |

Plan-read note: the offered set is `ListProductsForProductFamily` with `ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`, `IncludeArchived` unset, and any item with `Product.ArchivedAt != null` dropped.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| the plan handle accepted by `POST /api/subscriptions` must be a `Product.Handle` that the configured product family's plan list returns (not archived); an unoffered handle is rejected 4xx, never forwarded | `CreateSubscription` (`ProductHandle`) <- `ListProductsForProductFamily` | in the subscribe service, after the offered-set read and before the claim and before `CreateSubscription` is called (a truncated read that does not contain the handle is a 503, not "not offered") |

(`GET /api/my-subscriptions` is a read keyed by the caller's own token identity: no invariant row. It is scoped by the Maxio customer reference = caller identity.)

### Enums needed

| Enum (`MaxioAdvancedBilling.Models.Enums.SubscriptionState`, open string enum, static members) | Wire |
| --- | --- |
| Pending, FailedToCreate, Trialing, Assessing, Active, SoftFailure, PastDue, Suspended, Canceled, Expired, Paused, Unpaid, TrialEnded, OnHold, AwaitingSignup | `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup` |

Open enum: an undeclared value is possible; surface the raw wire value to the user. `IntervalUnit` values: read `Models/Enums/IntervalUnit.cs` at implement time (not copied here).

### Client construction / auth / server facts
- Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)`; DI alt `services.AddMaxioAdvancedBillingClient(options => ...)`. `sdk-map.md` "Getting a client".
- Auth: HTTP Basic, `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (map "Servers & auth": username is the API key, password is `x`). Set in code so SDK env vars are never consulted.
- Environment: `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) / `.Eu`. Group `Production` template `https://{site}.chargify.com` (Us) / `https://{site}.ebilling.maxio.com` (Eu); subdomain via `options.Server.Production.Us.Site` (`.Eu.Site` for Eu). Verbatim override: `options.Server.Production.Us.BaseUrl` (the selected environment's own options) = `Maxio:BaseUrl` when set. Group `Ebb` (`https://events.chargify.com/{site}`) is not touched by this scope. The SDK declares no sandbox environment: sandbox is selected purely by subdomain (a sandbox site) and/or `Maxio:BaseUrl`.
- `Environment` and `Server` are captured differently: set both before constructing the client.

## 3. Trap notes

| Step | Hazard | Skill |
| --- | --- | --- |
| 1 | The SDK reads its own env vars for an unset credential and never throws at construction; a blank half of a Basic credential is not a missing one; options are captured at construction. | MUST load `maxio-advanced-billing:dotnet-authentication` |
| 1 | Which `HttpClient` the client gets and who owns/disposes it, and what DI registration freezes at registration time. | MUST load `maxio-advanced-billing:dotnet-client-initialization` |
| 1,3,4 | Server selection: `Environment` and `Server` are read at different times, and overriding the base URL on the wrong environment's options is silently ignored. | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| 2,3,4 | Every operation takes one request record with optional `Body`; an operation with only optional members compiles with nothing set. | MUST load `maxio-advanced-billing:dotnet-calling-endpoints` |
| 3 | Nullable ids/enums/open-enum state values and the `AdditionalProperties` bag; wire names differ from C# names. | MUST load `maxio-advanced-billing:dotnet-models` |
| 2,3,4 | Two different error cases are in play (Case A typed for create/find/list-products, Case B raw for the reads); 404s are expressed differently per case. | MUST load `maxio-advanced-billing:dotnet-error-handling` |
| 3 | Transport failures on a write leave the outcome unknown; retries and the timeout knobs do not bound a whole call. | MUST load `maxio-advanced-billing:dotnet-configuration-resilience` |
| tests | The HTTP stub seam and constructing SDK exceptions. | MUST load `maxio-advanced-billing:dotnet-testing` |

## 4. REQUIRED READING (load before implementation)

- `maxio-advanced-billing:dotnet-error-handling` - every try/catch and the exception-to-HTTP translation layer.
- `maxio-advanced-billing:dotnet-authentication` - step 1 credential wiring and fail-fast.
- `maxio-advanced-billing:dotnet-client-initialization` - step 1 client/DI registration.
- `maxio-advanced-billing:dotnet-configuration-resilience` - retries, timeouts, base URL, manual paging, logging, unknown outcomes.
- `maxio-advanced-billing:dotnet-calling-endpoints` - steps 2-4 calls.
- `maxio-advanced-billing:dotnet-models` - steps 2-4 request/response mapping.
- `maxio-advanced-billing:dotnet-testing` - tests.

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Credentials are bound in code from `IConfiguration` (`Maxio:ApiKey`; password is the constant `x` from the map). Startup validation (`ValidateOnStart` or explicit guard in `Program.cs` before `app.Run`) refuses to boot when `Maxio:ApiKey`, `Maxio:Subdomain` or `Maxio:ProductFamilyHandle` is null/whitespace, naming the key, never the value. `Maxio:BaseUrl` is optional; if set it must be an absolute URI. Because credentials are set in code, the SDK's own env vars are not the source and are not checked. |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` from user-secrets (Development) / host configuration; never in a repo file (no value in `appsettings*.json`). Options object built once at registration, captured by the singleton client: rotation takes effect only on process restart (no rotation-without-restart requirement stated). |
| 3 | Total timeout budget | Per attempt `options.Retry.Timeout = 10s`; whole-call budget enforced by ONE `Bounded(...)` helper in the single service fronting the SDK: a `CancellationTokenSource` linked to `HttpContext.RequestAborted` with `CancelAfter(30s)` shared by all SDK calls in one request (subscribe makes up to ~4-5 calls). Caller sees <= 30s. YOUR CALL — not in the map (value of 30s is an application choice). |
| 4 | Write-retry ownership | Keep `HttpMethodsToRetry` at the SDK default (`GET, HEAD, PUT, OPTIONS`): the SDK never resends `CreateCustomer`/`CreateSubscription` (POST). Reads (`ListProductsForProductFamily`, `ReadCustomerByReference`, `ListCustomerSubscriptions`, `FindSubscription`) may be resent; `MaxRetries` set to 2. No OAuth2 in this SDK so no 401 re-send. Retry logic for writes is owned by reconciliation (row 11), not the SDK. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no idempotency-key member on its record; key = `Customer.Reference` (= caller identity) used for lookup via `ReadCustomerByReference` + local claim (DUPLICATE CLAIMS). `CreateSubscription`: no key member on its record; real key = deterministic `Subscription.Reference` (derived from caller identity + plan handle) searchable via `FindSubscription`, plus local claim. The injected `Idempotency-Key` header is not a key and is not cited. Whether Maxio rejects a duplicate `reference` is `UNVERIFIED`; the local claim is the guard regardless. |
| 6 | Observability | SDK `LoggerFactory` set explicitly from DI. Information: request/response lines (SDK, URL query masked by allow-list), our own one-line outcome per endpoint (caller identity, plan handle, claim result, Maxio ids). Warning: retries, claim conflicts, reconciliation started. Error: terminal SDK failures with exception. For `ApiException<TError>`/`RawError` log HTTP status and, from the raw body, any Maxio error list text/request id that is present (shape of correlation headers is `UNVERIFIED`; log status + `Headers` the exception exposes). The request's `CorrelationId` (BaseRequest) is on every line. |
| 7 | Sensitive data | Request bodies carry personal data (customer first/last name, email). `LogRequestBody` stays off (JSON bodies are logged verbatim otherwise); `LoggerFactory` assigned explicitly so `MAXIO_ADVANCED_BILLING_LOG=trace` cannot force body logging; our own logs never echo request bodies or the API key. No card data is in scope (payment not required, payment attributes not sent). |
| 8 | Environment selection | Groups: `Production` (touched by every operation in scope) and `Ebb` (not touched). Environments: `Us` (default), `Eu`. Each deployment sets `Maxio:Subdomain` (a sandbox site for this task; "target sandbox only") and optionally `Maxio:BaseUrl` (applied verbatim to `options.Server.Production.<Us>.BaseUrl`, which then wins over the subdomain template). Because exactly four `Maxio:` keys are allowed there is no environment key: environment is `Us` unless `Maxio:BaseUrl` points elsewhere. SDK has no sandbox environment: test traffic stays off live only by pointing at a sandbox site. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS below. Two rows (customer link, subscription). |
| 10 | Partial results | See PAGED READS below (plan list). `ListCustomerSubscriptions` has no paging parameters so it cannot cap. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES below. |
| 12 | Reads that start from local state | `my-subscriptions` and the customer step of subscribe read the local cache row first; when absent (new user, in-memory restart) they call `ReadCustomerByReference(Reference = caller identity)` at Maxio; only a Maxio 404 means "no customer" -> `my-subscriptions` returns an empty list that Maxio confirmed. Subscriptions are always listed at Maxio via `ListCustomerSubscriptions`, never answered from local rows. On a found customer the missing cache row is re-created. Maxio supports the lookup (`ReadCustomerByReference`), so no blocker. |

### DUPLICATE CLAIMS

Claim stores live in `CatalogContext` (the EF store the codebase keeps its records in). Order for each: claim -> SDK call -> record the result; release the claim when the provider refuses the call.

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateCustomer` (only reached when `ReadCustomerByReference` says 404) | `CatalogContext.MaxioCustomerLinks`, PK = caller identity, row `claiming` before the SDK call | PK violation on `SaveChangesAsync` | `catch (DbUpdateException or InvalidOperationException or ArgumentException)` (in-memory raises ArgumentException) around the claim insert; loser re-reads Maxio by reference else 409 | `MaxioSubscriptionService.EnsureCustomerAsync`: claim `SaveChangesAsync` then `CreateCustomerCall` |
| `CreateSubscription` | `CatalogContext.SubscriptionClaims`, composite PK (caller identity, plan handle), row `claiming` before the SDK call | composite PK violation on `SaveChangesAsync` | same ladder around the claim insert; loser returns the winner's subscription (found by reference at Maxio) else 409 | `MaxioSubscriptionService.EnsureSubscriptionAsync`: claim `SaveChangesAsync` then `CreateSubscriptionCall` |

A claim stuck in `claiming` after a crash is expired/settled by reconciliation (UNKNOWN OUTCOMES) via the Maxio lookup; a claim whose SDK call was definitively refused (4xx from Maxio, e.g. 422) is deleted. In-memory loses claims on restart: the lookup-before-create at Maxio (by reference) covers that gap.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (plans) | page cap (10 pages x `PerPage`; the maximum allowed `PerPage` is `UNVERIFIED`, default is 20) | the `GET /api/subscription-plans` response carries a `truncated: bool` field (true if the cap was hit with a full last page); subscribe returns 503 instead of "plan not offered" when truncated and the handle is not found | `MaxioSubscriptionService.ListPlansCoreAsync` (sets `PlanListResult.Truncated`) |

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `Customer.Reference` = caller identity | `EnsureCustomerAsync` `catch (ProviderException p)` (outcome-unknown arm): re-read, else claim `unknown` | none for customer (covered by the subscription test below; same ladder) |
| `CreateSubscription` | `Subscriptions.FindSubscription` | `Subscription.Reference` (`eshop:<user>:<plan>`) | `EnsureSubscriptionAsync` `catch (ProviderException p)` (outcome-unknown arm): re-read, else claim `unknown`; next subscribe call settles it via `FindSubscriptionAsync` | `MaxioSubscriptionServiceTests.ConnectionFailureOnCreateSubscription_*` |

The re-read runs in the failing write's own `catch (SdkConnectionException)` (includes `SdkTimeoutException`); if the re-read finds nothing or fails too, the claim row is set to `unknown` (not failed) and is settled by the next subscribe/my-subscriptions call for that caller.

## 6. Assumptions & Blockers

Blockers: none. Every capability needed exists in the map (list products by family handle, customer lookup by reference, subscription lookup by reference, create customer/subscription).

Assumptions:
1. Caller identity is the JWT `ClaimTypes.Name` (the only identity claim the existing token carries, read in `IdentityTokenClaimService.cs`); it is used as the Maxio customer `reference`. Customer `first_name`/`last_name`/`email` are required by `CreateCustomer` but the token carries none: YOUR CALL — not in the map (use `UserManager<ApplicationUser>` to read the stored email for the caller; name parts derived from it/the user name).
2. `POST /api/subscriptions` takes the plan handle in the request body. The task calls `eshop-pro` the default subscribe target but forbids hard-coded values and allows only four config keys, so there is no configured default: an omitted/blank plan handle is rejected 400. YOUR CALL — not in the map.
3. The harness env vars named in the task are deployment inputs only; the code reads the `Maxio:` binding keys. Mapping them onto those keys (user-secrets or `Maxio__*` host configuration) happens outside code. No `Maxio:Environment` key exists, so `Us` is used unless `Maxio:BaseUrl` overrides.
4. "Payment method NOT required": `CreateSubscription` is sent without any payment profile/attributes. Whether the site accepts that is `UNVERIFIED` (only live traffic settles it); a 422 is translated to a clear 4xx, not retried.
5. Whether a subscription is created in `active` state and has `NextAssessmentAt` populated on creation is `UNVERIFIED`; the response maps `next_assessment_at`, falling back to `current_period_ends_at`, and is null-safe.
6. `my-subscriptions` returns all of the caller's Maxio subscriptions, optionally restricted to the configured product family via `Subscription.Product.ProductFamily.Handle`: YOUR CALL — not in the map.
7. Local persistence (customer link + subscription claim) adds EF entities to `CatalogContext`; for the SQL Server path an EF migration would be needed, the in-memory run ignores migrations.
8. Metered component `api-call` and plans' trial/setup-fee settings are not touched by this scope (no component operations used).
9. Retry/timeout numbers (10s per attempt, `MaxRetries` 2, 30s total, 10-page cap) are application choices, YOUR CALL — not in the map.

## 7. Decisions settled at implementation time (supersede the YOUR CALL markers above)

- Customer name/email: email from `UserManager.FindByNameAsync(...).Email` (fallback: user name if it contains `@`, else 422); first name = email local part, last name `eShop` - `MaxioSubscriptionService.BuildCustomerAsync`.
- Plan handle: required in the POST body; missing/blank -> 400; no hard-coded default - `SubscribeCoreAsync`.
- `my-subscriptions`: always looks the customer up at Maxio by reference (the local row is only a claim, never consulted for reads); filtered to the configured family - `ListSubscriptionsCoreAsync`. Row 12 above is superseded by this.
- Budget: 10s per attempt, `MaxRetries` 2 (reads), 30s whole-request `Budget(...)`, plan cap 25 pages x 20 - `MaxioServiceCollectionExtensions`, `MaxioSubscriptionService`.
- **Found live (was UNVERIFIED):** with no payment method, `CreateSubscription` is refused 422 ("No payment method was on file") unless `PaymentCollectionMethod = Remittance` is sent (documented in `Models/CreateSubscription.cs`); the service always sends it. The subscription then comes back `active` with `next_assessment_at` populated.
- A subscription claim is per (user, plan): subscribing again to the same plan returns the existing subscription rather than creating a second one.
- Claims: `claiming` older than 2 min, `unknown` or stale rows are taken over; refused (non-unknown) calls release the claim.
- Local tables are EF entities in `CatalogContext`; the SQL Server path would need a migration (none added; in-memory ignores migrations).
- SDK referenced via `ProjectReference` in `src/Infrastructure/Infrastructure.csproj` pointing into the plugin directory (machine-local).
