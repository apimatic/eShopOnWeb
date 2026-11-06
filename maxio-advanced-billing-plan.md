# Maxio Advanced Billing plan: eShopOnWeb Subscribe flow (.NET)

SDK root (plugin-relative): `sdk/dotnet/` (map: `sdk/dotnet/sdk-map.md`, pages `sdk/dotnet/map/operations/<Controller>.md`). Source paths below are relative to `sdk/dotnet/src/MaxioAdvancedBilling/`.

## 1. Scope & sequence

Repo survey (read-only): endpoints in `src/PublicApi` follow the `MinimalApi.Endpoint` pattern; exemplar to imitate: `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs` (class `IEndpoint<IResult, TRequest, ...>` with `AddRoute` + `HandleAsync`, `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`, `.Produces<T>()`, `.WithTags(...)`, a `<Name>Request`/`<Name>Response` file pair deriving from `BaseRequest`/`BaseResponse` (correlation id), a `Dto` file). Registration is automatic via `AddEndpoints()/MapEndpoints()` in `src/PublicApi/Program.cs`. Duplicate refusals use `DuplicateException` -> `ExceptionMiddleware` (`src/PublicApi/Middleware/ExceptionMiddleware.cs`) -> 409; every other exception -> 500 (so the new code must map its own provider errors). Records are kept in `CatalogContext` (EF, in-memory when `UseOnlyInMemoryDatabase=true`; `src/Infrastructure/Dependencies.cs`). Caller identity: the JWT built by `src/Infrastructure/Identity/IdentityTokenClaimService.cs` carries **only** `ClaimTypes.Name` (user name) and `ClaimTypes.Role`; no email/sub claim.

Steps (each names the SDK operations it uses):

1. Config + client: bind `Maxio:` section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`), fail fast, register `MaxioAdvancedBillingClient` once. No SDK operation.
2. `GET /api/subscription-plans`: `client.ProductFamilies.ListProductsForProductFamily` (page loop, `ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`). Returns handle, name, price (`PriceInCents`), interval.
3. `POST /api/subscriptions` (body: plan handle; identity from `ClaimTypes.Name`):
   a. validate the handle against the set from step 2 (CROSS-OPERATION INVARIANTS);
   b. claim customer -> `client.Customers.ReadCustomerByReference` (reference = user name); if 404 -> `client.Customers.CreateCustomer` (Reference = user name); release/keep claim per DUPLICATE CLAIMS;
   c. claim subscription -> `client.Customers.ListCustomerSubscriptions` (provider pre-check, survives restarts) -> `client.Subscriptions.CreateSubscription` (`CustomerId` from b, `ProductHandle` = validated handle, `Reference` stamped, no payment attributes);
   d. return plan/price/state/next billing date from the `SubscriptionResponse`.
4. `GET /api/my-subscriptions`: `client.Customers.ReadCustomerByReference` (reference = user name; 404 => confirmed "no customer" => empty list) -> `client.Customers.ListCustomerSubscriptions`. Never gated on a local row.

## 2. CONTRACT SHEET

> Signatures are generated code, verbatim: each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer using the record's own property names, never flat arguments.
> Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

All operations: controller via `client.<Controller>` (type namespace `MaxioAdvancedBilling.Api`), throw-only, auth `options.BasicAuth`, server group `Production`, trailing params `RequestOptions? requestOptions = null, CancellationToken cancellationToken = default`.

| Controller property · method | Request record (`Name: type, required?`) | Body model (`Name (wire): type, required?`) | Response envelope + fields read | Error case / accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily(request, ...)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (id or `handle:<handle>`); `Page: int` (default 1, min 1); `PerPage: int` (default 20, max 200); `IncludeArchived: bool?` (leave unset); others unused | none | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; `.Product` (required) is `MaxioAdvancedBilling.Models.Product`: `Handle (handle): string?`, `Name (name): string?`, `Id (id): int?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `RequireCreditCard (require_credit_card): bool?`, `TrialInterval`, `InitialChargeInCents (initial_charge_in_cents): long?` | A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`; `TryGetString(out string)` [404, unknown family]; `TryGetRawError(out RawError)` fallback | Manual: `Page`/`PerPage`; map shows a plain list return, no `Pageable`. Stop when a page returns fewer than `PerPage` rows | map `Requests/ProductFamilies`, `ProductFamilies.md`; `Models/ProductResponse.cs`, `Models/Product.cs` |
| `client.Customers.ReadCustomerByReference(request, ...)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | none | `MaxioAdvancedBilling.Models.CustomerResponse`; `.Customer` (required) `MaxioAdvancedBilling.Models.Customer`: `Id (id): int?`, `Reference (reference): string?`, `Email`, `FirstName`, `LastName` | B: `ApiException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>`; `Error.StatusCode` (404 = no such customer; the map names no typed 404 here, the 404 meaning is **UNVERIFIED**) | none (single match, per XML remarks) | `Customers.md`; `Api/Customers.cs` remarks |
| `client.Customers.CreateCustomer(request, ...)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` (nullable but must be set) | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required`; `CreateCustomer`: `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` (optional; carry it: it is the lookup key). Left out: address/tax/org fields | `CustomerResponse`; `.Customer.Id` (int?) | A: `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`; `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422]; `TryGetRawError` fallback | none | `Customers.md`; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Models/Customer.cs` |
| `client.Customers.ListCustomerSubscriptions(request, ...)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | none | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; `.Subscription?` (nullable): `MaxioAdvancedBilling.Models.Subscription`: `Id (id): int?`, `State (state): MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Product (product): Product?` (null on the new catalog experience), `Reference (reference): string?`, `CreatedAt` | B: `ApiException<RawError>` | The record has no page members: the map shows none. Whether Maxio caps the list is **UNVERIFIED** | `Customers.md`; `Models/Subscription.cs` |
| `client.Subscriptions.CreateSubscription(request, ...)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` (must be set). No key-shaped member: no real idempotency key exists | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required`; `CreateSubscription` (nothing required; the docs tie acceptance to): `ProductHandle (product_handle): string?` (required unless `product_id`), `CustomerId (customer_id): int?` (required unless `customer_reference`/`customer_attributes`), `Reference (reference): string?` (app's own subscription reference). Payment members (`PaymentProfileId`, `*_attributes`) deliberately omitted: plans need no payment method (**UNVERIFIED** until a live call) | `MaxioAdvancedBilling.Models.SubscriptionResponse`; `.Subscription?` fields as above | A: `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`; `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422]; `TryGetRawError` fallback | none | `Subscriptions.md`; `Models/CreateSubscription.cs`; `Api/Subscriptions.cs` remarks (payment info "may be required depending on the product options"; 3DS 422 with action link) |
| `client.Subscriptions.FindSubscription(request, ...)` (optional re-read helper) | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string?` | none | `SubscriptionResponse` | A: `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`; `TryGetNoContent(out RawError)` [404]; fallback `TryGetRawError` | none | `Subscriptions.md` |

Namespaces to import for catch ladders: `MaxioAdvancedBilling.Core.Exceptions` (`ApiException<T>`, `SdkException` family), `MaxioAdvancedBilling.Core.ErrorResponse` (`RawError`), `MaxioAdvancedBilling.Errors` (typed errors).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The plan handle accepted by POST /api/subscriptions must be one returned for the configured family (`Maxio:ProductFamilyHandle`), compared against `Product.Handle`; "exists in Maxio" is not enough | `CreateSubscription` <- `ListProductsForProductFamily` | In the POST handler before the claim and before `CreateCustomer`/`CreateSubscription`: the handler reads the offered set (same helper that serves GET /api/subscription-plans) and returns 400 for a handle not in it |

### Enum / client facts

- `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (open string enum, static members): `Pending, FailedToCreate, Trialing, Assessing, Active, SoftFailure, PastDue, Suspended, Canceled, Expired, Paused, Unpaid, TrialEnded, OnHold, AwaitingSignup`. Treat `Canceled`, `Expired`, `FailedToCreate` as terminal (not "already subscribed"); resolve via `TryGetKnownValue`/`Match`, with an `otherwise` arm. Source `Models/Enums/SubscriptionState.cs`.
- Client: `MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)`; DI `services.AddMaxioAdvancedBillingClient(options => ...)`. Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (the map: username is the API key, password is `x`). Environment: `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default). Server node: `options.Server.Production.Us.Site = Maxio:Subdomain`; if `Maxio:BaseUrl` is set, `options.Server.Production.Us.BaseUrl = Maxio:BaseUrl` verbatim (default template `https://{site}.chargify.com`; source `Servers/ProductionOptions.cs`). The `Ebb` group is not touched by this scope.
- The SDK's own env vars (`MAXIO_ADVANCED_BILLING_BASIC_AUTH_USERNAME/_PASSWORD`) are NOT used; the credential is set in code from `Maxio:ApiKey`, since the task fixes that key name.

## 3. Trap notes

- Step 1, DI: lifetime/ownership of the `HttpClient` and how the options object is captured by the registration decide secret rotation and socket reuse. MUST load maxio-advanced-billing:dotnet-client-initialization
- Step 1, credential: Basic needs both username and password; a blank part fails only at first call and the SDK's env fallback can silently fill a value you did not set. MUST load maxio-advanced-billing:dotnet-authentication
- Steps 2-4, every call: argument shape (request record, optional trailing params, return envelope one level down, `Subscription?` nullable). MUST load maxio-advanced-billing:dotnet-calling-endpoints
- Step 3, models: `CreateSubscription`/`Customer` field nullability, open enums, `DateTimeOffset` handling, wire names. MUST load maxio-advanced-billing:dotnet-models
- Steps 2-4, resilience: which verbs the SDK resends on its own and what `Timeout` actually bounds decide how long a request thread waits and whether the create calls can run twice. MUST load maxio-advanced-billing:dotnet-configuration-resilience
- Steps 2-4, errors: the catch ladder differs between Case A (typed) and Case B (raw) operations; the 404-on-lookup signal is a different shape for `ReadCustomerByReference` than for `FindSubscription`. MUST load maxio-advanced-billing:dotnet-error-handling
- Tests: faking the HTTP seam to fail a connection mid-write. MUST load maxio-advanced-billing:dotnet-testing

## 4. REQUIRED READING (load all before implementing)

- maxio-advanced-billing:dotnet-error-handling · every catch ladder (steps 2-4); always required
- maxio-advanced-billing:dotnet-client-initialization · step 1
- maxio-advanced-billing:dotnet-authentication · step 1
- maxio-advanced-billing:dotnet-calling-endpoints · steps 2-4
- maxio-advanced-billing:dotnet-models · step 3
- maxio-advanced-billing:dotnet-configuration-resilience · steps 1-4 (retries, timeouts, base URL, logging)
- maxio-advanced-billing:dotnet-testing · tests

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | Bound from `IConfiguration` (`Maxio:` section) in the DI registration; startup validation (options validation run on start, or a check in `Program.cs`) throws when `Maxio:ApiKey`, `Maxio:Subdomain` or `Maxio:ProductFamilyHandle` is missing/blank (and the password part is the constant `x`, so only the API key needs checking). `Maxio:BaseUrl` is optional; when present it must be an absolute URI or startup fails. The SDK env vars are not the credential source, so the check follows `IConfiguration`. |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` comes only from .NET user-secrets (dev) or environment (`Maxio__ApiKey` form resolved by the host's configuration, bound by key); never in appsettings or the repo. Options are built once at registration and captured by the singleton client: rotation requires a process restart. |
| 3 | Total timeout budget | Set `options.Retry = RetryOptions.Default() with { Timeout = 10s, MaxRetries = 2 }` (per-attempt timeout; GET retries only by default). Each endpoint links the request-abort token to a 30 s overall `CancellationTokenSource`, which is the real bound on a whole call; the SDK's `Timeout` alone does not cap it. Writes get one attempt (POST is not retried by default). |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POST: the SDK does not resend them (default `HttpMethodsToRetry` is GET/HEAD/PUT/OPTIONS; `HttpMethodsToRetry` is left unchanged). Only the app's own reconciliation path (row 11) may re-issue them. GETs (`ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListProductsForProductFamily`) are SDK-retried. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: no key member on its request record; the `Idempotency-Key` header is not a key. Real lookup key = `CreateCustomer.Reference` (user name); reconcile with `ReadCustomerByReference`. `CreateSubscription`: no key member; reconcile with `ListCustomerSubscriptions(customerId)` matching `Product.Handle` and a non-terminal `State`; no subscription Reference is stamped. |
| 6 | Observability | `ILogger`: Information for each write start/outcome with userName hash, plan handle, Maxio customer/subscription id; Warning on reconciliation paths and claim conflicts; Error on unexpected provider failures. For `ApiException`, log `StatusCode` and the typed error body's message summary (never the request body); Maxio's error bodies as mapped here carry no request-id field in the map, so the correlation id logged is the app's own `BaseRequest.CorrelationId()` (read in the exemplar endpoint) plus the response headers' request-id if present (**UNVERIFIED** header name; not relied upon). |
| 7 | Sensitive data | The scope carries customer name/email (PII) but no card/bank data (no payment attributes sent). `LogRequestBody` stays off; `options.Logging.LoggerFactory` is assigned explicitly (DI extension supplies the container's); `MAXIO_ADVANCED_BILLING_LOG` must not be set in deployments. |
| 8 | Environment selection | Server groups: `Production` (`https://{site}.chargify.com` Us / `https://{site}.ebilling.maxio.com` Eu) is the only group these operations use; `Ebb` untouched. No separate sandbox environment member exists: the sandbox is a different **site** on the `Us` host. Each deployment sets `Maxio:Subdomain` to the sandbox site (and `Maxio:BaseUrl` only to point at a mock/other host, used verbatim). `ServerEnvironment` stays `Us`: no config key exists for Eu, an EU site is reached via `Maxio:BaseUrl`. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS (customer ensure and subscription create). The in-memory EF store (`CatalogContext`) is available, so no Blocker. |
| 10 | Partial results | See PAGED READS: the plans read returns a `PlansResponse` with `Truncated: bool` set when the page cap is reached. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |
| 12 | Reads that start from local state | GET /api/my-subscriptions and the pre-write checks never begin from a local row. The local `SubscriptionClaim` rows are only claims. With an empty store (new user, restart), the read calls `ReadCustomerByReference(reference = user name)`: 404 means a provider-confirmed "no customer" => empty list; otherwise `ListCustomerSubscriptions(customer.Id)`. The provider offers this lookup, so no Blocker. |

### DUPLICATE CLAIMS

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| Ensure customer (`CreateCustomer`) | New EF entity `SubscriptionClaim` in `CatalogContext` (the store this codebase keeps its records in), string primary key `cust:{userName}`, saved (SaveChanges) **before** `CreateCustomer` | Primary-key refusal of the second insert of the same key (enforced by the EF in-memory store) | `catch (DbUpdateException)` around the claim insert in the ensure-customer member; on catch: the second caller waits/re-reads via `ReadCustomerByReference` rather than creating; claim released (row deleted) if `CreateCustomer` is refused by Maxio (422) | `SubscriptionService.TryClaimAsync` (key `cust:{user}`), then `Customers.CreateCustomer` in `EnsureCustomerAsync`; claim released in its `finally` once settled (the by-reference read is the durable guard); `IsDuplicateKey` also catches the in-memory `ArgumentException` |
| Create subscription (`CreateSubscription`) | Same `SubscriptionClaim` entity, primary key `sub:{userName}:{planHandle}`, saved **before** the SDK call | Primary-key refusal of the second insert | `catch (DbUpdateException)` around the claim insert; second caller gets `DuplicateException` -> 409 via `ExceptionMiddleware`. Claim released on Maxio refusal (422 or other `ApiException`); retained on success | `SubscriptionService.TryClaimAsync` (key `sub:{user}:{plan}`), then `Subscriptions.CreateSubscription` in `SubscribeAsync`; released in `finally` (a retained claim would block resubscribing after cancel; the live-subscription pre-check `FindLiveSubscriptionAsync` is the durable guard). Second caller gets the existing subscription (200) or `DuplicateException` 409 |

Restart note: the claims are lost on restart (in-memory). The provider pre-check (`ListCustomerSubscriptions` for an existing non-terminal subscription to that `Product.Handle`) runs after the claim and before `CreateSubscription`, so a double-click after a restart is still caught at Maxio; the customer is reconciled by `Reference`.

### PAGED READS

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (plans, used by GET /api/subscription-plans and the POST validation) | A page-count cap in the app's loop (200 per page, bounded number of pages) | `Truncated: bool` on the plans result/`ListSubscriptionPlansResponse`; when the POST validation sees `Truncated == true` and the handle is not found it answers 503 rather than 400 | `SubscriptionService.ReadPlansAsync` sets `PlansResult.Truncated` -> `Truncated` on the response; `SubscribeAsync` answers 503 when truncated and handle unseen |

`ListCustomerSubscriptions` has no page members in the map (not a paged read here); a provider-side cap is **UNVERIFIED** (see Assumptions).

### UNKNOWN OUTCOMES

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `Reference` = user name (the `CreateCustomer.Reference` sent) | `catch (ProviderException)` in `SubscriptionService.EnsureCustomerAsync` (re-read by reference) | not written: no connection-failure test (live-verified happy path and races only) |
| `CreateSubscription` | `Customers.ListCustomerSubscriptions(CustomerId)` | Maxio customer id (from the reference lookup) + `Product.Handle` == plan handle + non-terminal `State` | `catch (ProviderException ex) when (ex.OutcomeUnknown)` in `SubscriptionService.SubscribeAsync` (re-read via `FindLiveSubscriptionAsync`) | not written: no connection-failure test |

Settlement happens in each write's own `catch (SdkConnectionException)` / `catch (SdkTimeoutException)` (and `ResponseDeserializationException` on a 2xx), before returning: re-read; found -> report as success with the found subscription; not found -> release the claim and answer 503 "not created, retry is safe".

## 6. Assumptions & Blockers

Blockers: none.

Assumptions:
1. Default subscribe target: the task names `eshop-pro` as the default, but the config keys are fixed to four and "no hard-coded values" applies. The plan: `POST /api/subscriptions` takes the plan handle in its body and **requires** it (missing => 400); the caller (and docs/sample) sends `eshop-pro`. No handle is baked into code. If a default is wanted without hard-coding, a fifth config key would be needed; not added.
2. Caller identity: the JWT carries only the `ClaimTypes.Name` claim (read from `IdentityTokenClaimService.cs`). That value is used as the Maxio `Reference`. The eShop default users have an email as user name; the customer email = that value if it contains `@`, otherwise a lookup of the Identity user's email via `UserManager<ApplicationUser>`; first/last name derived from the user name/email local part (Maxio requires first_name, last_name, email). If the user has no email at all, POST fails 422-style with a clear message.
3. Payment method not required and no trial/setup fee: no payment attributes are sent. If the sandbox product rejects the call with 422 (payment info required), that is a provider rejection to be surfaced as 422/502 to the user, not worked around. **UNVERIFIED** until live.
4. `ReadCustomerByReference` 404 (Case B, status 404) is read as "no such customer". The map names no typed 404 for it: **UNVERIFIED** (a 422/other status is treated as an error, not as "none").
5. `Customer.Reference` uniqueness at Maxio is relied on for the idempotent ensure: **UNVERIFIED**. Mitigated by the claim and the read-before-create.
6. `ListCustomerSubscriptions` returns all subscriptions of the customer in one response (map shows no page members); if Maxio caps it, my-subscriptions could be silently short: **UNVERIFIED**. A user with an unusually large number of subscriptions is not expected here.
7. Plans offered = products returned by `ListProductsForProductFamily` for `handle:<Maxio:ProductFamilyHandle>` with `IncludeArchived` unset (archived excluded). The metered component `api-call` is not a plan and is not offered. Subscribing to a plan does not touch components.
8. `GET /api/my-subscriptions` lists all of the caller's Maxio subscriptions, not only those in the configured family (family filtering was not requested).
9. Environment: only `ServerEnvironment.Us` is used; EU would be via `Maxio:BaseUrl`.
10. New `SubscriptionClaim` entity is added to `CatalogContext` with a string primary key (no migration needed: in-memory only; with SQL Server a migration would be needed and is out of scope per the task).

## 7. Decisions settled at implementation (YOUR CALL items)

- Payment collection: the live sandbox answered 422 "No payment method was on file for the balance" for both plans with automatic collection, despite the plans not requiring a card. `CreateSubscription` therefore sets `PaymentCollectionMethod = CollectionMethod.Remittance` (invoice billing, no card capture); member `SubscriptionService.SubscribeAsync`. Verified live: Pro plan subscription goes `active`.
- Claims are released after every attempt (not retained on success); see DUPLICATE CLAIMS. Stale claims (>2 min) are reclaimed in `TryClaimAsync`.
- Subscribe plan handle is required (400 when missing); no default handle is hard-coded. `SubscribeEndpoint`.
- Total budget: 30 s linked token in `SubscriptionService.Budget`; per-attempt 10 s and 2 retries in `MaxioServiceCollectionExtensions`.
- Error mapping: `SubscriptionService.ToCallerError` (401/403 -> 502, 429 and no answer -> 503, 422 -> 422, other -> 502), surfaced by `ExceptionMiddleware`.
