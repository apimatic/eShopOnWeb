# Maxio Advanced Billing — subscription billing for eShopOnWeb (plan + contract sheet)

SDK root (plugin-relative): `sdk/dotnet/` — map `sdk/dotnet/sdk-map.md`, pages `sdk/dotnet/map/operations/`.
All `src/MaxioAdvancedBilling/...` paths below are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 0 | `global.json` roll-forward; SDK `ProjectReference` from `Infrastructure`; baseline build/test | — |
| 1 | `Maxio:` settings type + fail-fast validation; client registered once over a named `HttpClient` | — |
| 2 | Local claim store (EF `CatalogContext`): `BillingCustomer` (PK = buyer id) and `SubscriptionEnrollment` (PK = claim key) + migration | — |
| 3 | Gateway (Infrastructure) — the single SDK boundary: bounded calls, error translation, unknown-outcome settlement | all below |
| 4 | Plans: list the configured family's products, page-walked, cached | `ProductFamilies.ListProductsForProductFamily` |
| 5 | Ensure customer (claim → adopt-by-reference → create → record) | `Customers.ReadCustomerByReference`, `Customers.CreateCustomer` |
| 6 | Subscribe (validate plan ∈ step-4 set → claim → adopt-by-reference → create → record / settle) | `Subscriptions.FindSubscription`, `Subscriptions.CreateSubscription`, `Subscriptions.ReadSubscription` |
| 7 | My subscriptions (settle unknown claims, then list the customer's subscriptions) | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions`, `Subscriptions.FindSubscription` |
| 8 | Background sweep that settles `Unknown`/stale `Pending` claims | `Subscriptions.FindSubscription` |
| 9 | PublicApi endpoints `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` (JWT) | — |
| 10 | Tests (unit: service + gateway over stub `HttpClient`; integration: claim store; PublicApi: endpoints w/ stubbed Maxio) | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type, never from where a neighbouring type sits.

| Controller · method | Request record + members | Body model + fields (wire) | Response envelope → fields read | Error case | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.ProductFamilies.ListProductsForProductFamily(MaxioAdvancedBilling.Requests.ProductFamilies.ListProductsForProductFamilyRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `ProductFamilyId: string, required` ("id or `handle:`-prefixed handle"); `Page: int = 1` ([Minimum(1)]); `PerPage: int = 20` ([Maximum(200)]); `IncludeArchived: bool?` (left unset → archived excluded) | — | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` → `.Product` (`required MaxioAdvancedBilling.Models.Product`) → `Id int?`, `Name string?`, `Handle string?`, `Description string?`, `PriceInCents long?`, `Interval int?`, `IntervalUnit MaxioAdvancedBilling.Models.Enums.IntervalUnit?`, `TrialPriceInCents long?`, `TrialInterval int?`, `InitialChargeInCents long?`, `RequireCreditCard bool?`, `ArchivedAt DateTimeOffset?` | **A** `ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError>`: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] | none at SDK level (single response); caller drives `Page`/`PerPage` | `map/operations/ProductFamilies.md`; `src/MaxioAdvancedBilling/Requests/ProductFamilies/ListProductsForProductFamilyRequest.cs`; `src/MaxioAdvancedBilling/Models/Product.cs` |
| `client.Customers.ReadCustomerByReference(MaxioAdvancedBilling.Requests.Customers.ReadCustomerByReferenceRequest request, …)` | `Reference: string, required` (query `reference`) | — | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`required MaxioAdvancedBilling.Models.Customer`) → `Id int?`, `Reference string?`, `Email string?` | **B** `ApiException<RawError>` (404 = not found) | none | `map/operations/Customers.md`; `Api/Customers.cs` remarks ("single match") |
| `client.Customers.CreateCustomer(MaxioAdvancedBilling.Requests.Customers.CreateCustomerOperationRequest request, …)` | `Body: MaxioAdvancedBilling.Models.CreateCustomerRequest?` | `CreateCustomerRequest.Customer (customer): MaxioAdvancedBilling.Models.CreateCustomer, required` → `FirstName (first_name): string, required`; `LastName (last_name): string, required`; `Email (email): string, required`; `Reference (reference): string?` — **carried**: remarks say one customer per `reference` value. Doc-named fields left out: address/state/country/locale/vat/entity identifiers/tax (not in scope) | `CustomerResponse.Customer.Id int?` | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError>`: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422] · `TryGetRawError` [fallback]. `CustomerErrorResponse1.Errors (errors): MaxioAdvancedBilling.Models.AnyOf.Errors1?` → `TryGetCustomerError(out MaxioAdvancedBilling.Models.CustomerError)` (`.Customer string?`) / `TryGetListOfString(out IReadOnlyList<string>)` | none | `map/operations/Customers.md`; `Models/CreateCustomer.cs`; `Errors/CreateCustomerError.cs`; `Models/AnyOf/Errors1.cs`; `Models/CustomerError.cs` |
| `client.Customers.ListCustomerSubscriptions(MaxioAdvancedBilling.Requests.Customers.ListCustomerSubscriptionsRequest request, …)` | `CustomerId: int, required` | — | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` → `.Subscription` (`MaxioAdvancedBilling.Models.Subscription?`, nullable!) | **B** | none | `map/operations/Customers.md` |
| `client.Subscriptions.CreateSubscription(MaxioAdvancedBilling.Requests.Subscriptions.CreateSubscriptionOperationRequest request, …)` | `Body: MaxioAdvancedBilling.Models.CreateSubscriptionRequest?` | `CreateSubscriptionRequest.Subscription (subscription): MaxioAdvancedBilling.Models.CreateSubscription, required` → nothing `required`; carried per remarks/member docs: `ProductHandle (product_handle): string?` ("Required, unless a product_id is given"); `CustomerId (customer_id): int?` ("Required, unless a customer_reference or customer_attributes"); `Reference (reference): string?` (our app's reference for the subscription — reconciliation key). Left out: `product_id`, price points, coupons, `payment_collection_method`, payment profile/card/bank attributes, components, `next_billing_at`, metafields, etc. | `SubscriptionResponse.Subscription?` (see Subscription fields below) | **A** `ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>`: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422] (`Errors (errors): IReadOnlyList<string>, required`) · `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `Models/CreateSubscription.cs`; `Errors/CreateSubscriptionError.cs`; `Models/ErrorListResponse1.cs`; `Api/Subscriptions.cs` remarks |
| `client.Subscriptions.FindSubscription(MaxioAdvancedBilling.Requests.Subscriptions.FindSubscriptionRequest request, …)` | `Reference: string?` (query `reference`) | — | `SubscriptionResponse.Subscription?` | **A** `ApiException<MaxioAdvancedBilling.Errors.FindSubscriptionError>`: `TryGetNoContent(out RawError)` [404] · `TryGetRawError` [fallback] | none | `map/operations/Subscriptions.md`; `Errors/FindSubscriptionError.cs` |
| `client.Subscriptions.ReadSubscription(MaxioAdvancedBilling.Requests.Subscriptions.ReadSubscriptionRequest request, …)` | `SubscriptionId: int, required` (`Include` left unset) | — | `SubscriptionResponse.Subscription?` | **B** | none | `map/operations/Subscriptions.md` |

`MaxioAdvancedBilling.Models.Subscription` fields read (`src/MaxioAdvancedBilling/Models/Subscription.cs`): `Id (id) int?`, `State (state) MaxioAdvancedBilling.Models.Enums.SubscriptionState?`, `Reference (reference) string?`, `ProductPriceInCents (product_price_in_cents) long?`, `CurrentBillingAmountInCents (current_billing_amount_in_cents) long?`, `Currency (currency) string?`, `CurrentPeriodStartedAt / CurrentPeriodEndsAt DateTimeOffset?`, `NextAssessmentAt (next_assessment_at) DateTimeOffset?` (next billing attempt), `ActivatedAt`, `CreatedAt`, `ExpiresAt DateTimeOffset?`, `CancelAtEndOfPeriod bool?`, `Customer (customer) Customer?`, `Product (product) Product?` (→ `Handle`, `Name`, `Interval`, `IntervalUnit`, `ProductFamily?`).

**Enums** (open string enums; read with `.Value`, never `ToString()`):

| Enum (file) | Members used |
| --- | --- |
| `MaxioAdvancedBilling.Models.Enums.SubscriptionState` (`Models/Enums/SubscriptionState.cs`) | `pending, failed_to_create, trialing, assessing, active, soft_failure, past_due, suspended, canceled, expired, paused, unpaid, trial_ended, on_hold, awaiting_signup` — terminal for us: `canceled`, `expired`, `failed_to_create` |
| `MaxioAdvancedBilling.Models.Enums.IntervalUnit` (`Models/Enums/IntervalUnit.cs`) | `day`, `month` |

**Client / auth / servers** (`sdk-map.md`): ctor `new MaxioAdvancedBilling.MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)`. `BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = <API key>, Password = "x" }` (provider note: password is `x`). `Environment: MaxioAdvancedBilling.Servers.ServerEnvironment` — `Us` (default, value `US`) / `Eu` (`EU`). Server group in scope: **`Production`** only (`Ebb` untouched): `options.Server.Production.Us|Eu.Site` (template `{site}`, default `"subdomain"`) and `.BaseUrl` (`https://{site}.chargify.com` / `https://{site}.ebilling.maxio.com`). `options.Retry: MaxioAdvancedBilling.Core.Configuration.RetryOptions` (`Default() with {…}`); `options.Logging: LoggingOptions`. Exceptions: `MaxioAdvancedBilling.Core.Exceptions.{ApiException<T>, ApiException, ResponseDeserializationException, SdkConnectionException, SdkTimeoutException, AuthSchemeException}`; `RawError`/`ApiError` in `MaxioAdvancedBilling.Core.ErrorResponse`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The `planHandle` a caller POSTs (→ `CreateSubscription.ProductHandle`) must be one of the products the configured family offers | `CreateSubscription` ← `ListProductsForProductFamily` (`ProductFamilyId = "handle:" + Maxio:ProductFamilyHandle`, archived excluded) | `SubscriptionService.SubscribeAsync`, before the claim and before `CreateSubscription` (via cached `ListPlansAsync`) |
| The `CustomerId` sent to `CreateSubscription` must be the customer this app created/adopted for the caller (by our deterministic customer reference) | `CreateSubscription` ← `CreateCustomer` / `ReadCustomerByReference` (stored on `BillingCustomer.MaxioCustomerId`) | `SubscriptionService.EnsureCustomerAsync`, before `CreateSubscription` |
| The subscription id read/listed for "my subscriptions" must belong to the caller's customer | `ReadSubscription` / `ListCustomerSubscriptions` ← stored `SubscriptionEnrollment.MaxioSubscriptionId` / `BillingCustomer.MaxioCustomerId` | `SubscriptionService.GetMySubscriptionsAsync` (only ids from the caller's own claim rows / own customer) |

## 3. Trap notes

- Step 1 — what each timeout knob bounds, and which one is a whole-call budget → MUST load `maxio-advanced-billing:dotnet-configuration-resilience`.
- Step 1 — singleton client over a factory `HttpClient`: DNS staleness and whether the default unnamed client is shared → MUST load `maxio-advanced-billing:dotnet-client-initialization`.
- Step 1 — an unset/blank credential is sent unauthenticated, not rejected → MUST load `maxio-advanced-billing:dotnet-authentication`.
- Step 1 — `Environment` vs `Server` read at different times; setting `BaseUrl` on the wrong environment is ignored → MUST load `maxio-advanced-billing:dotnet-configuration-resilience`.
- Step 1 — logging under a hand-built client and the env-var that can switch body logging on (customer PII in bodies) → MUST load `maxio-advanced-billing:dotnet-configuration-resilience`.
- Steps 4–7 — request records vs body models, `requestOptions` positional slot → MUST load `maxio-advanced-billing:dotnet-calling-endpoints`.
- Step 4 — a caller-driven page loop must have a provider-independent bound and must surface truncation → MUST load `maxio-advanced-billing:dotnet-configuration-resilience`.
- Step 4/7 — enum to string conversion for the API response → MUST load `maxio-advanced-billing:dotnet-models`.
- Step 5 — the `errors` member is an `AnyOf` union; reading it wrong drops the message → MUST load `maxio-advanced-billing:dotnet-models`.
- Steps 4–8 — which exception reaches the catch per case (A vs B), ordering of `TryGet…` arms → MUST load `maxio-advanced-billing:dotnet-error-handling`.
- Steps 5–6 — whether POSTs are resent by the SDK, and what a transport failure on a write means → MUST load `maxio-advanced-billing:dotnet-configuration-resilience`.
- Step 10 — stubbing the `HttpClient`, reading bodies after the call, faking connection failures → MUST load `maxio-advanced-billing:dotnet-testing`.

## 4. REQUIRED READING (all loaded before implementation)

- `maxio-advanced-billing:dotnet-client-initialization` — step 1
- `maxio-advanced-billing:dotnet-authentication` — step 1
- `maxio-advanced-billing:dotnet-configuration-resilience` — steps 1, 4, 5, 6, 8
- `maxio-advanced-billing:dotnet-calling-endpoints` — steps 4–8
- `maxio-advanced-billing:dotnet-models` — steps 4, 5, 7
- `maxio-advanced-billing:dotnet-error-handling` — steps 3–8. Hazard: a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).
- `maxio-advanced-billing:dotnet-testing` — step 10

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `MaxioSettings` bound from section `Maxio` (`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`, optional `Maxio:Environment` = `US`/`EU`, default `US`). `IValidateOptions` + `ValidateOnStart()`: `ApiKey` non-blank; `ProductFamilyHandle` non-blank; `Subdomain` non-blank unless `BaseUrl` set; `BaseUrl` (if set) absolute http(s); `Environment` (if set) `US`/`EU`. Basic password is the provider constant `x` (not configurable, never blank). Messages name the key, never the value. |
| 2 | Secret sourcing & rotation | Dev: .NET user-secrets of `PublicApi` (loaded from `MAXIO_*` env vars by the operator); other deployments: env vars `Maxio__ApiKey` etc. or any `IConfiguration` source. Options captured once when the singleton client is built → a rotated key needs a process restart (documented; no hot reload). |
| 3 | Total timeout budget | Per attempt `Retry.Timeout = 10s`; `HttpClient.Timeout = 15s` backstop; GET `MaxRetries = 2`. Whole-call budget **20s** enforced in one place: the gateway's `BoundedAsync` (linked to the HTTP request's abort token). Post-failure settlement re-reads get their own 10s budget. Worst `POST /api/subscriptions` ≈ 4 bounded calls (adopt-customer GET, CreateCustomer, adopt-subscription GET, CreateSubscription) + one settle ≈ ≤ 60s, typically < 3s. |
| 4 | Write-retry ownership | `CreateCustomer` and `CreateSubscription` are POSTs → never resent by the SDK (default `HttpMethodsToRetry` kept; not widened). Only GETs retried. |
| 5 | Idempotency & ambiguous writes | `CreateCustomer`: real key = body `customer.reference` (provider: one customer per reference) — deterministic `eshop-cust-<sha256(buyer)>`; reconciliation `ReadCustomerByReference`. `CreateSubscription`: no documented uniqueness key; body `subscription.reference` (deterministic `eshop-sub-<sha256(claimKey)>`) is our lookup key; reconciliation `FindSubscription`. The injected `Idempotency-Key` header is not relied on. |
| 6 | Observability | App logs via `ILogger`: Information on customer/subscription created/adopted (buyer hash ref, Maxio ids, reference); Warning on provider 4xx (status + provider messages); Error on 5xx/transport/deserialization/auth; Warning on unknown outcomes and on page-cap truncation. Maxio error bodies modelled here carry no correlation id → we log our references + `Method`/`RequestUri` path from `SdkException`. SDK request/response lines at Information via explicit `LoggerFactory`. |
| 7 | Sensitive data | Customer first/last name + email (PII) in `CreateCustomer` bodies → `LogRequestBody = false`, `LoggerFactory` assigned explicitly from DI (disables `MAXIOADVANCEDBILLINGCLIENT_LOG`), our logs never echo bodies or emails. JSON bodies only (no form deny-list needed). |
| 8 | Environment selection | Groups: `Production` (in scope) and `Ebb` (not touched). Environments `Us`/`Eu` via `Maxio:Environment`. `Maxio:BaseUrl` set → `options.Server.Production.<selected env>.BaseUrl` = value verbatim; else `.Site = Maxio:Subdomain`. The SDK declares no sandbox environment: sandbox isolation = a separate Maxio sandbox site, selected purely by `Maxio:Subdomain` (dev = `cp-exp-3` via user-secrets). |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `CatalogContext.BillingCustomers` row, PK `BuyerId` (inserted before the call; released when the provider refuses) | primary-key violation on `SaveChanges` (enforced by SQL Server and EF InMemory) | `SubscriptionClaimStore.TryClaimCustomerAsync` `catch (DbUpdateException)` (+ InMemory's `ArgumentException`) → `false` | `SubscriptionClaimStore.TryClaimCustomerAsync` (in `SubscriptionService.EnsureCustomerAsync`) → `MaxioBillingGateway.CreateCustomerAsync` |
| `CreateSubscription` | `CatalogContext.SubscriptionEnrollments` row, PK `ClaimKey = buyer|family|generation` (inserted before the call; released on provider refusal) | primary-key violation on `SaveChanges` | `SubscriptionClaimStore.TryClaimSubscriptionAsync` `catch (DbUpdateException)` (+ InMemory's `ArgumentException`) → `false` | `SubscriptionClaimStore.TryClaimSubscriptionAsync` (in `SubscriptionService.SubscribeAsync`) → `MaxioBillingGateway.CreateSubscriptionAsync` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListProductsForProductFamily` (page walk, `PerPage = 200`) | `MaxPlanPages = 5`; stop on short page; whole walk under the gateway budget | `PlanCatalog.IsTruncated` → API field `isTruncated` on `GET /api/subscription-plans` | `MaxioBillingGateway.ListPlansAsync` (sets `IsTruncated`) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateCustomer` | `ReadCustomerByReference` | `customer.reference` = `eshop-cust-<hash>` | `MaxioBillingGateway.CreateCustomerAsync` catch → `SettleCustomerAsync`; not found → `CustomerCreateResult.Unknown` → `SubscriptionService.EnsureCustomerAsync` releases claim (provider reference uniqueness protects the retry) | `MaxioBillingGatewayTests.CreateCustomer_ConnectionFailure_SettlesByReference` |
| `CreateSubscription` | `FindSubscription` | `subscription.reference` = `eshop-sub-<hash>` | `MaxioBillingGateway.CreateSubscriptionAsync` catch → `SettleSubscriptionAsync`; not found → `SubscriptionCreateResult.Unknown` → enrollment `Unknown`, settled by `SubscriptionService.SettleUnsettledAsync` (called by `SubscriptionReconciliationService`, `GetMySubscriptionsAsync`, and a re-POST) | `MaxioBillingGatewayTests.CreateSubscription_ConnectionFailure_SettlesByReference`, `…_NotFoundOnSettle_ReturnsUnknown` |

## 6. Assumptions & Blockers

- Assumption: eShop user identity = JWT `Name` claim (username), matching `Basket.BuyerId`/`Order.BuyerId`; email from `UserManager`. `CreateCustomer` needs first/last name: taken from optional POST fields, else derived from the email local part / `"Customer"`.
- Assumption: one live subscription per user per configured product family; a second POST for the same plan replays the existing one (200), a different plan → 409. A subscription that becomes `canceled`/`expired`/`failed_to_create` ends the claim generation so the user can subscribe again.
- `UNVERIFIED`: whether Maxio enforces uniqueness of `subscription.reference` — not relied on (local claim + adopt-by-`FindSubscription` before create).
- `UNVERIFIED`: whether `ListCustomerSubscriptions` embeds `product.product_family` — filter by family only when present.
- `UNVERIFIED`: `ListProductsForProductFamily` 404 body declared `string`; a non-string body becomes `ResponseDeserializationException` (caught, same mapping as misconfigured family).
- `payment_collection_method` left to the provider default (plans do not require a payment method) — `UNVERIFIED` against the sandbox; verified live in step 10.
- Blockers: none.

## 7. Source labels

Rows cite the map page / SDK file they came from; `UNVERIFIED` and assumptions are listed in §6. Application decisions (`YOUR CALL — not in the map`): claim keys/reference formats, one-subscription-per-family policy, budgets (20s/10s/15s), cache TTL (5 min), page cap (5), HTTP status mapping.
