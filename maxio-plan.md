# Maxio Advanced Billing .NET SDK — eShopOnWeb integration plan

> Written to `<repo-root>/maxio-plan.md` (default per brief; brief specified `/repo/maxio-plan.md`, repo root is `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-004\repo`).
> Plan mode — no project files edited; no clone path exposed.

---

## 1. Scope & sequence

Implement three PublicApi endpoints (`src/PublicApi`) that call the SDK (`AsadAli.AdvancedBilling.Sdk` / `MaxioAdvancedBilling`). The SDK has no "subscription-plans" or "my-subscriptions" endpoint exactly — plan maps each API route to SDK operations and notes where application code fills the gap.

| Step | Endpoint | SDK call (controller) | Key contract fact |
|---|---|---|---|
| 1 | `GET /api/subscription-plans` | `client.Products.ReadProductByHandle("eshop-subscribe")` + `client.ProductPricePoints.ListProductPricePoints` (or `client.Products.ListProducts` with filter) to return plan list. The sandbox family handle = `eshop-subscribe` (id 3023074); plans `eshop-pro` (7126957, $299/mo) and `basic-plan` (7126958, $29/mo). Metered component `api-call` (3057195, $0.01/unit) via `client.Components` / `client.SubscriptionComponents`. | Plan list is derived, not a single SDK route. Source: `map/operations/Products.md`, `map/operations/ProductPricePoints.md`, `map/operations/Components.md`. |
| 2 | `POST /api/subscriptions` | Idempotent `client.Customers.ReadCustomerByReference` (or `ListCustomers(q=…)`) → `client.Customers.CreateCustomer` if missing → `client.Subscriptions.CreateSubscription` with `customer_reference` + `product_handle` (`eshop-pro` or `basic-plan`). Persist `userId → subscriptionId` in-memory (`UseOnlyInMemoryDatabase=true`). | `CreateSubscription` takes `CreateSubscriptionRequest? body`; `CreateCustomer` takes `CreateCustomerRequest? body`. Idempotency uses customer `reference` (unique). Source: `map/operations/Customers.md`, `map/operations/Subscriptions.md`. |
| 3 | `GET /api/my-subscriptions` | Read JWT identity (`userId`) → lookup in-memory mapping → `client.Subscriptions.ListCustomerSubscriptions(customerId)` if customer id known, else `client.Subscriptions.ListSubscriptions` with `customer`/reference filters. | Application-route only; SDK provides `ListCustomerSubscriptions(int customerId)` (Case B, `SdkException<RawError>`). Source: `map/operations/Subscriptions.md`, `map/operations/Customers.md`. |

Sequence: load all `dotnet-*` companions before coding (see §5). Client/DI first (§2), auth (§3), endpoints (§4), models (§5), error boundary (§6), resilience (§7), testing (§8).

---

## 2. CONTRACT SHEET

### 2.1 Warning (literal — do not edit)

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. Enums, unions, auth, server and client-config types are spread across different child namespaces (`MaxioAdvancedBilling.Core.Configuration`, `MaxioAdvancedBilling.Models.Enums`, `MaxioAdvancedBilling.Errors`, `MaxioAdvancedBilling.Api`), and two types configured side by side in the same options object routinely live in different ones. Dropping a type to root or `.Models` breaks the build.

### 2.2 Package / namespace / client identity (from `sdk-map.md` lines 9–52)

| | |
|---|---|
| NuGet package id | `AsadAli.AdvancedBilling.Sdk` |
| Root `using` namespace | `MaxioAdvancedBilling` (differs from package id) |
| Client class | `MaxioAdvancedBillingClient` (`MaxioAdvancedBilling`) |
| Options | `MaxioAdvancedBillingClientOptions` (`MaxioAdvancedBilling`) |
| Controllers (accessed via property) | `Client.Subscriptions`, `Client.Customers`, `Client.Products`, `Client.ProductPricePoints`, `Client.Components`, `Client.SubscriptionComponents` (`MaxioAdvancedBilling.Api`) |
| Auth scheme | HTTP Basic — username = API key, password = literal `"x"`; type `BasicAuthCredentials` (`MaxioAdvancedBilling.Core.Authentication.Basic`) |
| Environment enum | `ServerEnvironment` (`MaxioAdvancedBilling.Servers`) — `Us` (default) → `https://{site}.chargify.com`; `Eu` → `https://{site}.ebilling.maxio.com` |

### 2.3 Auth / server / config binding (from `sdk-map.md` 199–225, `map/operations/*` Notes)

| Binding key (app config section `Maxio:`) | SDK usage / note | Source |
|---|---|---|
| `Maxio:ApiKey` | `BasicAuthCredentials.Username` (password always `"x"`) | `sdk-map.md` auth section; `dotnet-authentication` |
| `Maxio:Subdomain` | Used in environment URL: `https://{subdomain}.chargify.com` (US default) or `.ebilling.maxio.com` (EU) | `sdk-map.md` Servers |
| `Maxio:ProductFamilyHandle` | `"eshop-subscribe"` (id 3023074) — pass as filter / lookup handle to `Products` / `ProductPricePoints` | Brief (sandbox); `map/operations/Products.md` (`ReadProductByHandle(string apiHandle)`) |
| `Maxio:BaseUrl` (optional override) | If set, construct `ServerOptions` / override `Environment`; else use `ServerEnvironment.Us` + subdomain | `dotnet-configuration-resilience`; `sdk-map.md` |

### 2.4 Operation contracts (map rows read in this session)

#### A. Plans / products — Step 1

| Controller, method | Signature (verbatim params) | Request model + key wire fields | Response envelope | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — (path param) | `ProductResponse` → `.Product` (`Product`) | **Case B** `SdkException<RawError>` → `.StatusCode`, `.ReadAsString()` | none | `map/operations/Products.md` |
| `client.Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable must-pass parameters | `ListProductsFilter` (wire fields per model page) | `IReadOnlyList<ProductResponse>` | **Case B** `SdkException<RawError>` | manual `page`/`perPage` | `map/operations/Products.md` |
| `client.ProductPricePoints.ListProductPricePoints` | `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, CancellationToken ct = default)` | `ProductIdModel` (id/handle) | `ListProductPricePointsResponse` (inner list) | **Case B** `SdkException<RawError>` | manual `page`/`perPage` (default 10) | `map/operations/ProductPricePoints.md` |
| `client.Components.ListComponents` (metered `api-call`) | `ListComponents(...)` per `map/operations/Components.md` | — | `IReadOnlyList<ComponentResponse>` | **Case B** (list ops) | manual | `map/operations/Components.md` |

Key wire names (from model index, `map/models/records-*`): `ProductResponse.Product` (envelope); `ProductHandle (handle)` / `ProductId (id)`; `PricePoint (price_point)` fields include `Id`, `Handle`, `Name`, `Price` (double?). **Do not copy full models** — load `dotnet-models` and read `models/records-*.md` for the exact fields of `ProductResponse`, `Product`, `CreateSubscriptionRequest`, `SubscriptionResponse`, `CustomerResponse`, `CreateCustomerRequest`.

Plan endpoint for `GET /api/subscription-plans`: use `ReadProductByHandle("eshop-subscribe")` to confirm family, then `ListProductPricePoints` by product id (or filter on family) to return `eshop-pro` / `basic-plan` with prices; optionally include metered `api-call` via `Components`. The exact price fields (`formatted_price`, `price`) come from `ProductPricePointResponse` / `CurrencyPrice` — confirm in `map/models/` before serializing to the endpoint.

#### B. Subscription creation — Step 2

| Controller, method | Signature | Request model | Response envelope | Error case + accessors | Pagination / notes | Source |
|---|---|---|---|---|---|---|
| `client.Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — body nullable, no default → must pass explicitly | `CreateCustomerRequest` (fields: `FirstName (first_name)`, `LastName (last_name)`, `Email (email)`, `Reference (reference)` required for idempotency, `Organization`, `Address`, etc.) — see `models/records-2-Cr-Ne.md` `CustomerAttributes` / request model | `CustomerResponse` → `.Customer` (`Customer`) | **Case A** `SdkException<CreateCustomerError>` → `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]; `TryGetRawError(out RawError)` fallback | none; notes say `reference` must be unique — only one customer per reference value | `map/operations/Customers.md`; model `map/models/records-2-Cr-Ne.md` |
| `client.Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` must pass explicitly | — | `CustomerResponse` → `.Customer` | **Case B** `SdkException<RawError>` | none | `map/operations/Customers.md` |
| `client.Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — body nullable, must pass | `CreateSubscriptionRequest` (key wire: `Subscription (subscription)`: `ProductHandle (product_handle)` or `ProductId (product_id)`; `CustomerReference (customer_reference)` or `CustomerId (customer_id)`; `ComponentAllocations`; `PaymentProfile`) — full fields in `models/records-2-Cr-Ne.md` | `SubscriptionResponse` → `.Subscription` (`Subscription`) | **Case A** `SdkException<CreateSubscriptionError>` → `TryGetErrorListResponse1(out ErrorListResponse1)` [422]; `TryGetRawError(out RawError)` | none; notes: specify product via handle/id; identify existing customer via id/reference; to create new customer pass `customer_attributes` inside request — but prefer separate `CreateCustomer` for idempotency | `map/operations/Subscriptions.md` (line 31–39) |

Idempotency rule (application-design, not SDK): before `CreateCustomer`, try `ReadCustomerByReference(userId)`; if it throws `SdkException<RawError>` with 404, create with `Reference = userId`. Before `CreateSubscription`, try `ListSubscriptions` filtering by `reference` / customer; if missing, create with `CustomerReference = userId` and `ProductHandle = "eshop-pro"` or `"basic-plan"`. The SDK does not provide a native "find by reference" for subscriptions — use `FindSubscription(string? reference)` (`map/operations/Subscriptions.md` line 42–53) which is Case A (`SdkException<FindSubscriptionError>` with `TryGetNoContent(out RawError)` [404]). That is the idempotency gate.

#### C. My-subscriptions — Step 3

| Controller, method | Signature | Request / filter | Response | Error case | Notes | Source |
|---|---|---|---|---|---|---|
| `client.Subscriptions.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path `customerId` from in-memory mapping (after resolving JWT `userId` → customer id) | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | Returns array; read `.Subscription` per item | `map/operations/Subscriptions.md` (line 28–33) |
| `client.Subscriptions.ListSubscriptions` | `ListSubscriptions(..., string? customer, ...)` (full param list in §2.4 A) | pass customer id / reference as filter if needed | `IReadOnlyList<SubscriptionResponse>` | **Case B** | Pagination manual | `map/operations/Subscriptions.md` (line 54–65) |

The endpoint `GET /api/my-subscriptions` never hits the SDK directly; it resolves `userId` from JWT, reads in-memory `userId ↔ subscriptionId` (and `userId ↔ customerId` if needed), then calls SDK to enrich. Because persistence is in-memory (`UseOnlyInMemoryDatabase=true`), mapping is lost on restart — document that as a deploy-level constraint, not SDK.

### 2.5 Enums actually needed (from `map/models/enums.md` — load for full lists)

- `ServerEnvironment` (`Us`, `Eu`) — `MaxioAdvancedBilling.Servers`
- `SubscriptionState` / `SubscriptionStateFilter` — `MaxioAdvancedBilling.Models.Enums`
- `SubscriptionSort` / `SortingDirection` — `MaxioAdvancedBilling.Models.Enums`
- `PricePointType` / `ProductPricePoint` filter types — `MaxioAdvancedBilling.Models.Enums`
- `BasicDateField` — filter dates
- `SubscriptionListInclude` — include tokens
- `CardType`, `PaymentType` — if payment profiles needed

Load `map/models/enums.md` before using any enum member; build with `StringEnum<T>.FromValue("wire")` or static members per map.

### 2.6 Response envelopes (mandate for reads)

Every response is wrapped; never treat `SubscriptionResponse` as `Subscription`.
- `SubscriptionResponse` has exactly one field `Subscription` (`Subscription`) — read `.Subscription`.
- `CustomerResponse` has `Customer` (`Customer`).
- `ProductResponse` has `Product` (`Product`).
- `ProductPricePointResponse` has inner price-point fields — confirm via `map/models/records-*.md`.

Source: `sdk-map.md` envelope note; per-controller pages name the return type explicitly.

---

## 3. Trap notes (one per step — load companion; do not resolve inline)

> ⚠ Step 1 (client registration / DI) — the SDK's `RetryOptions` (`MaxioAdvancedBilling.Core.Configuration`) and `Timeout` bound **per attempt**, not total call time; `HttpMethodsToRetry` gates status-triggered retry only, but transport failures (`HttpRequestException`) retry on **every verb including POST**, so a non-idempotent `CreateSubscription` may execute more than once and `MaxRetries = 0` is rejected at construction (floor = 1). **MUST load `dotnet-configuration-resilience`** before wiring retries / `HttpClient` lifetime.

> ⚠ Step 2 (auth) — Basic auth username = API key, password = literal `"x"` (`BasicAuthCredentials`); set before constructing `MaxioAdvancedBillingClient`; load key from `Maxio:ApiKey` binding, never hardcode. Environment + subdomain determine base URL; `BaseUrl` override requires `ServerOptions`. **MUST load `dotnet-authentication`** before setting credentials.

> ⚠ Step 3 (calling endpoints) — named arguments required for optional params that have no C# default; parameter names are literal (`ct`, `apiHandle`, `customerReference`, `productHandle`, etc.). `FindSubscription` is a lookup (Case A, 404 accessor `TryGetNoContent`), not a list. **MUST load `dotnet-calling-endpoints`** before first call.

> ⚠ Step 4 (models / requests) — request records use `init`-only setters with `required` properties; missing a required field fails at build, not at wire time. Unions (e.g., `ProductIdModel`) built via factory, read via `TryGet…`. Enums are `StringEnum<T>`, not C# enums. Unmodeled JSON fields dropped on deserialize. **MUST load `dotnet-models`** before constructing payloads.

> ⚠ Step 5 (error boundary) — every operation is **throw-only** (no `…Result` variants). Case A (`SdkException<{Op}Error>` — typed accessors) vs Case B (`SdkException<RawError>` — status/string only) varies per operation; confirm in each row. `JsonException` reaches boundary from **two directions**: (1) malformed/drifted 2xx body (missing `required` member) — surfaces as `JsonException`, not `SdkException`; (2) non-2xx body that doesn't match `{Operation}Error` — `JsonException` replaces `SdkException` and destroys HTTP status. A catch ladder that only catches `SdkException` lets both escape; mapping every `JsonException` to 5xx reports deterministic rejections as outages; retrying 5xx retries unrecoverable errors. **MUST load `dotnet-error-handling`** before writing any try/catch; include both hazard rows verbatim in the boundary (see §5 of this file).

> ⚠ Step 6 (resilience / persistence) — `UseOnlyInMemoryDatabase=true` is an application-level persistence flag, not SDK; the mapping table will not survive restart. The SDK client should be long-lived (via `IHttpClientFactory`), not rebuilt per request. Logging hook not built in — wire externally if needed. **MUST load `dotnet-configuration-resilience`** and `dotnet-testing`.

---

## 4. REQUIRED READING (load BEFORE implementation starts — these are not restated here)

- `dotnet-client-initialization` — governs Step 1 (client construction, `HttpClient` ownership, DI via `AddMaxioAdvancedBillingClient`, `ServiceCollectionExtensions`).
- `dotnet-authentication` — governs Step 2 (Basic auth property names, credential rotation, env setup).
- `dotnet-calling-endpoints` — governs Step 3 (named args, parameter order, envelope reads, pagination mechanics, cancellation token `ct`).
- `dotnet-models` — governs Step 4 (required/init-only, wire names vs C# names, enum construction, union factories, envelope shapes, unmodeled drop).
- `dotnet-error-handling` — governs Step 5 (Case A/B mechanics, `TryGet…` accessors, `RawError` fields, `JsonException` dual-direction hazard — both rows must be in the boundary — and why `Source-lookup` never replaces mapping the error type in the row).
- `dotnet-configuration-resilience` — governs Step 6 (retries, `Timeout` per-attempt semantics, `HttpMethodsToRetry`, server-base-URL override, pagination page/perPage defaults, no built-in logging hook).
- `dotnet-testing` — governs Step 8 (seam = `HttpClient` constructor; match existing test framework; avoid asserting SDK internals).

These skills carry defaults, examples, and the parts a one-line note cannot (e.g., exact `RetryOptions` construction, `BasicAuthCredentials` property, `StringEnum` usage, `JsonException` handling pattern). The sheet deliberately does not restate their contents.

---

## 5. Assumptions & Blockers

Assumptions (application design — not SDK contract):
- `userId` is the JWT claim / identity key; the in-memory mapping uses it directly.
- `GET /api/subscription-plans` returns derived plans, not a single SDK endpoint; the endpoint design is the implementer's.
- `POST /api/subscriptions` creates a new Maxio customer + subscription; idempotency relies on `customer_reference` + `FindSubscription`; the SDK supports it but does not enforce idempotency itself.
- Paid amounts ($299/mo, $29/mo, $0.01/unit) are sandbox data; the endpoint can either read from SDK responses or shadow with config — either is valid; the map does not declare a "price" contract endpoint.
- `UseOnlyInMemoryDatabase=true` is set by the application (e.g., EF Core in-memory provider); the SDK is unaware of it.
- JWT auth for `PublicApi` is the application's concern; SDK only uses Basic auth to Maxio.

Blocker (would stop planning / requires resolution):
- **None at map level.** All required controllers and operations (`Subscriptions`, `Customers`, `Products`, `ProductPricePoints`, `Components`) exist and are indexed. If a live wire payload differs from the generated response model (e.g., `SubscriptionResponse` envelope missing `subscription`), that is `UNVERIFIED` — use defensive "extract best-effort, fall back to generic message" and label it `UNVERIFIED`; do not assume live wire matches generated model.

---

## 6. Source citations (map pages only — never clone path; clone stays in temp, invisible)

Each contract row above cites its `map/operations/*.md` page or `map/models/*.md` group; the implementer should name the page when asking for clarification rather than reopening the full map. Key pages read this session:
- `sdk-map.md` (identity, auth, error model, namespaces, envelope rule)
- `map/operations/Subscriptions.md`
- `map/operations/Customers.md`
- `map/operations/Products.md`
- `map/operations/ProductPricePoints.md`
- `map/operations/Components.md`
- `map/models/enums.md` (enum lists — load for members)
- `map/models/records-2-Cr-Ne.md` (CreateSubscriptionRequest / CustomerResponse area — confirm fields via that page + `dotnet-models`)
- `map/models/records-3-Of-Su.md` / `records-4-Su-We.md` (SubscriptionResponse, ProductResponse — confirm envelope inner fields)

No `api-reference.md` opened; no clone filesystem path entered in this file.

---

## 7. Defensive-coding directive (for anything only live traffic can confirm)

If live Maxio wire payload for `SubscriptionResponse`, `ProductResponse`, or `CreateSubscriptionRequest` drifts from the generated model (e.g., missing required member, extra/renamed field), treat as `UNVERIFIED`: extract best-effort using the documented envelope path (`.Subscription`, `.Product`, `.Customer`), fall back to a generic message (no field-level guarantee), and do not rely on a specific field being present for authorization or billing decisions. Confirm with live traffic before tightening any extractor.

---

*Plan produced in plan mode — no project code edited. Return path: `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-004\repo\maxio-plan.md`.*
