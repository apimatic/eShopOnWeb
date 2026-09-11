# Maxio Advanced Billing .NET SDK — integration plan

**Path:** `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-009\repo\maxio-plan.md` (dictated by brief).
**Target repo:** eShopOnWeb `src/PublicApi`. **Target site:** sandbox `cp-exp-1` (`https://cp-exp-1.chargify.com`, `ServerEnvironment.Us` default). **Product family handle:** `eshop-subscribe`. **Package:** `AsadAli.AdvancedBilling.Sdk` (root namespace `MaxioAdvancedBilling`). **Clone:** not needed — all facts from bundled SDK map (`sdk-map.md` + `map/operations/*.md` + `map/models/*.md`). No `api-reference.md` opened; no clone path in this file.

---

## 1. Scope & sequence

Steps in order (each names the SDK controller/operation from the map):

1. **Client + DI + auth** — `MaxioAdvancedBillingClient` via `HttpClient` (long-lived, `IHttpClientFactory`); `BasicAuthCredentials { Username = "<api_key>", Password = "x" }`; `ServerOptions` / `Environment = ServerEnvironment.Us`; site = `cp-exp-1`. **Must load `dotnet-client-initialization`, `dotnet-authentication`.**
2. **List product families / products / prices (catalog for subscription-plans)** — `client.ProductFamilies.ListProductFamilies(...)` (Case B `RawError`); `client.Products.ListProducts(...)` (Case B); `client.ProductFamilies.ListProductsForProductFamily(productFamilyId: "eshop-subscribe", ...)` (Case A typed `ListProductsForProductFamilyError`, 404 `TryGetString`); `client.ProductPricePoints` operations (map `map/operations/ProductPricePoints.md`) for price-point listing if needed. **Must load `dotnet-calling-endpoints`, `dotnet-models`.**
3. **Customer create / update** — `client.Customers.CreateCustomer(...)` (Case A `CreateCustomerError`: 422 `TryGetCustomerErrorResponse1`); `client.Customers.UpdateCustomer(id, ...)` (Case A `UpdateCustomerError`: 404 `TryGetNoContent`, 422 `TryGetCustomerErrorResponse1`); `client.Customers.ListCustomers(...)` (Case B). Body envelopes: `CreateCustomerRequest` wraps `CreateCustomer` (wire fields per `Models/CreateCustomer.cs`); response `CustomerResponse` has `Customer (customer): Customer !req`.
4. **Subscription list / create / read (the user's endpoints maps here)** — `client.Subscriptions.CreateSubscription(...)` (Case A `CreateSubscriptionError`: 422 `TryGetErrorListResponse1`); `client.Subscriptions.ListSubscriptions(...)` (Case B `RawError`; pagination manual `page`/`perPage`); `client.Subscriptions.ReadSubscription(...)` (Case B, `include` optional); `client.Customers.ListCustomerSubscriptions(customerId, ...)` for `GET /api/my-subscriptions`-style customer-scoped list. Body: `CreateSubscriptionRequest` wraps `CreateSubscription` (key wire fields from operation Notes: `product_id`/`product_handle`, `customer_id`/`customer_reference`, `product_price_point_handle`/`id`, `payment_profile_id`, `customer_attributes` for new-customer; `plan` concept is modeled via product/price-point, not a separate plan entity). Response envelope: `SubscriptionResponse` has `Subscription (subscription): Subscription?` — read `.Subscription`.
5. **Subscription status / cancel / hold (optional, if endpoint supports cancellation)** — `client.SubscriptionStatus.CancelSubscription(...)` / `InitiateDelayedCancellation(...)` / `PauseSubscription(...)` / `ReactivateSubscription(...)` (all Case A typed errors with 404/422 accessors per `map/operations/SubscriptionStatus.md`). **Not required for the three endpoints but listed because user said "maybe list subscription statuses".**
6. **Error boundary + resilience** — wrap each call; classify `SdkException<T>` vs `JsonException`; configure `RetryOptions` (floor 1, `Timeout` per-attempt, `HttpMethodsToRetry` gates status-only so `POST` is not retried on 503 but IS retried on transport failure — non-idempotent writes may replay). **Must load `dotnet-error-handling`, `dotnet-configuration-resilience`, `dotnet-testing`.**

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Controller (`client.X`) | Method signature (params in order, types, nullable/must-pass) | Request model + fields (wire names, required?) | Response envelope + inner fields read | Error case + accessors + payload type | Pagination | Source |
|---|---|---|---|---|---|---|
| `ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 params nullable, must pass explicitly (pass `null`) | n/a (GET) | `IReadOnlyList<ProductFamilyResponse>`; each has `ProductFamily (product_family): ProductFamily?` | **B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`); no typed accessors | none | `map/operations/ProductFamilies.md` |
| `ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — path `productFamilyId` required; 8 query params nullable must-pass (defaults `page`/`perPage`) | n/a (GET) | `IReadOnlyList<ProductResponse>`; each `Product (product): Product !req` | **A** `SdkException<ListProductsForProductFamilyError>`; `TryGetString(out string)` [404] · `TryGetRawError` | manual `page`+`perPage` | `map/operations/ProductFamilies.md` |
| `Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | n/a | `IReadOnlyList<ProductResponse>`; `Product (product): Product !req` | **B** `RawError` | manual | `map/operations/Products.md` |
| `Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, must pass explicitly | `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req` (wire fields per `Models/CreateCustomer.cs` — `email`, `first_name`, `last_name`, `reference`, `organization`, `country`, `state`, `locale`, etc.; country = ISO 2-char, state = ISO per notes) | `CustomerResponse`; `Customer (customer): Customer !req` | **A** `SdkException<CreateCustomerError>`; `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none | `map/operations/Customers.md`; `map/models/records-1-Ac-Cr.md` (envelopes) |
| `Customers` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` | `UpdateCustomerRequest` → `Customer (customer): UpdateCustomer !req` | `CustomerResponse`; `Customer (customer): Customer !req` | **A** `SdkException<UpdateCustomerError>`; `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1` [422] · `TryGetRawError` | none | `map/operations/Customers.md` |
| `Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params nullable must-pass (defaults `page`=1, `perPage`=50) | n/a | `IReadOnlyList<CustomerResponse>` | **B** `RawError` | manual | `map/operations/Customers.md` |
| `Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable must-pass | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req` (key wire fields from Notes: `product_id`, `product_handle`, `product_price_point_id`/`handle`, `customer_id`/`customer_reference`, `payment_profile_id`, `customer_attributes` [new-customer], `reference`, `period`, `trial_ends_at`, etc.) | `SubscriptionResponse`; `Subscription (subscription): Subscription?` (nullable) — read `.Subscription` | **A** `SdkException<CreateSubscriptionError>`; `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none | `map/operations/Subscriptions.md`; `map/models/records-2-Cr-Ne.md` (envelope) |
| `Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params nullable must-pass | n/a | `IReadOnlyList<SubscriptionResponse>`; read `.Subscription` on each | **B** `RawError` | manual `page`+`perPage` | `map/operations/Subscriptions.md` |
| `Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable must-pass (e.g. `SubscriptionInclude.Coupons`, `.SelfServicePageToken`) | n/a | `SubscriptionResponse`; `Subscription (subscription): Subscription?` | **B** `RawError` | none | `map/operations/Subscriptions.md` |
| `SubscriptionStatus` | `CancelSubscription(int subscriptionId, CancellationRequest? body, CancellationToken ct = default)` | `CancellationRequest` → `Cancellation (cancellation): Cancellation !req` (wire fields per Notes / source) | `SubscriptionResponse`; `.Subscription` | **A** `SdkException<CancelSubscriptionApiError>`; `TryGetNoContent(out RawError)` [404] · `TryGetCancelSubscriptionErrorResponse` [422] · `TryGetRawError` | none | `map/operations/SubscriptionStatus.md` |

**Enum tables actually needed (namespace `MaxioAdvancedBilling.Models.Enums`)** — construct via static members or `Type.FromValue("wire")`; never treat as C# `enum`:

- `BasicDateField`: `UpdatedAt (updated_at)`, `CreatedAt (created_at)` — used by ProductFamilies/ListProducts/ListCustomers/ListSubscriptions.
- `SubscriptionStateFilter`: `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` — `ListSubscriptions` filter.
- `SortingDirection`: `Asc (asc)`, `Desc (desc)` — list sorts.
- `SubscriptionSort`: `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` — `ListSubscriptions` sort key.
- `SubscriptionListInclude`: `SelfServicePageToken (self_service_page_token)` — `ListSubscriptions` include.
- `SubscriptionInclude`: `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` — `ReadSubscription` include.
- `IntervalUnit`: `Day (day)`, `Month (month)` — product price-point interval.
- `ListProductsInclude`: not fully read; reference `map/operations/Products.md` / `map/models/enums.md` if needed.

**Client construction / auth / server (from `sdk-map.md` lines 24–199)** — `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)`; `BasicAuth = new BasicAuthCredentials { Username = "<key>", Password = "x" }`; `Environment = ServerEnvironment.Us`; `Retry` = `RetryOptions.Default()` (members all `required` — `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`). Server base for `Us`: `https://{site}.chargify.com`; for site `cp-exp-1`: `https://cp-exp-1.chargify.com`. **Namespace `MaxioAdvancedBilling.Core.Authentication.Basic` for `BasicAuthCredentials`; `MaxioAdvancedBilling.Servers` for `ServerEnvironment`; `MaxioAdvancedBilling.Core.Configuration` for `RetryOptions`.**

---

## 3. Trap notes (one per step, with `MUST load` — names hazard + consequence, never resolves it)

- ⚠ Step 1 (client/DI) — `HttpClient` must be long-lived / reused via `IHttpClientFactory`; SDK wrapper may be transient. `Timeout` is **per-attempt**, not total call; `RetryOptions` floor is 1 (`MaxRetries = 0` rejected at construction). **MUST load `dotnet-client-initialization`, `dotnet-configuration-resilience`.**
- ⚠ Step 1 (auth) — Basic auth username = API key, password = literal `"x"`. Credentials must be set before constructing `MaxioAdvancedBillingClient` or inside DI callback. Load key from config binding (no raw env-var naming invented here). **MUST load `dotnet-authentication`.**
- ⚠ Step 2/4 (calling) — Many optional query params (e.g., `dateField`, `state`, `include`) have **no C# default**; positional calls mis-bind. Always use named arguments (`dateField: null`, `ct: ct`). Response envelopes wrap payload one level down (`ProductResponse.Product`, `SubscriptionResponse.Subscription`) — read inner field, not the wrapper type. **MUST load `dotnet-calling-endpoints`.**
- ⚠ Step 2/4 (models) — Unions build via factory / implicit conversion and read via `TryGet…`; enums are `StringEnum<T>` (use `FromValue` or static members like `SubscriptionStateFilter.Active`); request/response records are immutable `init`-only with `required` properties. Unmodeled JSON fields dropped on deserialize. **MUST load `dotnet-models`.**
- ⚠ Step 6 (error boundary) — Every operation is **throw-only** (no `ApiResult` / no-throw variants generated). Case A (`SdkException<{Op}Error>`) vs Case B (`SdkException<RawError>`) is per-operation — confirm each row; `TryGetRawError` is NOT a catch-all on typed errors. For config failures (401, wrong host, timeout): check `BasicAuth` + `Environment`/`Server` before touching call sites. **MUST load `dotnet-error-handling`.**

---

## 4. REQUIRED READING (load before any implementation starts)

Load these skills **before coding**; this sheet carries the contract, not the skill contents:

- `dotnet-client-initialization` — governs Step 1 (client construction, `HttpClient` ownership, DI `AddMaxioAdvancedBillingClient`).
- `dotnet-authentication` — governs Step 1 (Basic credentials property name `BasicAuth`; password literal `"x"`; config loading).
- `dotnet-calling-endpoints` — governs Steps 2–5 (named arguments for optional params, envelope read-one-level-down, `ct:`, pagination `page`/`perPage`).
- `dotnet-models` — governs Steps 2–5 (request envelopes `CreateSubscriptionRequest`/`CreateCustomerRequest` inner fields; records `init` required; enums `StringEnum`; union `TryGet…`).
- `dotnet-configuration-resilience` — governs Step 6 (retry/status vs transport semantics; `Timeout` per-attempt; `HttpMethodsToRetry`; no disable of transport retry; `MaxRetries` floor 1; no built-in logging hook).
- `dotnet-error-handling` — governs Step 6 (throw-only; Case A typed accessors vs Case B `RawError`; `SdkException<T>` access patterns; `TryGet…` usage).
- `dotnet-testing` — governs Step 6 (test seam = `HttpClient` constructor arg; match project framework/assertion style).

**Mandatory `dotnet-error-handling` boundary caveats (verbatim from skill / map; both directions of `System.Text.Json.JsonException`):**

- A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

- **Assumption — endpoints mapping:** The user's `GET /api/subscription-plans` / `POST /api/subscriptions` / `GET /api/my-subscriptions` are interpreted as SDK `ProductFamilies`/`Products` list + `Subscriptions` create/list + `Customers`/`Subscriptions` customer-scoped list. If the live site uses a different path scheme (e.g., `/api/subscription-plans` is a site-specific alias, not the SDK's `product_families.json`), the call site must adapt — **UNVERIFIED** (only live traffic confirms wire payload shape vs generated model).
- **Assumption — product family handle `eshop-subscribe`:** Used for `ListProductsForProductFamily` and potentially as `product_handle` in `CreateSubscriptionRequest`. If the handle differs on `cp-exp-1`, the list/filter calls will return empty or 404; confirm handle via `ListProductFamilies` first.
- **Assumption — customer reference / ID persistence:** The integration's own persistence of `customer_reference` / `customer_id` and `subscription_id` is the implementer's design — SDK only requires them at call time. Not set by this sheet.
- **Blocker (none resolved, but noted):** Whether the live wire payload for `SubscriptionResponse.Subscription` fully matches the generated `Subscription` record (e.g., whether `subscription` is always present, or null only on errors) is **UNVERIFIED** — defensive code must extract `resp.Subscription` best-effort and fall back to generic message if null. Same for `CustomerResponse.Customer` / `ProductResponse.Product`.
- **Blocker (none):** The SDK map covers all operations named; no missing controller. If `ProductPricePoints` is needed, open `map/operations/ProductPricePoints.md`; not fully expanded here because the brief only says "maybe".

---

## 6. Source citations (every contract row above)

- SDK identity / client / auth / server / error model: `sdk-map.md` (lines 1–225).
- Operations page sources: `map/operations/ProductFamilies.md` (+ `Products.md`), `Customers.md`, `Subscriptions.md`, `SubscriptionStatus.md`, `SubscriptionProducts.md`.
- Response / request envelopes: `map/models/records-1-Ac-Cr.md` (`CreateCustomerRequest`, `CustomerResponse`), `map/models/records-2-Cr-Ne.md` (`CreateSubscriptionRequest`, `SubscriptionResponse`, `CustomerResponse` — note `SubscriptionResponse` actually in `records-4-Su-We.md`), `map/models/records-3-Of-Su.md` (`ProductFamilyResponse`, `ProductResponse`), `map/models/records-4-Su-We.md` (`SubscriptionResponse`).
- Enums: `map/models/enums.md` (entry table line 5+; members for `SubscriptionStateFilter` etc. read via search in file).
- Namespaces: `sdk-map.md` lines 184–195.
- No clone path appears; clone never left temp (not used — map sufficient).

---
*Plan written to `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-009\repo\maxio-plan.md`. Return with file path + brief below.*
