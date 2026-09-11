# Maxio Advanced Billing — Subscription Integration Plan

Plan file: `C:/claude-runs/t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-012/repo/maxio-plan.md` (dictated by brief; not edited by agent after return — main agent reads this).
Plan mode — no project files edited. No SDK clone path appears here.

---

## 1. Scope & sequence

Endpoint layer (`src/PublicApi`): add controllers / actions under existing `PublicApi` project, protected by existing JWT auth (not changed by this plan).

1. **Plan lookup** (`GET /api/subscription-plans`) — SDK: `client.ProductFamilies.ListProductFamilies` + `client.Products.ReadProductByHandle` (plan handles `eshop-pro` / `basic-plan` mapped to IDs 7126957 / 7126958) + `client.ProductFamilies.ReadProductFamily` (family id 3023074, handle `eshop-subscribe`). Response assembled by the app, not SDK.
2. **Customer + subscribe** (`POST /api/subscriptions`) — idempotent customer: `client.Customers.ReadCustomerByReference`; if missing `client.Customers.CreateCustomer` with `reference`; then `client.Subscriptions.CreateSubscription` with `customer_reference` / `customer_id` + `product_handle`/`product_id`. Uses `CreateSubscriptionRequest`.
3. **My subscriptions** (`GET /api/my-subscriptions`) — from JWT identity, resolve customer by `reference` (`CustomerReadByReference`); then `client.Customers.ListCustomerSubscriptions(customerId)`.

Operations in scope (map-confirmed signatures):
- `client.ProductFamilies.ListProductFamilies` (`map/operations/ProductFamilies.md`)
- `client.ProductFamilies.ReadProductFamily` (`map/operations/ProductFamilies.md`)
- `client.Products.ReadProductByHandle` (`map/operations/Products.md`)
- `client.Customers.CreateCustomer` (`map/operations/Customers.md`)
- `client.Customers.ReadCustomerByReference` (`map/operations/Customers.md`)
- `client.Customers.ListCustomerSubscriptions` (`map/operations/Customers.md`)
- `client.Subscriptions.CreateSubscription` (`map/operations/Subscriptions.md`)
- `client.Subscriptions.ListSubscriptions` (`map/operations/Subscriptions.md`) — available if list-by-customer isn't sufficient; prefer `ListCustomerSubscriptions` for step 3.

Config binding (application settings — names are the binding keys; SDK types verified against map):
- `Maxio:ApiKey` → `BasicAuth.Username` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`)
- `Maxio:Subdomain` → `options.Server.Production.Us.Site` (`ServerOptions`; `ServerEnvironment.Us` default per `sdk-map.md` line 220-222)
- `Maxio:ProductFamilyHandle` → app-level reference (handle `eshop-subscribe`; family id 3023074 — `YOUR CALL — not in the map` for which form to pass to ReadProductFamily; both `id` int and `handle:my-family` accepted per Notes)
- `Maxio:BaseUrl` (optional override) → `options.Server.Production.Us.BaseUrl` — only when set; else SDK uses `https://{site}.chargify.com` (US) / `https://{site}.ebilling.maxio.com` (EU) per `sdk-map.md`
- `Maxio:Environment` (optional; default `Us`) → `ServerEnvironment.Us` / `ServerEnvironment.Eu`

Sandbox facts (from brief, not SDK contract): site cp-exp-1, family handle `eshop-subscribe` (id 3023074), plan products `eshop-pro` (7126957) / `basic-plan` (7126958). These are hard values the endpoint layer uses; SDK only receives the id/handle.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Client & auth (from `sdk-map.md` lines 24-62, 199-225)

| Item | Exact form | Source (map) |
|---|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` (namespace root `MaxioAdvancedBilling`) | `sdk-map.md` |
| Options class | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Constructor | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` |
| Auth type / namespace | `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials` | `sdk-map.md`; `dotnet-authentication` |
| Auth pattern | Basic — `Username = <ApiKey>`, `Password = "x"` (literal) | `sdk-map.md` line 28 |
| Server env enum | `MaxioAdvancedBilling.Servers.ServerEnvironment` (`Us` / `Eu`) | `sdk-map.md` |
| Server options | `options.Server.Production.Us.Site = subdomain`; `.BaseUrl = override` | `sdk-map.md` 220-223 |

### 2.2 Operation signatures (map pages named)

| Controller property | Signature (params in order, nullables must be passed explicitly) | Request model + fields (C# `Name (wire_name): Type`, required?) | Response envelope + inner reads | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | n/a (query-only) | `IReadOnlyList<ProductFamilyResponse>` — read `.ProductFamily` (envelope has one field per `records-*`) | **Case B** `SdkException<RawError>` · `StatusCode` / `ReadAsString()` / `ReadAsJson<T>()` | none | `map/operations/ProductFamilies.md` |
| `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` — also accepts `handle:...` format per Note | n/a | `ProductFamilyResponse` — `.ProductFamily` | **Case B** `SdkException<RawError>` | none | `map/operations/ProductFamilies.md` |
| `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | n/a | `ProductResponse` — `.Product` | **Case B** `SdkException<RawError>` | none | `map/operations/Products.md` |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateCustomerRequest` (see §2.3) | `CustomerResponse` — `.Customer` | **Case A** `SdkException<CreateCustomerError>` · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none | `map/operations/Customers.md` |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | n/a | `CustomerResponse` — `.Customer` | **Case B** `SdkException<RawError>` | none | `map/operations/Customers.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | n/a | `IReadOnlyList<SubscriptionResponse>` — each `.Subscription` | **Case B** `SdkException<RawError>` | none | `map/operations/Customers.md` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` (see §2.3) | `SubscriptionResponse` — `.Subscription` | **Case A** `SdkException<CreateSubscriptionError>` · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | `map/operations/Subscriptions.md` |
| `client.Subscriptions` | `ListSubscriptions(..., int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 nullable params, must pass `null` to skip | n/a | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `map/operations/Subscriptions.md` |

Notes from map (only place that says when a call is accepted):
- `CreateCustomer` Note: reference must be unique; one customer per reference. Idempotent pattern requires lookup first (`ReadCustomerByReference`) or catch 422 then fall back — see Trap notes.
- `CreateSubscription` Note: specify product with `product_id` or `product_handle`; customer with `customer_id` or `customer_reference`; payment profile optional (`payment_profile_id`). If product requires payment info, creation may return 422 with 3DS or validation errors.
- `ReadProductFamily` Note: can specify by `id` number OR `handle:my-family` format.

### 2.3 Request / response models (key fields from `map/models/records-*.md` — read only the rows touched; not full dumps)

`CreateCustomerRequest` (`MaxioAdvancedBilling.Models.CreateCustomerRequest`, `Models/` namespace):
- `FirstName (first_name): string?` — optional
- `LastName (last_name): string?` — optional
- `Email (email): string?` — optional
- `Reference (reference): string?` — **idempotency key** (must be unique per Note)
- `Organization (organization): string?` — optional
- `Country (country): string?` — ISO 3166-1 2-char required if set (Note on page)
- `State (state): string?` — ISO 3166-2 / 2-3 char (Note)
- Full list in `map/models/records-1-Ac-Cr.md`; build only fields the endpoint needs (`reference`, `email`, `first_name`, `last_name`, `organization`).

`CreateSubscriptionRequest` (`MaxioAdvancedBilling.Models.CreateSubscriptionRequest`):
- `ProductHandle (product_handle): string?` **or** `ProductId (product_id): int?` — one required by server; pass `product_handle` using handles `eshop-pro` / `basic-plan`
- `CustomerReference (customer_reference): string?` **or** `CustomerId (customer_id): int?` — pass `customer_reference` (the same reference used at customer creation)
- `ProductPricePointId (product_price_point_id): int?` / `ProductPricePointHandle (product_price_point_handle): string?` — optional
- `PaymentProfileId (payment_profile_id): int?` — optional
- `Reference (reference): string?` — optional subscription reference
- Full list in `map/models/records-2-Cr-Ne.md`; do not pass unmodeled fields.

Response envelopes (read one level down — this is the classic case):
- `ProductFamilyResponse` (namespace `MaxioAdvancedBilling.Models`) — single field `ProductFamily: ProductFamily` — read `.ProductFamily` (map implies by response convention; verify in source only if compiler disagrees — per rules, never guess).
- `ProductResponse` — `.Product`
- `CustomerResponse` — `.Customer`
- `SubscriptionResponse` — `.Subscription`

Enum values needed (from `map/models/enums.md` — names literal, build via `FromValue` or static members):
- `BasicDateField`, `SortingDirection`, `ListProductsInclude`, `ListProductsFilter`, `SubscriptionStateFilter`, `SubscriptionDateField`, `SubscriptionSort`, `SubscriptionListInclude` — only load the specific members the query params need; do not dump full list here. `dotnet-models` covers `StringEnum<T>` construction.

---

## 3. Trap notes (attach to step; name hazard + consequence; `MUST load` pointer — never resolve inline)

- ⚠ Step 1 (client registration / DI) — `RetryOptions` members are `required`; `RetryOptions.Default()` is the constructor path. `Timeout` is per-attempt not total; `HttpMethodsToRetry` gates only status-triggered retries (transport failures retry all verbs including POST — a non-idempotent write can run twice; `MaxRetries` floor is 1, `0` rejected). **MUST load `dotnet-configuration-resilience`** before wiring retries/timeout.
- ⚠ Step 2 (auth) — Basic auth username = API key, password = literal `"x"`. Credentials must be set in `BasicAuth` before constructing `MaxioAdvancedBillingClient` or in the DI callback; key comes from `Maxio:ApiKey` config, never hardcoded. **MUST load `dotnet-authentication`**.
- ⚠ Step 3 (calling endpoints / named args) — many optional params have no C# default and mis-bind positionally; use named args (`dateField: null`, `ct: ct`). Cancellation token is named `ct`. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Step 4 (request models / unions / enums) — `StringEnum<T>` not C# enum; unions use factory methods and `TryGet…`; unmodeled JSON fields dropped on deserialize. Required members must be set via `init`. **MUST load `dotnet-models`**.
- ⚠ Step 5 (idempotent customer + subscription create) — `CreateCustomer` is not idempotent by retry; reference uniqueness is enforced server-side (422 `CustomerErrorResponse1`). Pattern: `ReadCustomerByReference`; if 404/no-match, `CreateCustomer`; if `CreateCustomer` throws `CreateCustomerError` with 422, fall back to lookup (reference already exists from concurrent call). Same for `CreateSubscription` — never blindly retry a 422 3DS/validation error; it is deterministic, not transient. **MUST load `dotnet-error-handling`** before writing the catch ladder.
- ⚠ Step 5 (list by customer) — `ListCustomerSubscriptions(int customerId)` takes the Advanced-Billing-generated `id`, not the app's `reference`; retrieve `Customer.Id` from `CustomerResponse.Customer` before calling.
- ⚠ Step 6 (error boundary) — All operations throw; no `…Result` variants (`sdk-map.md` line 118). Every call needs a try/catch. See §4 for both `JsonException` directions.

---

## 4. REQUIRED READING (load before implementation — not carried in sheet)

Every `dotnet-*` skill named above (`dotnet-client-initialization`, `dotnet-authentication`, `dotnet-calling-endpoints`, `dotnet-models`, `dotnet-error-handling`, `dotnet-configuration-resilience`, `dotnet-testing`). Load in that order; the trap notes tell you which step each governs.

Both `System.Text.Json.JsonException` hazard rows (verbatim from instructions; needed for every integration boundary):
- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the first sheet, not a later revision.

---

## 5. Assumptions & Blockers

Assumptions (app-level, labeled `YOUR CALL — not in the map` where they affect contract):
- JWT identity carries a claim/value that maps to Maxio `customer_reference`; the exact claim name and how `PublicApi` extracts it is `YOUR CALL — not in the map`. The SDK only sees `reference` (string) passed to `CreateCustomer` / `CreateSubscription` / `ReadCustomerByReference`.
- Endpoint route names (`/api/subscription-plans`, `/api/subscriptions`, `/api/my-subscriptions`) and the controller/file names (`SubscriptionPlansController`, etc.) are `YOUR CALL — not in the map`.
- Which fields of `CreateCustomerRequest` / `CreateSubscriptionRequest` the endpoint requires from the caller (e.g., whether `email` is mandatory) is `YOUR CALL — not in the map`; the SDK only enforces `required` members at deserialization time, not at the API-contract level.
- Whether `POST /api/subscriptions` creates a new `customer_reference` when the caller doesn't supply one is `YOUR CALL — not in the map`; SDK supports both `customer_reference` and `customer_id`.
- `BaseUrl` override presence / absence and `Environment` (`Us` vs `Eu`) come from config; defaults per `sdk-map.md` (`Environment = Us`; no `BaseUrl` = hosted `chargify.com` / `ebilling.maxio.com`).

Blockers (nothing stops planning; all contract facts resolved from map + brief):
- None. The operations needed are all in `map/operations/`. The one unverified live-wire question (whether the live payload for `CreateSubscription` truly includes all `CreateSubscriptionRequest` fields the map lists) is handled as a defensive directive: build request with only the fields the endpoint needs, extract best-effort from SDK response `.Subscription`, fall back to generic message if a field is missing — labeled `UNVERIFIED` only if live traffic proves it; plan does not assert it.
- Sandbox site (`cp-exp-1`) and product/family handles/ids are brief-supplied; SDK does not validate them at compile time.

---

## 6. Source references (map pages only — not clone paths)

- Client/auth/environment: `sdk-map.md` (lines 24-62, 199-225)
- Operations: `map/operations/ProductFamilies.md`, `map/operations/Products.md`, `map/operations/Customers.md`, `map/operations/Subscriptions.md`
- Request/response models: `map/models/records-1-Ac-Cr.md` (`CreateCustomerRequest`, `CustomerResponse`), `map/models/records-2-Cr-Ne.md` (`CreateSubscriptionRequest`, `SubscriptionResponse`), `map/models/records-3-Of-Su.md` / `records-4-Su-We.md` (response envelope fields if needed)
- Enums: `map/models/enums.md`
- Unions / error payload shapes: `map/models/unions.md`; typed error payload records in same `records-*` pages named by error accessors (e.g., `CustomerErrorResponse1`, `ErrorListResponse1`)
- Errors (core): `sdk-map.md` 81-102 (`SdkException<T>`, `RawError`, Case A/B)
- Namespaces: `sdk-map.md` 184-196 (using directives per type kind)

No clone path is named in this sheet. Source file opens (e.g., `Api/Customers.cs`) are done only if a map-sourced name fails to compile; per instructions the clone lives in system temp only and its path never appears here.
