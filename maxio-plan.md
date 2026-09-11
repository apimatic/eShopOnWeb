# Maxio Advanced Billing — eShopOnWeb PublicApi integration plan

Path: `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-006\repo\maxio-plan.md`
Agent: maxio-sdk (map-first; clone never leaves temp; clone path never appears here)
Package: `AsadAli.AdvancedBilling.Sdk` · Namespace: `MaxioAdvancedBilling` · Source commit: `v1.0.2` (`15db14b`) · Client: `MaxioAdvancedBillingClient` · Options: `MaxioAdvancedBillingClientOptions`

---

## 1. Scope & sequence

Integration adds 3 PublicApi endpoints interacting with Maxio via `MaxioAdvancedBillingClient` (`client.Customers`, `client.Subscriptions`, `client.Products`, `client.Components`). Sandbox entities use stable handles and unstable numeric IDs per requirement.

| Step | Endpoint (app) | SDK operations (controller) | Purpose |
|---|---|---|---|
| 1 | `GET /api/subscription-plans` | `client.Products.ListProducts` · `client.Products.ReadProductByHandle` · `client.Components.ListComponentsForProductFamily` · `client.Components.FindComponent` | List plans (Basic `basic-plan`, Pro `eshop-pro`) + family `eshop-subscribe`; expose metered `api-call` |
| 2 | `POST /api/subscriptions` | `client.Customers.ListCustomers` (search by `q`) · `client.Customers.CreateCustomer` · `client.Subscriptions.ListSubscriptions` (filter by customer/product) · `client.Subscriptions.CreateSubscription` | Idempotent customer (by email) then idempotent subscription; uses handles `eshop-subscribe`, `eshop-pro`, `basic-plan` |
| 3 | `GET /api/my-subscriptions` | `client.Subscriptions.ListSubscriptions` (filter by customer reference / state) · `client.Customers.ReadCustomer` (resolve customer id from email) | Return subscriptions for the calling customer |

Stable identifiers (use in payloads/filters): Product Family `eshop-subscribe`; Products `eshop-pro`, `basic-plan`; Component `api-call`. Numeric IDs (`customer_id`, `product_id`, `subscription_id`) are unstable — never hard-code; resolve via handle/reference at call time.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Client / auth / server (source: `sdk-map.md`, `MaxioAdvancedBillingClient.cs`, `ServerEnvironment.cs`)

| Concept | Type (namespace) | Key members / usage |
|---|---|---|
| Client | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` | Constructor: `(System.Net.Http.HttpClient, MaxioAdvancedBillingClientOptions)` |
| Options | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `BasicAuth`, `Environment` (`MaxioAdvancedBilling.Servers.ServerEnvironment`), `Retry`, `Server` |
| Auth credentials | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` | `Username` = API key, `Password` = literal `"x"` |
| Server env | `MaxioAdvancedBilling.Servers.ServerEnvironment` | `Us` (default, `https://{site}.chargify.com`), `Eu` |
| Retry options | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` | All members `required`; build from `RetryOptions.Default()`; `MaxRetries` floor is 1 (0 rejected) |

### Operations — exact signatures (from `map/operations/*.md`)

**Namespace conventions for controllers:** `client.Customers` → types under `MaxioAdvancedBilling.Api` / operation controllers own the method; response types in `MaxioAdvancedBilling.Models`; errors in `MaxioAdvancedBilling.Errors`.

#### Customers (`client.Customers` · `Api/Customers.cs`)

| Op | Signature (verbatim params) | Request model / fields | Response envelope | Error | Pagination | Source |
|---|---|---|---|---|---|---|
| `ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` | query `q` for email search | `IReadOnlyList<CustomerResponse>` (each `CustomerResponse.Customer`) | Case B `SdkException<RawError>` | manual `page`+`perPage` | `operations/Customers.md` |
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → must pass explicitly | `CreateCustomerRequest` (see model) | `CustomerResponse` (inner `.Customer`) | Case A `SdkException<CreateCustomerError>` (accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError`) | none | `operations/Customers.md` |
| `UpdateCustomer` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` | `UpdateCustomerRequest` | `CustomerResponse` | Case A `SdkException<UpdateCustomerError>` (accessors: `TryGetNoContent` [404], `TryGetCustomerErrorResponse1` [422], `TryGetRawError`) | none | `operations/Customers.md` |
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` | `CustomerResponse` | Case B | none | `operations/Customers.md` |

**Idempotent customer creation (by email):** Search `ListCustomers(q: email)`; if empty, `CreateCustomer` with `CreateCustomerRequest` setting `reference = email` (the map's Notes state: "only create one customer for a given reference value"). Use `reference` as stable identity; do not rely on numeric `id`.

**Customer request model fields (from records pages, scope-limited):**
- `CreateCustomerRequest`: `email`, `first_name`, `last_name`, `reference`, `organization`, `address`, `address_2`, `city`, `state`, `zip`, `country`, `phone`, `locale` (wire names match C# property names where not overridden; take exact from `map/models/records-*.md` for `CreateCustomerRequest`). `required` members must be set in initializer; check `required?` per record row.
- `UpdateCustomerRequest`: same-shaped partial; only fields to change set.
- `CustomerResponse`: envelope with single `.Customer` of type `Customer` (record); read inner `.Customer` after call.

#### Subscriptions (`client.Subscriptions` · `Api/Subscriptions.cs`)

| Op | Signature | Request model | Response envelope | Error | Pagination | Source |
|---|---|---|---|---|---|---|
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | query filters only | `IReadOnlyList<SubscriptionResponse>` (each `.Subscription`) | Case B `SdkException<RawError>` | manual `page`+`perPage` | `operations/Subscriptions.md` |
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default → must pass | `CreateSubscriptionRequest` (see below) | `SubscriptionResponse` (inner `.Subscription`) | Case A `SdkException<CreateSubscriptionError>` (`TryGetErrorListResponse1` [422], `TryGetRawError`) | none | `operations/Subscriptions.md` |

**Idempotent subscription creation:** Before calling `CreateSubscription`, use `ListSubscriptions` with `product = <stable-product-id-resolved>` / filter by `customer_reference` (or resolve customer id from `ReadCustomerByReference` / `ListCustomers`). If a subscription already exists for that customer + product, skip creation; else create with `customer_id` or `customer_reference` + `product_handle` (`eshop-pro`/`basic-plan`). The Notes say: identify existing customer with `customer_id` or `customer_reference`; specify product with `product_id` or `product_handle`.

**Subscription request fields (scope-limited from `map/models/records-*.md`):**
- `CreateSubscriptionRequest`: `product_handle` / `product_id`, `product_price_point_handle` / `product_price_point_id`, `customer_id` / `customer_reference`, `payment_profile_id`, `subscription_state`? (check enum `SubscriptionStateFilter` for allowed states), `coupon_code`, `metadata`, `reference`, `trial_ends_at`, billing/ship address via nested objects, etc. Use exact wire names; pass only needed fields.
- `SubscriptionResponse`: envelope with `.Subscription` (record) containing `id`, `state`, `product_handle`, `customer_id`, etc.

**Subscription enums / values:**
- `SubscriptionStateFilter` — values from `map/models/enums.md`; common filters: `active`, `trialing`, `past_due`, `unpaid`, `canceled`, etc. (load `map/models/enums.md` for exact member names).
- `SubscriptionDateField` — `created_at`, `updated_at`, `activated_at`, `current_period_started_at`, etc.
- `SubscriptionSort` — `created_at`, `updated_at`, etc.
- `SubscriptionListInclude` — `self_service_page_token` (must pass explicitly as `IReadOnlyList<>` when needed).
- `SortingDirection` — `asc`, `desc`.
- `BasicDateField` — date field for list filters.

#### Products (`client.Products` · `Api/Products.cs`)

| Op | Signature | Request / query | Response envelope | Error | Source |
|---|---|---|---|---|---|
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | query filters | `IReadOnlyList<ProductResponse>` (`.Product`) | Case B `SdkException<RawError>` | `operations/Products.md` |
| `ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `apiHandle` = `eshop-pro` or `basic-plan` | `ProductResponse` (`.Product`) | Case B | `operations/Products.md` |
| `ReadProduct` | `ReadProduct(int productId, CancellationToken ct = default)` | numeric `productId` (unstable) | `ProductResponse` | Case B | `operations/Products.md` |

**Plan details:** Use `ReadProductByHandle` with stable handles (`eshop-pro`, `basic-plan`) to read price, family, price points, and components without unstable IDs.

**Product response inner fields (scope):** `Product` record: `id` (int, unstable), `name`, `handle` (stable), `product_family` (record with `id` + `name`/`handle`), `price_in_cents`, `interval_unit` (`month`), `interval`, `default_price_point_id`, etc.

#### Components (`client.Components` · `Api/Components.cs`)

| Op | Signature | Request / query | Response envelope | Error | Source |
|---|---|---|---|---|---|
| `ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `productFamilyId` resolved from family `eshop-subscribe` via `ListProducts`/`ReadProduct` or family lookup | `IReadOnlyList<ComponentResponse>` | Case B | `operations/Components.md` |
| `FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` | `handle` = `api-call` (stable) | `ComponentResponse` (`.Component`) | Case B | `operations/Components.md` |
| `CreateMeteredComponent` | `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` — `body` nullable, no default | `productFamilyId` (stable handle or resolved numeric); `CreateMeteredComponent` request | `ComponentResponse` | Case A `SdkException<CreateMeteredComponentError>` (`TryGetNoContent` [404], `TryGetErrorListResponse1` [422], `TryGetRawError`) | `operations/Components.md` |

**Metered component `api-call`:** Resolve family `eshop-subscribe` to numeric family id (or use handle where supported); use `FindComponent("api-call")` to confirm existence; list via `ListComponentsForProductFamily`. If creation needed in setup, use `CreateMeteredComponent` with family id + `CreateMeteredComponent` body (fields: `name`, `handle`, `pricing_scheme`, `unit_balance`, `taxable`, etc. — check record page for exact names and `required` flags).

---

## 3. Trap notes

> ⚠ Step 1 (client registration) — `MaxioAdvancedBillingClientOptions.retry`/timeout do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; `RetryOptions` all members are `required` (start from `RetryOptions.Default()`); `MaxRetries` floor is 1 (0 rejected at construction). Transport failures retry on every verb including `POST`, so a non-idempotent `CreateSubscription` can execute more than once — make subscription creation idempotent (check before create). **MUST load `dotnet-configuration-resilience`** before wiring `Retry` / `Timeout`.

> ⚠ Step 2 (auth) — Basic auth requires `BasicAuthCredentials { Username = apiKey, Password = "x" }`; set before constructing client (`options.BasicAuth = ...`). Environment `ServerEnvironment.Us` (default) targets `https://{site}.chargify.com`; override `options.Server.Production.Us.Site = "..."`. **MUST load `dotnet-authentication`**.

> ⚠ Step 2 (calling endpoints) — All optional params on list/search ops have **no C# default** and must be passed explicitly (e.g., `ListCustomers(direction: null, dateField: null, startDate: null, endDate: null, startDatetime: null, endDatetime: null, q: "user@example.com", page: 1, perPage: 50)`). Named arguments must use literal param names (`ct:`, `q:`). Response envelopes wrap payload in one field — read `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductResponse.Product`, `ComponentResponse.Component`. **MUST load `dotnet-calling-endpoints`**.

> ⚠ Step 2 (models / enums) — Enums are `StringEnum<T>` (e.g., `SortingDirection`, `SubscriptionStateFilter`) not C# enums; construct/read via `Type.FromValue("asc")` or static members per `map/models/enums.md`. Unions (if any response variant) use factory + `TryGet…` with no `new`; request records are immutable with `init`-only setters and `required` fields must be set. **MUST load `dotnet-models`**.

> ⚠ Step 3 (error boundary) — Operations are throw-only (no `...Result` variants exist in this SDK). Every call needs try/catch. Case A (typed `SdkException<{Op}Error>`) vs Case B (`SdkException<RawError>`) differs per row — confirm from operation page. `TryGet…` accessors give status-specific payloads; `TryGetRawError` is fallback on typed errors; `RawError` gives `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`. **MUST load `dotnet-error-handling`**.

> ⚠ Step 4 (idempotency / config) — Customer idempotency relies on `reference` being unique (map Notes); do not assume `email` is unique at SDK layer unless enforced by your app. Subscription idempotency must be enforced by the application (search before create) — the SDK provides the search/list filters, not an upsert.

---

## 4. REQUIRED READING (load BEFORE implementation starts)

- `dotnet-client-initialization` — client construction, `HttpClient` lifetime, DI (`AddMaxioAdvancedBillingClient`), `MaxioAdvancedBillingClientOptions`.
- `dotnet-authentication` — Basic credentials property names, `Username`/`Password`, config binding.
- `dotnet-calling-endpoints` — controller accessor naming (`client.Customers`), parameter binding (many optional params require explicit `null`), cancellation token name `ct`, response envelope read-down (`.Customer`/`.Subscription`/etc.), pagination `page`/`perPage`.
- `dotnet-models` — `StringEnum<T>` vs C# enum, `required`/`init`, union factories/`TryGet…`, wire names via `[JsonPropertyName]`, record immutability.
- `dotnet-error-handling` — Case A (`SdkException<{Op}Error>` + `TryGet…` + `TryGetRawError`) vs Case B (`SdkException<RawError>` + `.StatusCode`/`.ReadAsString()`); `JsonException` boundary rules below; no `...Result` variants in this SDK.
- `dotnet-configuration-resilience` — `RetryOptions` (`Required` members, `Default()`), `Timeout` (per-attempt, not total), `HttpMethodsToRetry` (status-only; transport failures retry all verbs including `POST`), server override (`Site`, `BaseUrl`), no built-in logging hook.
- `dotnet-testing` — fake via `HttpClient` constructor seam; match project framework/style.

**Mandatory `dotnet-error-handling` boundary caveats (exact, verbatim):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. Both directions require explicit `JsonException` handling alongside the `SdkException` ladder; do not collapse them into a single 5xx mapping.

---

## 5. Assumptions & Blockers

- **Stable handles / unstable IDs:** The plan treats `eshop-subscribe` (family handle), `eshop-pro` and `basic-plan` (product handles), and `api-call` (component handle) as stable. Numeric `id` fields (`customer_id`, `product_id`, `subscription_id`) are resolved at runtime via lookup (not hard-coded). This is a design assumption; if the sandbox assigns different handles, adjust lookup filters.
- **Sandbox auth / site:** The plan assumes the app supplies an API key + site subdomain via configuration binding (key name not invented; use existing app config path). No environment override logic is specified — `ServerEnvironment.Us` default used unless `EU` required.
- **Customer search by email:** The SDK `ListCustomers(q: ...)` supports text search; the plan assumes `q` matching email yields a single match or empty. If the site has duplicate emails, the idempotency rule (reference = email) takes precedence over email equality — this is the application's consistency choice (YOUR CALL — not in the map).
- **Subscription idempotency:** The SDK has no upsert/conditional-create for subscriptions; idempotency is enforced by the application's `ListSubscriptions` check before `CreateSubscription`. This is the application's design, not a SDK feature.
- **No blocker on map:** All required operations (`ListCustomers`, `CreateCustomer`, `UpdateCustomer`, `ListSubscriptions`, `CreateSubscription`, `ListProducts`, `ReadProductByHandle`, `ListComponentsForProductFamily`, `FindComponent`, `CreateMeteredComponent`) are present in `map/operations/*.md`. No missing SDK capability blocks planning.
- **Unverified:** Whether live sandbox payloads exactly match the generated `CreateSubscriptionRequest` / `SubscriptionResponse` shapes (e.g., presence of `product_handle` vs `product_id` in a given site config) is unverified by the map alone. Defensive coding directive: build `CreateSubscriptionRequest` with both `product_handle` (preferred, stable) and `customer_reference` when possible; fall back to resolving numeric IDs via lookup if `Handle`-based call is rejected by the live server. Label any live-only discrepancy `UNVERIFIED`; do not invent wire fields.

---

## 6. Source references (per row, for questions to agent)

- Client / auth / server / retry: `sdk-map.md` (§Getting a client, §Servers & auth, RetryOptions table).
- Customers operations: `map/operations/Customers.md`.
- Subscriptions operations: `map/operations/Subscriptions.md`.
- Products operations: `map/operations/Products.md`.
- Components operations: `map/operations/Components.md`.
- Models / enums / unions: `map/models/records-*.md`, `map/models/enums.md`, `map/models/unions.md`.
- Error core / access patterns: `sdk-map.md` (§Error-handling model) and per-operation error-accessor cells.

No source file path from a clone appears above (clone lives in temp per rules, never written to this file, never exposed to main agent).
