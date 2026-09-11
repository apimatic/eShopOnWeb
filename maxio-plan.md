# Maxio Advanced Billing .NET SDK — Plan (eShopOnWeb / src/PublicApi)

Output path (per brief): `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-003\repo\maxio-plan.md`
Plan only — no project files edited yet (gate: plan exists, read, then implement).

---

## 1. Scope & sequence (order of implementation)

1. **Settings / auth binding** — bind `Maxio` config section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` optional); load env `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN=cp-exp-1`, `MAXIO_ENVIRONMENT=US`, `MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe`. Register `MaxioAdvancedBillingClient` (namespace `MaxioAdvancedBilling`) with Basic auth (username = API key, password literal `"x"`) and server `ServerEnvironment.Us` (default) → `https://cp-exp-1.chargify.com`; optional `BaseUrl` overrides.
2. **Client + DI** — use `MaxioAdvancedBillingClient` / `MaxioAdvancedBillingClientOptions`; long-lived `HttpClient` via `IHttpClientFactory`; never rebuild per request.
3. **Customer idempotency layer** (PublicApi backing) — idempotent by `reference` (app's email/reference) via `ReadCustomerByReference(string reference)` (map: `Customers.md`); create via `CreateCustomer(CreateCustomerRequest? body, ct)` (Case A `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1` [422], `TryGetRawError` fallback). Notes: reference must be unique; `reference` is the app's shared identifier.
4. **Subscription endpoints (src/PublicApi)**
   - `GET /api/subscription-plans` → list plans (price points / products) via `client.Products.ListProducts(...)` / `client.ProductPricePoints.ListProductPricePoints(...)`; filter by product-family handle `eshop-subscribe` (resolved from `ProductFamilyHandle` config or `FindProductFamily` / `ReadProductByHandle`). Plans referenced by handle: `eshop-pro` ($299/mo), `basic-plan` ($29/mo). Source: `map/operations/Products.md`, `ProductPricePoints.md`, `ProductFamilies.md`.
   - `POST /api/subscriptions` → enrollment via `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, ct)` (Case A `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1` [422], `TryGetRawError`). Use `product_handle` (plan handle) and `customer_id` or `customer_reference`; payment method not required per operation Notes (depends on product options). No `payment_profile_id` required in plan.
   - `GET /api/my-subscriptions` → list customer subscriptions via `client.Customers.ListCustomerSubscriptions(int customerId, ct)` → `IReadOnlyList<SubscriptionResponse>`; or `client.Subscriptions.ListSubscriptions(...)` with `state` filter (`SubscriptionStateFilter`). Read state + billing via `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, ct)` → `SubscriptionResponse` (Case B `SdkException<RawError>`; access `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`). Next billing / period data live in response fields (`current_period_ends_at`, `next_billing_at`, `state` as `SubscriptionState` StringEnum).
5. **Metered component** (`api-call`, $0.01/unit) — component definition under family `eshop-subscribe` via `client.Components.FindComponent(string handle)` or `ListComponentsForProductFamily`; create definition via `client.Components.CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, ct)` (Case A `SdkException<CreateMeteredComponentError>` — `TryGetNoContent` [404], `TryGetErrorListResponse1` [422], `TryGetRawError`). Actual usage reporting via `SubscriptionComponents` / events (outside this endpoint scope — noted as blocker if usage endpoint needed).
6. **Error boundary** — every SDK call wrapped; read `SdkException<T>` per operation's case (A: typed `{Op}Error` with `TryGet…`; B: `RawError`). Handle `JsonException` from deserialization (2xx body drift / missing `required`) and from error-object construction (non-2xx body mismatched shape) — both destroy status or become 5xx if over-mapped; see REQUIRED READING §4.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side-by-side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Client / auth / server (binding key `Maxio`)

| Fact | Value / namespace / source |
| --- | --- |
| Package id | `AsadAli.AdvancedBilling.Sdk` |
| Root import namespace | `MaxioAdvancedBilling` |
| Client class | `MaxioAdvancedBilling.Clients.MaxioAdvancedBillingClient` (implied by root + usage; confirm from `dotnet-client-initialization`) |
| Options | `MaxioAdvancedBilling.Clients.MaxioAdvancedBillingClientOptions` |
| Auth scheme | HTTP Basic — username = API key, password = literal `"x"` (`dotnet-authentication`) |
| Environment (default) | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` → `https://{site}.chargify.com` |
| Sandbox site subdomain (env / config) | `cp-exp-1` → base URL `https://cp-exp-1.chargify.com` (unless `BaseUrl` overrides) |
| Config section (binding key) | `Maxio` — properties: `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` (optional) |
| Env vars to read (app layer) | `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT` (map to `Us`/`Eu`), `MAXIO_DEFAULT_PRODUCT_FAMILY` |
| Product family handle (entity) | `eshop-subscribe` (id varies at runtime; resolve via `ProductFamilies` or config) |
| Plan handles (entities) | `eshop-pro` ($299/mo), `basic-plan` ($29/mo) — resolve by `api_handle` via `Products.ReadProductByHandle` |
| Metered component handle (entity) | `api-call` ($0.01/unit) — under `eshop-subscribe` family |

### Operations in scope (map pages: `map/operations/*.md`)

| Controller · method | Signature (params order, required-but-nullable) | Request model + key fields (wire) | Response envelope + inner | Error case / accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → pass explicitly | `CreateCustomerRequest` (map `records-2-Cr-Ne.md` / `Records...`) — fields include `email`, `first_name`, `last_name`, `reference`, etc.; `reference` must be unique | `CustomerResponse` (wraps `Customer`) | **Case A** `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1` [422] · `TryGetRawError` fallback | none | `operations/Customers.md` |
| `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` nullable, no default | — | `CustomerResponse` | **Case B** `SdkException<RawError>`: `StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()` | none | `operations/Customers.md` |
| `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |
| `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default | `CreateSubscriptionRequest` (map `records-2-Cr-Ne.md`) — `product_handle`, `product_price_point_handle`, `customer_id`/`customer_reference`, `customer_attributes`, optionally `payment_profile_id`; Notes say payment info may be required depending on product options, not always required | `SubscriptionResponse` (wraps `Subscription`) | **Case A** `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1` [422] · `TryGetRawError` fallback | none | `operations/Subscriptions.md` |
| `Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable, pass explicitly | — | `SubscriptionResponse` | **Case B** `SdkException<RawError>` | none | `operations/Subscriptions.md` |
| `Subscriptions.ListSubscriptions` | `ListSubscriptions(...)` 14 nullable params + `page`=1, `perPage`=20 | — | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `operations/Subscriptions.md` |
| `Products.ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable params + defaults | — | `IReadOnlyList<ProductResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` (default 20) | `operations/Products.md` |
| `Products.ReadProductByHandle` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` — `apiHandle` nullable, pass explicitly | — | `ProductResponse` | **Case B** `SdkException<RawError>` | none | `operations/Products.md` |
| `ProductPricePoints.ListProductPricePoints` | `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, CancellationToken ct = default)` | — | `ListProductPricePointsResponse` | (map page needed; assume Case B or A — verify before call) | manual `page`+`perPage` (default 10) | `operations/ProductPricePoints.md` |
| `Components.FindComponent` | `FindComponent(string handle, CancellationToken ct = default)` — `handle` nullable, pass explicitly | — | `ComponentResponse` | **Case B** `SdkException<RawError>` | none | `operations/Components.md` |
| `Components.ListComponentsForProductFamily` | `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 7 nullable + defaults | — | `IReadOnlyList<ComponentResponse>` | **Case B** `SdkException<RawError>` | manual | `operations/Components.md` |
| `Components.CreateMeteredComponent` | `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, CancellationToken ct = default)` — `body` nullable, pass explicitly; `productFamilyId` is `string` per map (handles can be string IDs) | `CreateMeteredComponent` (map `models/records-...`) — `name`, `price_point_price`, `unit_balance`, etc.; meter type | `ComponentResponse` | **Case A** `SdkException<CreateMeteredComponentError>`: `TryGetNoContent` [404] · `TryGetErrorListResponse1` [422] · `TryGetRawError` fallback | none | `operations/Components.md` |

### Enums needed (from `map/models/enums.md`; namespace `MaxioAdvancedBilling.Models.Enums` — separate `using` required)

- `SubscriptionState` (`pending`, `trialing`, `active`, `past_due`, `canceled`, `expired`, `on_hold`, `awaiting_signup`, `failed_to_create`, `soft_failure`, `suspended`, `paused`, `unpaid`, `trial_ended`, `assessing`) — used to read subscription state; filter via `SubscriptionStateFilter`.
- `SubscriptionStateFilter` (`active`, `canceled`, `expired`, `expired_cards`, `on_hold`, `past_due`, `pending_cancellation`, `pending_renewal`, `suspended`, `trial_ended`, `trialing`, `unpaid`) — `ListSubscriptions` filter.
- `SubscriptionInclude` (`coupons`, `self_service_page_token`) — `ReadSubscription` include.
- `SubscriptionDateField` (`current_period_ends_at`, `current_period_starts_at`, `created_at`, `activated_at`, `canceled_at`, `expires_at`, `trial_started_at`, `trial_ended_at`, `updated_at`) — list/order.
- `ComponentKind` (`metered_component`, `quantity_based_component`, `on_off_component`, `prepaid_usage_component`, `event_based_component`)
- `SortingDirection` (`asc`, `desc`)
- `BasicDateField` (`updated_at`, `created_at`)
- `PricePointType` (`catalog`, `default`, `custom`)
- `ServerEnvironment` (`Us`, `Eu`) — from `Servers/` / client options

### Response envelope warning (contract fact)

- `CustomerResponse` has exactly one inner field `Customer`; `SubscriptionResponse` has `Subscription`; `ProductResponse` has `Product`; `ComponentResponse` has `Component`. Reads go one level down: `result.Customer.Id`, not `result.Id`. Confirmed by operation rows (`map/operations/*.md`). No `{Op}Result` / no-throw variants exist in this SDK — every operation throws.

---

## 3. Trap notes (named hazard + MUST load pointer)

- ⚠ Step 1 (client registration / auth) — Maxio auth is Basic with username = API key, password = `"x"`; `ServerEnvironment` and `BaseUrl` are separate settings in `MaxioAdvancedBillingClientOptions`, not the `HttpClient` you register. **MUST load `dotnet-authentication`** before wiring credentials; **MUST load `dotnet-client-initialization`** before writing `new MaxioAdvancedBillingClient(...)` or DI `AddMaxioAdvancedBillingClient`.
- ⚠ Step 1c (configuration) — retry/timeout options (`MaxRetries` floor 1, `Timeout` per-attempt) do **not** bound the whole call; transport failures (`HttpRequestException`) retry on **every** verb including `POST`, so a non-idempotent `CreateSubscription` can execute >1 time and `MaxRetries = 0` is rejected at construction. **MUST load `dotnet-configuration-resilience`** before tuning.
- ⚠ Step 2 (customer lookup / idempotency) — idempotency is by `reference`, not by `email`; `ReadCustomerByReference(string reference)` is Case B `RawError`; if missing, `StatusCode` is 404 (no typed `TryGetNoContent` on this op). Create (`CreateCustomer`) is Case A (`CreateCustomerError`). **MUST load `dotnet-calling-endpoints`** for named-argument rules (`ct:`, pass `null` for skipped nullable params) and **MUST load `dotnet-error-handling`** to distinguish Case A vs B per call.
- ⚠ Step 3 (subscription creation) — `CreateSubscriptionRequest` uses `product_handle` / `product_price_point_handle`; `customer_reference` can substitute `customer_id`; `payment_profile_id` is optional per operation Notes (depends on product). Response `SubscriptionResponse` wraps `Subscription`; read `Subscription.State`, `.CurrentPeriodEndsAt`, `.NextBillingAt`. **MUST load `dotnet-models`** for request construction (wire names, nullability, `StringEnum<T>` not C# enum) and **MUST load `dotnet-calling-endpoints`** before first call.
- ⚠ Step 4 (listing / pagination) — `ListCustomers`, `ListSubscriptions`, `ListProducts`, `ListProductPricePoints`, `ListComponentsForProductFamily` all use manual `page`+`perPage` (defaults vary: customers 50, subscriptions 20, products 20, price points 10, components 20). Named params required; do not use positional for nullable filters. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Step 5 (metered component) — `CreateMeteredComponent` takes `string productFamilyId` (family handle or ID) plus `CreateMeteredComponent? body`; `FindComponent(string handle)` / `ListComponentsForProductFamily(int productFamilyId, ...)` use different id types (string handle vs int family id). Confirm `productFamilyId` type from source if map ambiguous. **MUST load `dotnet-models`**.
- ⚠ Step 6 (error boundary / JsonException — both directions) — `JsonException` reaches boundary from (a) drifted/malformed 2xx body (missing `required` member) → surfaces from deserialization, **not** `SdkException`; (b) non-2xx body that doesn't match `{Operation}Error` → throws `JsonException` while error object constructed, **replacing** `SdkException` and destroying HTTP status. A boundary mapping all `JsonException` to 5xx reports deterministic rejections as outages; a retry-on-5xx retries unfixable calls. **MUST load `dotnet-error-handling`** before writing the catch ladder; include both rows verbatim.
- ⚠ Step 6 (testing) — the `HttpClient` constructor argument is the test seam; match project framework. **MUST load `dotnet-testing`** before stubbing.

---

## 4. REQUIRED READING (load BEFORE implementation starts)

These skills carry the hazard resolution the sheet names; the sheet deliberately does not restate their defaults or semantics. Load them in order; all named above in trap notes.

- `dotnet-client-initialization` — governs Step 1 (client construction, builder/options, `HttpClient` ownership, DI registration).
- `dotnet-authentication` — governs Step 1 (Basic auth, key from config, password `"x"`).
- `dotnet-calling-endpoints` — governs Steps 2–5 (controller access `client.Customers`/`Subscriptions`/`Products`/`Components`, named params, `ct:`, null-skip, envelope reads one level down).
- `dotnet-models` — governs Steps 3–5 (`CreateSubscriptionRequest`, `CreateCustomerRequest`, `SubscriptionResponse`, `ProductResponse`, enums `StringEnum<T>`, unions `TryGet…`, wire names vs C# names).
- `dotnet-error-handling` — governs Steps 2, 3, 5, 6 (Case A/B per operation; `TryGet…` accessors; `RawError`; `JsonException` from 2xx drift vs non-2xx mismatch — include both verbatim rows below; never parse `.ToString()` when accessor exists).
- `dotnet-configuration-resilience` — governs Step 1 (retries, timeout per-attempt, base URL / server selection, pagination defaults, no logging hook, `POST` transport retries).
- `dotnet-testing` — governs verification (fake `HttpClient` seam, framework match, assert behavior not execution).

Both `JsonException` caveat rows (mandatory, first sheet, never moved to a revision):

> - A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
> - A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

---

## 5. Assumptions & Blockers

Assumptions (your call — weigh against intent):
- The `PublicApi` project uses ASP.NET Core controllers; endpoint paths `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions` are created in `src/PublicApi`; no existing Maxio integration present (verified by absence of SDK package / `MaxioAdvancedBilling` references — confirm at build time).
- Customer identity for `/api/my-subscriptions` is resolved from the application's own auth/session (e.g., `User.Claims` / `CustomerId`), not from the Maxio customer ID directly — the sheet does not set that mapping; implementer decides.
- `eshop-pro` / `basic-plan` and `api-call` are pre-created in the sandbox `cp-exp-1`; the integration resolves by handle, not by creating them (creation endpoints exist but are out of scope unless needed).
- The metered component `api-call` is already defined; the endpoint only needs to read/allocate it, not recreate it every call. If creation is needed, `CreateMeteredComponent` contract applies.
- `BaseUrl` is optional; if omitted, `ServerEnvironment.Us` + `Subdomain` builds `https://cp-exp-1.chargify.com`.
- The `reference` value for idempotent customer lookup is the application's customer identifier (e.g., email or app reference); the map confirms `reference` is unique.

Blockers (stop planning until resolved — do not invent replacements):
- None blocking the contract sheet; all requested entities (family handle `eshop-subscribe`, plans by handle, metered component by handle, customer/subscription operations, error types) are covered by the SDK map (`map/operations/Customers.md`, `Subscriptions.md`, `Products.md`, `Components.md`). If live payload differs (e.g., `SubscriptionResponse` omits a documented inner field, or `CreateSubscriptionRequest` requires an unexpected payment field), mark that row `UNVERIFIED` in the implementer notes and fall back to generic message parsing — do not invent the wire shape.
- If `ProductFamilyHandle` config value (`eshop-subscribe`) does not resolve to an existing family in sandbox `cp-exp-1`, `ListProducts` / `ReadProductByHandle` will return empty / 404; this is a sandbox-data issue, not an SDK contract gap.

---

## 6. Source citations (per row — not clone paths; clone never leaves temp, never appears here)

- Client/auth/server: `sdk-map.md` (SDK identity) + `maxio-getting-started` skill intro.
- Customer ops / envelopes: `map/operations/Customers.md` (lines 7–81, signatures `CreateCustomer`, `ReadCustomerByReference`, `ListCustomerSubscriptions`, error cases A/B, `CustomerResponse` envelope implicit from return type).
- Subscription ops / envelopes / pagination: `map/operations/Subscriptions.md` (lines 31–145, `CreateSubscription`, `ReadSubscription`, `ListSubscriptions`, error accessors, `SubscriptionResponse` envelope, `page`/`perPage` defaults).
- Product/plan ops / price-point: `map/operations/Products.md` (lines 28–39 `ListProducts`; 51–59 `ReadProductByHandle`) + `map/operations/ProductPricePoints.md` (`ListProductPricePoints`).
- Component ops / metered: `map/operations/Components.md` (lines 28–37 `CreateMeteredComponent`; 72–94 `FindComponent`; 96–100 `ListComponentsForProductFamily`; error cases A/B).
- Enums: `map/models/enums.md` (`SubscriptionState`, `SubscriptionStateFilter`, `SubscriptionInclude`, `ComponentKind`, `SortingDirection`, `BasicDateField`, `ServerEnvironment`, etc.).
- Models / request/response records: `map/models/records-2-Cr-Ne.md` (CreateSubscription / CreateCustomer range) + `unions.md` if any union needed; wire names and nullability from record pages, not memory.
- Companion skill pointers: `dotnet-*` skills listed in REQUIRED READING; each name is the exact skill file under `.opencode/skills/`.

---

*Plan written at required path; no clone path included; no project files edited; no source path exposed. Implement only after this file is read, required skills loaded, and Assumptions & Blockers checked.*
