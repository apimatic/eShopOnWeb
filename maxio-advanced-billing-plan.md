# Maxio Advanced Billing — subscription billing for eShopOnWeb (PublicApi)

SDK source (read-only, referenced by ProjectReference): `<plugin-root>/sdk/dotnet/` (`maxio-advanced-billing` plugin).
Map pages cited below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Settings (`Maxio:` section) + startup validation + DI-registered singleton client over a named `HttpClient` | — |
| 2 | `GET /api/subscription-plans` — list the plans of the configured product family | `ProductFamilies.ListProductsForProductFamily` |
| 3 | Claim store: `SubscriptionEnrollment` row in `CatalogContext`, PK = buyer id (JWT `Name`), + EF migration | — |
| 4 | `POST /api/subscriptions` — validate plan → claim → ensure customer → adopt-or-create subscription → record | `ListProductsForProductFamily`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription` (reconcile) |
| 5 | `GET /api/my-subscriptions` — the caller's subscriptions, Maxio as system of record | `ReadCustomerByReference`, `ListCustomerSubscriptions` |
| 6 | Error boundary → ProblemDetails; unit tests with stub `HttpMessageHandler`; live sandbox verification | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names; never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its own source path implies (taken from the path the map gives for THAT type), never from where a neighbouring type sits.

| Controller | Signature | Request record (members) | Body model (fields) | Response + fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` | `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (id **or** `handle:<handle>`), `Page: int = 1` `[Minimum(1)]`, `PerPage: int = 20` `[Maximum(200)]`, `IncludeArchived: bool?` (others unused) | — | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>`; `ProductResponse.Product (product): Product, required` → `Product.Id (id) int?`, `Name (name) string?`, `Handle (handle) string?`, `Description (description) string?`, `PriceInCents (price_in_cents) long?`, `Interval (interval) int?`, `IntervalUnit (interval_unit) IntervalUnit?`, `ArchivedAt (archived_at) DateTimeOffset?`, `RequireCreditCard (require_credit_card) bool?` | A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none per map; manual `page`/`per_page` query params (plain list) | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`, `Models/Product.cs` |
| `client.Customers` | `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | — | `MaxioAdvancedBilling.Models.CustomerResponse`: `Customer (customer): Customer, required` → `Id (id) int?`, `Reference (reference) string?`, `Email (email) string?` | B: `ApiException<RawError>`; 404 = no customer with that reference (UNVERIFIED — map gives no typed 404; treat 404 as absent, any other status as failure) | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`, `Models/Customer.cs`; remarks `Api/Customers.cs` ("returns a single match") |
| `client.Customers` | `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` (nothing required — Body must be set) | `MaxioAdvancedBilling.Models.CreateCustomerRequest`: `Customer (customer): CreateCustomer, required` → `CreateCustomer`: `FirstName (first_name) string, required`, `LastName (last_name) string, required`, `Email (email) string, required`, `Reference (reference) string?` (carried: remarks make it the uniqueness key). Left out: address/phone/vat/locale/tax fields. | `CustomerResponse` → `Customer.Id` | A: `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)`; `CustomerErrorResponse1.Errors (errors): MaxioAdvancedBilling.Models.AnyOf.Errors1?` → `TryGetCustomerError(out CustomerError)` (`CustomerError.Customer (customer) string?`) / `TryGetListOfString(out IReadOnlyList<string>)` | none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`, `Models/CustomerErrorResponse1.cs`, `Models/AnyOf/Errors1.cs`, `Models/CustomerError.cs`; remarks `Api/Customers.cs` (one customer per reference) |
| `client.Customers` | `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | — | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>`; `SubscriptionResponse.Subscription (subscription): Subscription?` (NOT required) → see Subscription fields below | B: `ApiException<RawError>` | none (returns all; remarks: "Lists all subscriptions that belong to a customer") | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs`; `Models/SubscriptionResponse.cs` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` | `MaxioAdvancedBilling.Models.CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required` → `CreateSubscription`: `ProductHandle (product_handle) string?` (remarks: product_id **or** product_handle), `CustomerId (customer_id) int?` (remarks: customer_id **or** customer_reference **or** customer_attributes), `Reference (reference) string?` (our reconcile key), `PaymentCollectionMethod (payment_collection_method) MaxioAdvancedBilling.Models.Enums.CollectionMethod?` (`remittance` default from `Maxio:PaymentCollectionMethod` — live sandbox 422'd "No payment method was on file" without it; `Models/Enums/CollectionMethod.cs`: `automatic`, `remittance`, `prepaid`, `invoice`). Left out: price point, coupons, payment profile/card/bank (plans do not require a payment method), components, metafields, dates. | `SubscriptionResponse.Subscription?` → `Id (id) int?`, `State (state) SubscriptionState?`, `Reference (reference) string?`, `ProductPriceInCents (product_price_in_cents) long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents) long?`, `NextAssessmentAt (next_assessment_at) DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at) DateTimeOffset?`, `ActivatedAt (activated_at) DateTimeOffset?`, `CreatedAt (created_at) DateTimeOffset?`, `Currency (currency) string?`, `Product (product) Product?`, `Customer (customer) Customer?` | A: `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)`; `ErrorListResponse1.Errors (errors): IReadOnlyList<string>, required` | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/SubscriptionResponse.cs`, `Models/Subscription.cs`, `Models/ErrorListResponse1.cs`; remarks `Api/Subscriptions.cs` |
| `client.Subscriptions` | `FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string?` (set it) | — | `SubscriptionResponse` (as above) | A: `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs`; remarks "Finds a subscription by its reference." |

**CROSS-OPERATION INVARIANTS**

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The plan handle a caller POSTs must be one the configured product family currently offers (non-archived) | `CreateSubscription` ← `ListProductsForProductFamily` (`ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`) | `SubscriptionService.SubscribeAsync`, via `MaxioBillingGateway.GetPlansAsync`, before any claim or write; unknown handle → 400 |
| A subscription adopted for the caller must belong to the customer found/created for the caller's reference | `CreateSubscription`/adopt ← `ReadCustomerByReference`/`CreateCustomer` | `SubscriptionService` passes only the resolved `Customer.Id` to `ListCustomerSubscriptions` / `CreateSubscription` |
| A reconciled subscription (FindSubscription by reference) must belong to the caller's customer | `FindSubscription` ← stored claim's `MaxioCustomerId` | `SubscriptionService.ReconcileAsync` compares `Subscription.Customer.Id` with the claim's customer id |

**Enums used** — `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`Models/Enums/SubscriptionState.cs`, open string enum; read `.Value`): `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup`. Live (adoptable) = `active, trialing, assessing, pending, past_due, soft_failure, unpaid, awaiting_signup, paused`. End-of-life = `canceled, expired, failed_to_create, on_hold, suspended, trial_ended` (classification from the enum's own XML docs). `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (`day`, `month`) — read `.Value`.

**Client construction / auth / servers** (`sdk-map.md`):
- `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)` — only constructor.
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (map: password is `x`).
- `options.Environment = MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default) / `.Eu`. Group `Production` (`https://{site}.chargify.com` US, `https://{site}.ebilling.maxio.com` EU): set `options.Server.Production.Us.Site` (or `.Eu.Site`) = `Maxio:Subdomain`; when `Maxio:BaseUrl` is set → `options.Server.Production.<Env>.BaseUrl = Maxio:BaseUrl` verbatim. Group `Ebb` not touched.
- `options.Retry`: `MaxioAdvancedBilling.Core.Configuration.RetryOptions.Default() with { … }`; `options.Logging`: `MaxioAdvancedBilling.Core.Configuration.LoggingOptions`.
- Exceptions: `MaxioAdvancedBilling.Core.Exceptions` (`ApiException<T>`, `ResponseDeserializationException`, `SdkConnectionException`, `SdkTimeoutException`, `AuthSchemeException`); `RawError` in `MaxioAdvancedBilling.Core.ErrorResponse`.

## 3. Trap notes

- Step 1 — what each of the two "Timeout" knobs bounds versus what bounds a whole call, and which verbs the retry policy resends. **MUST load `maxio-advanced-billing:dotnet-configuration-resilience`**
- Step 1 — the DI extension's HttpClient lifetime / DNS staleness and sharing the unnamed default client. **MUST load `maxio-advanced-billing:dotnet-client-initialization`**
- Step 1 — an unset credential sends unauthenticated calls silently; both Basic halves matter. **MUST load `maxio-advanced-billing:dotnet-authentication`**
- Step 1 — the env-var logging switch and verbatim JSON-body logging (CreateCustomer carries name/email). **MUST load `maxio-advanced-billing:dotnet-configuration-resilience`**
- Step 2 — a list call with manual page/per_page params and no provider-independent stop condition. **MUST load `maxio-advanced-billing:dotnet-configuration-resilience`**
- Steps 2/4/5 — open enums: what `ToString()` yields vs the wire value; nullable nested members. **MUST load `maxio-advanced-billing:dotnet-models`**
- Step 4 — `Errors1` is an AnyOf union; reading it wrong throws or drops the message. **MUST load `maxio-advanced-billing:dotnet-models`**
- Step 4 — a lookup that gates a create: an unreadable body must not be treated as "absent". **MUST load `maxio-advanced-billing:dotnet-error-handling`**
- Step 4 — a POST that fails in transport has an unknown outcome. **MUST load `maxio-advanced-billing:dotnet-configuration-resilience`**
- Steps 2/4/5 — Case A vs Case B catch ladder order; positional `CancellationToken` vs `RequestOptions?`. **MUST load `maxio-advanced-billing:dotnet-error-handling`**, **`maxio-advanced-billing:dotnet-calling-endpoints`**
- Step 6 — request bodies are disposed after send; `ApiException<T>` cannot be constructed directly. **MUST load `maxio-advanced-billing:dotnet-testing`**

## 4. REQUIRED READING (loaded before implementation)

- `maxio-advanced-billing:dotnet-client-initialization` — step 1
- `maxio-advanced-billing:dotnet-authentication` — step 1
- `maxio-advanced-billing:dotnet-configuration-resilience` — steps 1, 2, 4
- `maxio-advanced-billing:dotnet-calling-endpoints` — steps 2, 4, 5
- `maxio-advanced-billing:dotnet-models` — steps 2, 4, 5
- `maxio-advanced-billing:dotnet-error-handling` — steps 2, 4, 5, 6
- `maxio-advanced-billing:dotnet-testing` — step 6
- Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from `Maxio:` with `ValidateOnStart()`; custom validator rejects blank `ApiKey`, blank `ProductFamilyHandle`, and blank `Subdomain` unless `BaseUrl` is set; a set `BaseUrl` must be an absolute http(s) URI. Messages name the key, never the value. Password half is the constant `x` (map). |
| 2 | Secret sourcing & rotation | `Maxio:ApiKey` from .NET user-secrets (dev) / environment `Maxio__ApiKey` or a secret store (prod). Options built once in the singleton factory; rotation takes effect on process restart (documented). |
| 3 | Total timeout budget | Per attempt: `Retry.Timeout = Maxio:AttemptTimeoutSeconds` (default 10s) and `HttpClient.Timeout` 15s backstop. Whole SDK call: `Maxio:CallBudgetSeconds` (default 20s) via a linked CTS in `MaxioBillingGateway.Bounded` (linked to `HttpContext.RequestAborted`). POST /api/subscriptions makes ≤ 5 calls → worst case ≈ 100s; GETs ≤ 2 calls → ≈ 40s. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` (GET/HEAD/PUT/OPTIONS) kept: the reads retry, `CreateCustomer`/`CreateSubscription` (POST) are never resent by the SDK. Never widened. A unit test asserts one POST on a transport fault. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: real key = body `Customer.Reference` (remarks: one customer per reference) → reconcile with `ReadCustomerByReference`. `CreateSubscription`: no provider-enforced key in the map; we send body `Subscription.Reference` = per-claim `eshop-sub-<attemptId>` and reconcile with `FindSubscription(Reference)`. The injected `Idempotency-Key` header is not a key. |
| 6 | Observability | SDK built-in logger with the host `ILoggerFactory` (request line Info, failures Warning/Error, retries Warning); our gateway logs operation + status + Maxio error messages at Warning, unknown outcomes at Error; no correlation id exists in Maxio error bodies per the map models (`ErrorListResponse1`, `CustomerErrorResponse1` carry only `errors`), so the correlation key in our logs is the subscription reference (`eshop-sub-…`) and the operation name. |
| 7 | Sensitive data | Yes — CreateCustomer carries name + email. `LogRequestBody = false`, headers off, `LoggerFactory` assigned explicitly (disables `MAXIOADVANCEDBILLINGCLIENT_LOG`). Our own logs never print request bodies or the API key. |
| 8 | Environment selection | Groups: `Production` (touched), `Ebb` (not touched). Environments `Us`/`Eu`; `Maxio:Environment` optional (default `US`). Dev/test → the sandbox site named by `Maxio:Subdomain` (`cp-exp-4` via user-secrets) — the SDK has no sandbox environment; separation is by site subdomain/API key; `Maxio:BaseUrl` overrides verbatim (mock server/proxy). |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | `ListProductsForProductFamily` walked with `PerPage = 200`, cap 5 pages → `SubscriptionPlansResult.IsTruncated` → `ListSubscriptionPlansResponse.IsTruncated`. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateSubscription` (POST /api/subscriptions, per buyer) | `SubscriptionEnrollments` table in `CatalogContext` (PK `BuyerId`), inserted with state `Pending` before any Maxio write | Primary-key violation on insert of a second row for the same `BuyerId` (stale-lease takeover guarded by the `LeaseId` concurrency token) | `catch (Exception ex) when (IsDuplicateKey(ex))` in `EfSubscriptionEnrollmentStore.TryClaimAsync` (relational: `DbUpdateException`; in-memory provider: `ArgumentException` "same key" — verified by `ClaimTests.SecondClaimForSameBuyerIsRefusedAndReturnsTheWinner`) → existing row to `SubscriptionService.ResolveExistingClaimAsync` (Active → 200 same subscription; fresh Pending → 409; stale Pending/Unknown → `TryTakeOverAsync` + `ReconcileAsync`) | `EfSubscriptionEnrollmentStore.TryClaimAsync` → `MaxioBillingGateway.CreateSubscriptionAsync` (both called from `SubscriptionService.SubscribeAsync` → `EnrollAsync`) |
| `CreateCustomer` (same request, per buyer) | Same `SubscriptionEnrollments` row (the per-buyer claim precedes the customer write) + Maxio reference uniqueness beside it | Same PK violation | Same catch | `EfSubscriptionEnrollmentStore.TryClaimAsync` → `MaxioBillingGateway.CreateCustomerAsync` (called from `SubscriptionService.EnsureCustomerAsync`) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` | 5 pages × 200 (`MaxioBillingGateway.MaxPlanPages`) | `BillingPlanCatalog.IsTruncated` → `ListSubscriptionPlansResponse.IsTruncated`; subscribe on a truncated catalog that misses the handle → 503, not 400 | `MaxioBillingGateway.GetPlansAsync` (sets `truncated`), surfaced by `SubscriptionPlanListEndpoint.HandleAsync`; test `MaxioBillingGatewayTests.GetPlans_StopsAtPageCap_AndReportsTruncation` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `Customers.ReadCustomerByReference` | `Customer.Reference` = `eshop-<buyerId>` | `catch (MaxioAmbiguousOutcomeException)` in `MaxioBillingGateway.CreateCustomerAsync` (raised by `Bounded` for `SdkConnectionException`/`SdkTimeoutException`, budget expiry, unreadable 2xx) → `FindCustomerByReferenceAsync`; still unknown → `BillingOutcomeUnknownException` → `SubscriptionService.SubscribeAsync` marks the claim `OutcomeUnknown` (HTTP 202); settled on the next POST by `ResolveExistingClaimAsync` → `EnsureCustomerAsync` (re-reads by reference) | `MaxioBillingGatewayTests.CreateCustomer_TransportFailure_SettlesByReference` |
| `CreateSubscription` | `Subscriptions.FindSubscription` | `Subscription.Reference` = `eshop-sub-<guid>` (stored on the claim before the call) | `catch (MaxioAmbiguousOutcomeException)` in `MaxioBillingGateway.CreateSubscriptionAsync` → `SettleSubscriptionAsync` → `FindSubscriptionByReferenceAsync`; still unknown → claim `OutcomeUnknown`, settled by `SubscriptionService.ReconcileAsync` (next POST) or `GetMySubscriptionsAsync` (matches the reference in the customer's list) | `MaxioBillingGatewayTests.CreateSubscription_TransportFailure_SettlesByReference`, `…_TransportFailureAndLookupFailure_IsOutcomeUnknown`; `Subscribe.UnknownClaim_IsTakenOverAndSettledByReference` |

## 6. Assumptions & Blockers

- Assumption: one eShop subscription per buyer (the hero flow); a second POST for the same plan returns the existing subscription, for a different plan → 409. Plan changes/cancel are out of scope.
- Assumption: buyer identity = JWT `ClaimTypes.Name` (the only identity claim `IdentityTokenClaimService` issues; eShop uses user name as `BuyerId`). Customer reference = `eshop-<userName>`; email from `UserManager` (falls back to user name).
- UNVERIFIED: Maxio enforces uniqueness of subscription `reference` — not relied on; the local claim is the guard.
- UNVERIFIED: `ReadCustomerByReference` signals "no such customer" with 404 — treat only 404 as absent.
- No blockers.

## 8. Implementation notes (post-coding)

- Live sandbox: `CreateSubscription` without `payment_collection_method` → 422 "No payment method was on file for the $299.00 balance" even though the plan does not require a card. Fixed by sending `remittance` (configurable `Maxio:PaymentCollectionMethod`, validated at startup against `CollectionMethod`).
- The SDK brings `System.Text.Json`/`Microsoft.Extensions.*` 10.0.x into the net8.0 graph. Verified on a real ASP.NET Core 8 runtime (all 105 tests pass). The `PipeWriter.UnflushedBytes` 500s in `PublicApiIntegrationTests` happen only when the net8 `TestHost` 8.0.2 package runs on a rolled-forward 9/10 runtime (pre-existing; affects the untouched catalog tests equally).

## 7. Source labels

Used inline above: map page / map-named file; `UNVERIFIED`; `YOUR CALL — not in the map` (rows in §5/§6 that are application design).
