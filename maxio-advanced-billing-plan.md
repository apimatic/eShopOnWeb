# Maxio Advanced Billing — subscription billing for eShopOnWeb (plan + contract sheet)

SDK root (plugin-relative): `sdk/dotnet/` in the `maxio-advanced-billing` plugin. Map: `sdk-map.md`, `map/operations/*.md`.
All "source" cells below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 0 | Toolchain: `global.json` → `rollForward: latestMajor` (only SDK 10 installed; SDK project needs C# 14). Reference the plugin SDK project from `src/Infrastructure`. | — |
| 1 | Options + client registration in PublicApi: bind `Maxio:` section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`, optional `Environment`), fail-fast validation at startup, register `MaxioAdvancedBillingClient` singleton. | — |
| 2 | Persistence: `SubscriptionEnrollment` aggregate in `CatalogContext` (string PK = claim key), EF config + SQL Server migration. | — |
| 3 | Gateway (`IMaxioBillingGateway` in ApplicationCore, `MaxioBillingGateway` in Infrastructure): plans listing, ensure-customer, create subscription, find-by-reference, list customer subscriptions — the only code touching the SDK; translates SDK exceptions into app-level outcomes. | `ProductFamilies.ListProductsForProductFamily`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Subscriptions.CreateSubscription`, `Subscriptions.FindSubscription`, `Customers.ListCustomerSubscriptions` |
| 4 | `SubscriptionService` (ApplicationCore): claim → ensure customer → create subscription → record; reconciliation of pending claims. | via gateway |
| 5 | PublicApi endpoints (MinimalApi.Endpoint `IEndpoint` convention, JWT): `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`. | via service |
| 6 | Tests: unit tests for service (fake gateway) + gateway tests with stubbed `HttpClient` (unknown-outcome path); PublicApi integration tests with a stub HTTP handler. | — |
| 7 | user-secrets load, run PublicApi detached on assigned ports, live sandbox verification. | all of the above |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (`Requests/Customers/…` → `MaxioAdvancedBilling.Requests.Customers`, `Models/…` → `MaxioAdvancedBilling.Models`, `Models/Enums/…` → `MaxioAdvancedBilling.Models.Enums`, `Errors/…` → `MaxioAdvancedBilling.Errors`, `Core/Exceptions/…` → `MaxioAdvancedBilling.Core.Exceptions`, `Core/ErrorResponse/…` → `MaxioAdvancedBilling.Core.ErrorResponse`).

| Controller | Signature | Request record (members) | Body model (fields) | Response + fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies` | `ListProductsForProductFamily(ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest`: `ProductFamilyId: string, required` (id or `handle:<handle>`), `Page: int = 1` (min 1), `PerPage: int = 20` (max 200), `IncludeArchived: bool?` | — | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` → `.Product` (`Product`, required): `Id int?`, `Name (name) string?`, `Handle (handle) string?`, `Description (description) string?`, `PriceInCents (price_in_cents) long?`, `Interval (interval) int?`, `IntervalUnit (interval_unit) IntervalUnit?`, `ArchivedAt (archived_at) DateTimeOffset?` | A: `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | Not a `Pageable` (map default: none) — manual `Page`/`PerPage` walk | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `Models/ProductResponse.cs`, `Models/Product.cs` |
| `client.Customers` | `ReadCustomerByReference(ReadCustomerByReferenceRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest`: `Reference: string, required` | — | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`customer`, required) → `Id (id) int?`, `Reference (reference) string?`, `Email (email) string?` | B: `ApiException<RawError>` (not-found status: UNVERIFIED — treat 404 as "no customer") | none | `map/operations/Customers.md`; `Requests/Customers/ReadCustomerByReferenceRequest.cs`; `Models/CustomerResponse.cs`, `Models/Customer.cs` |
| `client.Customers` | `CreateCustomer(CreateCustomerOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest`: `Body: CreateCustomerRequest?` (nothing required — Body MUST be set) | `CreateCustomerRequest`: `Customer (customer): CreateCustomer, required` → `CreateCustomer`: `FirstName (first_name) string, required`, `LastName (last_name) string, required`, `Email (email) string, required`, `Reference (reference) string?` — carried: remarks say reference must be unique per site (our idempotency key). Left out: address/phone/vat/locale/tax fields (not collected by eShop). | `CustomerResponse` → `.Customer.Id` | A: `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none | `map/operations/Customers.md`; `Requests/Customers/CreateCustomerOperationRequest.cs`; `Models/CreateCustomerRequest.cs`, `Models/CreateCustomer.cs`; remarks in `Api/Customers.cs` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest`: `Body: CreateSubscriptionRequest?` (nothing required — Body MUST be set) | `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription, required` → `CreateSubscription` (all optional): carried `ProductHandle (product_handle) string?` (remarks: product via `product_id` or `product_handle`), `CustomerId (customer_id) int?` (remarks: customer via `customer_id`/`customer_reference`/`customer_attributes`), `Reference (reference) string?` (app's reference for the subscription), `PaymentCollectionMethod (payment_collection_method) CollectionMethod?` (doc: `remittance`/`automatic`/`prepaid`, legacy `invoice`) — **added after live traffic**: with automatic collection and no payment profile the sandbox refused the create with 422 *"No payment method was on file for the $299.00 balance"*, so the default is `remittance` (`Maxio:PaymentCollectionMethod` overrides). Left out: price point, coupons, payment profile/card/bank attributes (plans don't require a payment method), components, calendar billing, metafields, group. | `MaxioAdvancedBilling.Models.SubscriptionResponse` → `.Subscription` (`subscription`, **nullable**) → `Id (id) int?`, `State (state) SubscriptionState?`, `ProductPriceInCents (product_price_in_cents) long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents) long?`, `NextAssessmentAt (next_assessment_at) DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at) DateTimeOffset?`, `ActivatedAt`, `CreatedAt`, `Currency (currency) string?`, `Reference (reference) string?`, `Product (product) Product?` → `Handle`, `Name`, `ProductFamily (product_family) ProductFamily?` → `Handle` | A: `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] (`Errors (errors) IReadOnlyList<string>, required`) · `TryGetRawError(out RawError)` | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/CreateSubscriptionOperationRequest.cs`; `Models/CreateSubscriptionRequest.cs`, `Models/CreateSubscription.cs`, `Models/SubscriptionResponse.cs`, `Models/Subscription.cs`, `Models/ErrorListResponse1.cs`; remarks in `Api/Subscriptions.cs` |
| `client.Subscriptions` | `FindSubscription(FindSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest`: `Reference: string?` (always set) | — | `SubscriptionResponse` (as above) | A: `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/FindSubscriptionRequest.cs` |
| `client.Subscriptions` | `ReadSubscription(ReadSubscriptionRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` (added during implementation: resolve an Active enrollment by its recorded id) | `MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest`: `SubscriptionId: int, required`, `Include: IReadOnlyList<SubscriptionInclude>?` (unset) | — | `SubscriptionResponse` (as above) | B: `ApiException<RawError>` (404 → null) | none | `map/operations/Subscriptions.md`; `Requests/Subscriptions/ReadSubscriptionRequest.cs` |
| `client.Customers` | `ListCustomerSubscriptions(ListCustomerSubscriptionsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest`: `CustomerId: int, required` | — | `IReadOnlyList<SubscriptionResponse>` (as above) | B: `ApiException<RawError>` | none (single response) | `map/operations/Customers.md`; `Requests/Customers/ListCustomerSubscriptionsRequest.cs` |

**Enums needed**

| Enum (source) | Members used | Read as |
| --- | --- | --- |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`Models/Enums/SubscriptionState.cs`, `OpenStringEnum`) | `Pending` pending · `FailedToCreate` failed_to_create · `Trialing` trialing · `Assessing` assessing · `Active` active · `SoftFailure` soft_failure · `PastDue` past_due · `Suspended` suspended · `Canceled` canceled · `Expired` expired · `Paused` paused · `Unpaid` unpaid · `TrialEnded` trial_ended · `OnHold` on_hold · `AwaitingSignup` awaiting_signup | `.Value` (wire string; open enum may carry unknown values) — `Core/Enum/TypedEnum.cs` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (`Models/Enums/IntervalUnit.cs`) | `Day` day · `Month` month | `.Value` |
| `MaxioAdvancedBilling.Models.Enums.CollectionMethod` (`Models/Enums/CollectionMethod.cs`) | `Automatic` automatic · `Remittance` remittance · `Prepaid` prepaid · `Invoice` invoice | resolved from config with `TryGetKnownValue` |

**Client construction / auth / servers** (`sdk-map.md`):
- `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — only constructor; DI alternative `services.AddMaxioAdvancedBillingClient(Action<MaxioAdvancedBillingClientOptions>?)` (`ServiceCollectionExtensions.cs`).
- Auth: `options.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <Maxio:ApiKey>, Password = "x" }` (map: username is the API key, password is `x`). An unset credential is skipped silently, not thrown.
- `options.Environment`: `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default, `US`) / `.Eu` (`EU`).
- Server groups: `Production` (`https://{site}.chargify.com` US, `https://{site}.ebilling.maxio.com` EU; `{site}` via `options.Server.Production.Us.Site` / `.Eu.Site`; full override via `options.Server.Production.Us.BaseUrl` / `.Eu.BaseUrl`) and `Ebb` (events; not used by this scope). All six operations above are on `Production` (map default).

**CROSS-OPERATION INVARIANTS**

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The plan handle a caller posts to `POST /api/subscriptions` must be one of the products returned for the configured product family (`Maxio:ProductFamilyHandle`), not archived | `CreateSubscription` ← `ListProductsForProductFamily` | `SubscriptionService.SubscribeAsync` checks the requested handle against `IMaxioBillingGateway.GetPlansAsync()` before claiming / calling `CreateSubscription` |
| The `customer_id` passed to `CreateSubscription` must be the customer whose `reference` is this eShop user's reference | `CreateSubscription` ← `ReadCustomerByReference` / `CreateCustomer` | `MaxioBillingGateway.EnsureCustomerAsync` returns the id; only that id is passed by `SubscriptionService` |
| The subscription reference re-read by `FindSubscription` must be the one this app stored on its claim before `CreateSubscription` | `FindSubscription` ← (`CreateSubscription` request, stored on `SubscriptionEnrollment.SubscriptionReference`) | `SubscriptionService` always reads the reference from the persisted claim |
| `ListCustomerSubscriptions` results are shown only when their product belongs to the configured family | `ListCustomerSubscriptions` ← `ListProductsForProductFamily` | `SubscriptionService.GetMySubscriptionsAsync` filters on `Product.ProductFamily.Handle` (fallback: product handle ∈ plan set) |

## 3. Trap notes

- Step 1 — wiring credentials onto the options: which property, and that an unset/blank credential is sent anyway and surfaces only as a 401 later. MUST load `maxio-advanced-billing:dotnet-authentication`
- Step 1 — HttpClient ownership/lifetime when the client is a DI singleton, and what the provided DI extension builds vs. what we need (BaseUrl override, logger). MUST load `maxio-advanced-billing:dotnet-client-initialization`
- Step 1 — base-URL override semantics (template vars, which environment's BaseUrl is used), what `Retry.Timeout` actually bounds, which verbs are retried. MUST load `maxio-advanced-billing:dotnet-configuration-resilience`
- Step 3 — building request records and the `Body` wrapper that nothing marks required; reading nullable response envelopes. MUST load `maxio-advanced-billing:dotnet-calling-endpoints`
- Step 3 — open enums (`SubscriptionState`) carrying values the SDK doesn't declare; nullable/`required` mapping onto our DTOs. MUST load `maxio-advanced-billing:dotnet-models`
- Step 3/4 — which exception types reach the catch (typed vs raw vs transport vs deserialization), and reading the status/body off each. MUST load `maxio-advanced-billing:dotnet-error-handling`
- Step 6 — stubbing the transport, failing a connection deliberately, asserting outgoing requests. MUST load `maxio-advanced-billing:dotnet-testing`

## 4. REQUIRED READING (load all before implementation)

| Skill | Governs |
| --- | --- |
| `maxio-advanced-billing:dotnet-client-initialization` | Step 1 client/DI |
| `maxio-advanced-billing:dotnet-authentication` | Step 1 credentials |
| `maxio-advanced-billing:dotnet-configuration-resilience` | Step 1 retries/timeouts/base URL/logging |
| `maxio-advanced-billing:dotnet-calling-endpoints` | Step 3 calls |
| `maxio-advanced-billing:dotnet-models` | Step 3 payloads/mapping |
| `maxio-advanced-billing:dotnet-error-handling` | Step 3/4 error boundary |
| `maxio-advanced-billing:dotnet-testing` | Step 6 tests |

Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioOptions` bound from `Maxio:` with `ValidateOnStart()`; `ApiKey`, and `Subdomain` unless `BaseUrl` is set, must be non-blank; `ProductFamilyHandle` non-blank; `BaseUrl` (when set) must be an absolute http(s) URI; `Environment` must be `US`/`EU`. Host refuses to start otherwise. Basic auth has one secret part (API key); password is the constant `x`. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets on PublicApi (`Maxio:ApiKey` etc., loaded from `MAXIO_*` env vars); prod: env vars `Maxio__ApiKey` or a vault-backed configuration provider. Options built once when the singleton client is first resolved → rotation needs a process restart (accepted; documented). |
| 3 | Total timeout budget | `Retry.Timeout` = 10 s per attempt (GET: up to 3 attempts + backoff; POST: 1 attempt), `HttpClient.Timeout` = 15 s backstop, and a 25 s whole-call budget per gateway call (`MaxioBillingGateway.CallBudget`, linked `CancellationTokenSource` with the request's `RequestAborted`). Unknown-outcome re-reads get their own 15 s budget. |
| 4 | Write-retry ownership | GETs (`ListProductsForProductFamily`, `ReadCustomerByReference`, `FindSubscription`, `ListCustomerSubscriptions`) may be retried by the SDK. POST writes (`CreateCustomer`, `CreateSubscription`) must NOT be resent by the SDK — restrict `HttpMethodsToRetry` to GET; the app owns write retries via reconciliation. |
| 5 | Idempotency & ambiguous writes | No request record carries a key member (`CreateCustomerOperationRequest`/`CreateSubscriptionOperationRequest` have only `Body`); the injected `Idempotency-Key` header is not a key. `CreateCustomer`: real dedupe = customer `reference` (remarks: unique per site) → reconcile via `ReadCustomerByReference`. `CreateSubscription`: subscription `reference` (app-generated GUID stored on the claim before the call) → reconcile via `FindSubscription`; uniqueness of subscription reference at Maxio is UNVERIFIED, so the local claim is the authoritative duplicate guard. |
| 6 | Observability | Structured `ILogger` logs: Information for customer created / subscription created (ids, reference, user key — no email); Warning for 4xx rejections (status + Maxio `errors` list), unknown outcomes and reconciliations; Error for 5xx/transport. Maxio error bodies carry no correlation id in the map shapes, so we log our own request `CorrelationId` (BaseMessage) + subscription reference + HTTP status. |
| 7 | Sensitive data | Scope carries PII (`first_name`, `last_name`, `email` on CreateCustomer) and the API key in the Authorization header. `LogRequestBody` off (default), `LoggerFactory` assigned explicitly from DI, request/response headers not logged. App logs never echo request bodies or emails. |
| 8 | Environment selection | Only the `Production` server group is used (`Ebb` untouched). Each deployment sets `Maxio:Subdomain` (site) + optional `Maxio:Environment` (`US` default / `EU`), or `Maxio:BaseUrl` verbatim override (applied to the selected environment's `Production` BaseUrl). The SDK declares no sandbox environment: test traffic is kept off live by pointing dev at the sandbox site subdomain via user-secrets; nothing is hard-coded. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `POST /api/subscriptions` → `CreateSubscription` (and the `CreateCustomer` it may trigger) | `SubscriptionEnrollments` table in `CatalogContext` (string primary key `ClaimKey` = user reference + `|` + product family handle) inserted with state `Pending` before any Maxio write | Primary-key violation on insert (`DbUpdateException` on SQL Server; in-memory provider's duplicate-key exception) | `EfSubscriptionEnrollmentStore.TryClaimAsync` `catch (Exception ex) when (IsDuplicateKey(ex))` (in-memory provider raises `ArgumentException` — observed live; SQL Server `DbUpdateException`) → returns the existing claim; `SubscriptionService.ResolveExistingAsync` then returns the existing subscription (same plan), 409 (other plan / in progress) or settles/takes over a stale pending claim | `EfSubscriptionEnrollmentStore.TryClaimAsync` → `MaxioBillingGateway.CreateSubscriptionAsync` (`client.Subscriptions.CreateSubscription`) |
| `CreateCustomer` | Same claim row (the customer write only happens inside the claimed subscribe flow) + Maxio's unique customer `reference` beside it | The same PK claim; Maxio 422 on a duplicate reference as backstop | `EfSubscriptionEnrollmentStore.TryClaimAsync` duplicate-key catch (as above); backstop `MaxioBillingGateway.EnsureCustomerAsync` `catch (ApiException<CreateCustomerError>)` → `TryFindCustomerQuietlyAsync` by reference | `EfSubscriptionEnrollmentStore.TryClaimAsync` → `MaxioBillingGateway.EnsureCustomerAsync` (`client.Customers.CreateCustomer`) |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (plans) | `PerPage = 200`, max 10 pages | `PlanCatalog.IsTruncated` → `ListSubscriptionPlansResponse.IsTruncated` | `MaxioBillingGateway.LoadPlansAsync` (sets `PlanCatalog.IsTruncated`), surfaced by `SubscriptionPlanListEndpoint.HandleAsync` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateSubscription` | `FindSubscription` | `SubscriptionEnrollment.SubscriptionReference` (app GUID sent as `reference`) | `MaxioBillingGateway.CreateSubscriptionAsync` catches (`SdkConnectionException`/`SdkTimeoutException`, own-budget `OperationCanceledException`, 5xx/408 `ApiException<CreateSubscriptionError>`, unreadable 2xx/5xx `ResponseDeserializationException`) → `SettleSubscriptionAsync` → `TryFindSubscriptionQuietlyAsync`; if still not found → `SubscriptionCreationResult.Unknown()`, claim stays `Pending` (HTTP 202) and `SubscriptionService.TrySettleAsync` settles it on the next POST (`ResolveExistingAsync`) or GET (`GetMySubscriptionsAsync`) | `MaxioBillingGatewayTests.CreateSubscription_ConnectionFails_SettlesViaFindSubscription`, `MaxioBillingGatewayTests.CreateSubscription_ConnectionFailsAndNotFound_ReturnsUnknownOutcome` |
| `CreateCustomer` | `ReadCustomerByReference` | customer `reference` = eShop user reference | `MaxioBillingGateway.EnsureCustomerAsync` catches → `SettleCustomerAsync` → `TryFindCustomerQuietlyAsync` (`ReadCustomerByReference`); still not found → `BillingProviderException(Unavailable)` and the claim is released (customer create is idempotent by reference, so the retry re-reads first) | `MaxioBillingGatewayTests.CreateCustomer_ConnectionFails_SettlesViaReadByReference` |

## 6. Assumptions & Blockers

- Assumption: one subscription per eShop user per product family (re-subscribing to a different plan = plan change, out of scope → 409). Same-plan repeat POST returns the existing subscription (idempotent).
- Assumption: Maxio customer `reference` = `eshop-user-` + first 16 bytes (hex) of SHA-256 of the normalized eShop username — deterministic (the JWT carries only `ClaimTypes.Name`; usernames are unique and immutable here and survive in-memory restarts, unlike the seeded Identity GUID) yet opaque, so no email lands in Maxio references, URLs or logs. (Changed during implementation from the raw username after the email showed up in logs.)
- Decision (implementation): before creating, the subscribe flow lists the customer's live subscriptions in the family and adopts one if present — the guard against duplicates when local state was lost (in-memory store restart). The local primary-key claim remains the concurrency guard.
- Assumption: customer first/last name are not collected by eShop; derived from the email local part (Maxio requires both).
- UNVERIFIED: `ReadCustomerByReference` returns 404 when no customer matches → handled as "not found"; any other status is an error.
- No blockers: every capability needed is in the map.

## 7. Source labels

Used inline above: map page / map-named file; `UNVERIFIED` where only live traffic can settle it.
