# Maxio Advanced Billing .NET SDK — Integration Plan (eShopOnWeb)

Path: `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-001\repo\maxio-plan.md`
Mode: Plan. No repo files edited except this file.

> **Plan written to the dictated path** (no alternative chosen). The plan is read by the main agent before any SDK call; no code edited until this file exists and is read (per `integrate-maxio` gate 3).

---

## 1. Scope & sequence

In order, using only the controllers/operations below (names from `sdk-map.md` index, pages from `map/operations/`):

1. **Client + auth + server** — `MaxioAdvancedBillingClient` + `BasicAuthCredentials` + `ServerEnvironment` (see Contract Sheet §Client/auth).
2. **Product families (by handle)** — `client.ProductFamilies.ReadProductFamily` (handle format `handle:my-family` per Notes; parameter type is `int id` per signature — see Contract Sheet). `ListProductFamilies` available but has no handle filter.
3. **Plans / products by family** — `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, ...)` (returns `IReadOnlyList<ProductResponse>`); also `client.Products.ListProducts` with `filter` if broader list needed.
4. **Customer by email (idempotent)** — search via `client.Customers.ListCustomers(string? q, ...)` (`q` = email search), then `client.Customers.ReadCustomerByReference(string reference)` for lookup, `CreateCustomer(CreateCustomerRequest? body)` with `reference` set for idempotency, and `UpdateCustomer(int id, UpdateCustomerRequest? body)` for updates.
5. **Create subscription** — `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body)` (specifies `product_id`/`handle`, `customer_id`/`reference`, optional `customer_attributes`).
6. **List subscriptions by customer** — `client.Customers.ListCustomerSubscriptions(int customerId)` (returns `IReadOnlyList<SubscriptionResponse>`).
7. **Get subscription details** — `client.Subscriptions.ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, ...)`.
8. **Component info** — `client.Components.ReadComponent(int componentId)` / `ListComponents` if needed; `SubscriptionComponent` model for allocation context (not called directly unless needed).

Capability not in map = none — all operations exist in `sdk-map.md`. No invented data paths.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Client / auth / server (binding keys; not invented names)

| Config item | Binding / property name | Type / namespace | Default / notes | Source |
|---|---|---|---|---|
| Client options | `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` | — | `sdk-map.md` §Getting a client |
| Auth (Basic) | `options.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` | `BasicAuthCredentials` in `MaxioAdvancedBilling.Core.Authentication.Basic` | Password literal `"x"`; username = API key | `sdk-map.md` §Servers & auth |
| Environment | `options.Environment = ServerEnvironment.Us` | `ServerEnvironment` (`MaxioAdvancedBilling.Servers`) | `Us` = `https://{site}.chargify.com`; `Eu` = `https://{site}.ebilling.maxio.com` | `sdk-map.md` §Servers & auth |
| Site override | `options.Server.Production.Us.Site = "your-subdomain"`; `BaseUrl` override for mock | `ServerOptions` / `ProductionOptions` | `{site}` defaults to `subdomain` | `sdk-map.md` §Servers & auth |
| Retry | `options.Retry = RetryOptions.Default()` then set members | `RetryOptions` in `MaxioAdvancedBilling.Core.Configuration` | All members required; `MaxRetries` floor = 1 (0 rejected at construction) | `sdk-map.md` §Getting a client |
| Client ctor | `new MaxioAdvancedBillingClient(httpClient, options)` | `HttpClient` (long-lived, reused via `IHttpClientFactory`) | Singleton / scoped client wrapper over `HttpClient`; do not rebuild per call | `dotnet-client-initialization` trap (load before wiring) |

### Operation rows

Legend: all nullable params with no C# default must be passed **explicitly** (`null` to skip). `ct` = cancellation token.

| Step | Controller property | Method signature (literal params, order) | Request model (key fields · wire names) | Response envelope | Error case + accessors + payload type | Pagination | Source map page |
|---|---|---|---|---|---|---|---|
| 2 | `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` | — (path-only `id`; Notes say `handle:my-family` format — see Unresolved / UNVERIFIED below) | `ProductFamilyResponse` → inner `ProductFamily (product_family): ProductFamily?` | Case B: `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`) | none | `map/operations/ProductFamilies.md` |
| 2 | `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | — | `IReadOnlyList<ProductFamilyResponse>` | Case B: `SdkException<RawError>` | none | `map/operations/ProductFamilies.md` |
| 3 | `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | — | `IReadOnlyList<ProductResponse>` → inner `Product (product): Product!req` (wire `product`) | Case A: `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | manual `page`/`perPage` (defaults 1/20) | `map/operations/ProductFamilies.md` |
| 4 | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req`; `CreateCustomer` fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?`, etc. (full list `records-2-Cr-Ne.md` line ~124) | `CustomerResponse` → `Customer (customer): Customer !req` | Case A: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none | `map/operations/Customers.md` |
| 4 | `client.Customers` | `UpdateCustomer(int id, UpdateCustomerRequest? body, CancellationToken ct = default)` | `UpdateCustomerRequest` → `Customer (customer): UpdateCustomer !req` | `CustomerResponse` → inner `Customer` | Case A: `SdkException<UpdateCustomerError>` — `TryGetNoContent(out RawError)` [404] · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none | `map/operations/Customers.md` |
| 4 | `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` | — | `IReadOnlyList<CustomerResponse>` | Case B: `SdkException<RawError>` | manual `page`/`perPage` (1/50) | `map/operations/Customers.md` |
| 4 | `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference ← string reference` | `CustomerResponse` | Case B: `SdkException<RawError>` | none | `map/operations/Customers.md` |
| 4 | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` → inner `Subscription (subscription): Subscription?` | Case B: `SdkException<RawError>` | none | `map/operations/Customers.md` |
| 5 | `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req`; `CreateSubscription` fields include `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerAttributes (customer_attributes): CreateCustomer?`, etc. (full list `records-2-Cr-Ne.md`/`records-3-Of-Su.md`) | `SubscriptionResponse` → inner `Subscription (subscription): Subscription?` | Case A: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none | `map/operations/Subscriptions.md` |
| 7 | `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | query `include ← SubscriptionInclude` (values: `Coupons`, `SelfServicePageToken`) | `SubscriptionResponse` → inner `Subscription` | Case B: `SdkException<RawError>` | none | `map/operations/Subscriptions.md` |

### Model / envelope reference (namespaces explicitly)

All records live in `MaxioAdvancedBilling.Models`. Responses wrap in `MaxioAdvancedBilling.Models` as well (same namespace per map index §Models). Key envelopes (from `map/models/records-*.md`):

- `CustomerResponse` (`records-2-Cr-Ne.md` line ~43): `Customer (customer): Customer !req`
- `SubscriptionResponse` (`records-4-Su-We.md` line ~66): `Subscription (subscription): Subscription?`
- `ProductFamilyResponse` (`records-3-Of-Su.md` line ~64): `ProductFamily (product_family): ProductFamily?`
- `ProductResponse` (`records-3-Of-Su.md` / `records-4-Su-We.md` — find `ProductResponse`): `Product (product): Product !req`
- `ComponentResponse` (`records-1-Ac-Cr.md` line 101): `Component (component): Component !req`
- `CreateCustomerRequest` (`records-1-Ac-Cr.md` ~125): `Customer (customer): CreateCustomer !req`
- `UpdateCustomerRequest`: `Customer (customer): UpdateCustomer !req`
- `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req`

Inner `Customer` wire fields (from `CreateCustomer` row, records-2 line ~124; full `Customer` model on same page): `first_name`, `last_name`, `email`, `reference`, `organization`, `address`, `address_2`, `city`, `state`, `zip`, `country`, `phone`, `locale`, `tax_exempt`, `tax_exempt_reason`, `parent_id`, `salesforce_id`.

Inner `Subscription` wire fields (from `CreateSubscription` / `Subscription` rows, records-3/4): `id`, `customer_id`, `product_id`, `product_handle`, `product_price_point_id`, `product_price_point_handle`, `reference`, `state`, `activated_at`, `canceled_at`, `expires_at`, `current_period_started_at`, `current_period_ends_at`, `trial_started_at`, `trial_ended_at`, `next_billing_at`, etc. (full list in `Models/Subscription.cs`; map does not dump all inside envelope table — cite source file `Models/Subscription.cs` when a field is needed).

Inner `ProductFamily` wire fields (from `ProductFamily` row, records-3): `id`, `name`, `handle`, `description`, `accounting_code`, `created_at`, `updated_at`.

Plan = `Product` (in this SDK terminology) wire fields (from `Product` / `CreateOrUpdateProduct` rows): `id`, `name`, `handle`, `description`, `accounting_code`, `require_credit_card`, `price_in_cents`, `interval`, `interval_unit`, `trial_price_in_cents`, etc. (source `Models/Product.cs`).

Component wire fields (from `Component` row, records-1 line 81): `id`, `name`, `handle`, `pricing_scheme`, `unit_name`, `unit_price`, `product_family_id`, `kind`, `archived`, `description`, `default_price_point_id`, `prices`, `price_points_url`, etc.

### Enums actually needed (from `map/models/enums.md`)

| Enum | Namespace | Values needed | Source file |
|---|---|---|---|
| `SortingDirection` | `MaxioAdvancedBilling.Models.Enums` | `Asc (asc)`, `Desc (desc)` | `Models/Enums/SortingDirection.cs` |
| `BasicDateField` | `MaxioAdvancedBilling.Models.Enums` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `Models/Enums/BasicDateField.cs` |
| `SubscriptionStateFilter` | `MaxioAdvancedBilling.Models.Enums` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` | `Models/Enums/SubscriptionStateFilter.cs` |
| `SubscriptionInclude` | `MaxioAdvancedBilling.Models.Enums` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | `Models/Enums/SubscriptionInclude.cs` |
| `ListProductsInclude` | `MaxioAdvancedBilling.Models.Enums` | `PrepaidProductPricePoint (prepaid_product_price_point)` | `Models/Enums/ListProductsInclude.cs` |
| `SubscriptionListInclude` | `MaxioAdvancedBilling.Models.Enums` | (used by `ListSubscriptions`; not directly in scope unless needed) | `Models/Enums/SubscriptionListInclude.cs` |

Build with `StringEnum<T>.FromValue("asc")` or static members (`SortingDirection.Asc`) — not C# `enum` syntax (`dotnet-models` trap; load `dotnet-models` before constructing requests).

---

## 3. Trap notes (named hazard + MUST load pointer; not resolved here)

> ⚠ Step 1 (client registration & DI) — `HttpClient` must be long-lived / reused via `IHttpClientFactory`; SDK wrapper may be transient; retry/timeout options do **not** bound a whole call and are **not** the `HttpClient` timeout you register separately. **MUST load `dotnet-client-initialization` and `dotnet-configuration-resilience`** before wiring.
>
> ⚠ Step 1 (auth) — Basic auth uses `Username` = API key, `Password` = literal `"x"`; credentials must be set before constructing the client or inside the DI callback, loaded from config (binding key `BasicAuth` on `MaxioAdvancedBillingClientOptions`). **MUST load `dotnet-authentication`** before wiring.
>
> ⚠ Step 2/3/4/5 (calling endpoints) — most list/search operations have many optional params with **no C# default**; positional call without explicit `null` binds wrong. Use named arguments (parameter names are literal from map, `ct:` for token). **MUST load `dotnet-calling-endpoints`** before first call.
>
> ⚠ Step 4/5 (models / requests) — `required` properties must be set; unions (`Quantity`, `PricePoint`, etc.) use factories + `TryGet…`; enums are `StringEnum<T>`; unmodeled JSON fields dropped on deserialize. **MUST load `dotnet-models`** before building `CreateCustomerRequest` / `CreateSubscriptionRequest`.
>
> ⚠ Step 4/5 (idempotency / reference) — only `reference` (not email) is a unique lookup key per `Customers.md` Notes; email search via `q` is search, not exact lookup. No compiler catches dropping `reference` from `CreateCustomer`. **MUST load `dotnet-calling-endpoints`** to confirm query param mappings.
>
> ⚠ All steps (error boundary) — every operation is **throw-only** (no `…Result` variants). Two `JsonException` directions reach the boundary: (a) drifted/malformed 2xx body (missing `required` member → `JsonException` from deserialization, **not** `SdkException`) and (b) non-2xx body not matching `{Operation}Error` shape (throws `JsonException` *while constructing error*, replacing `SdkException` and destroying HTTP status). **MUST load `dotnet-error-handling`** before writing any try/catch; include both rows verbatim below.
>
> ⚠ Step 5 (create subscription) — `CreateSubscription` may require payment info / card depending on product options; 3DS post-authentication returns 422 with `action_link`. Notes say do not use real card info for testing; PCI compliance needed for raw card details. This is a provider note, not a code decision — application must handle 422 with `action_link` defensively.
>
> ⚠ Step 6 (list by customer) — `ListCustomerSubscriptions` takes `int customerId`; no `q`/email filter here — must resolve customer ID first (from `ListCustomers` or `ReadCustomerByReference`).

---

## 4. REQUIRED READING (load before implementation starts)

These are to be loaded **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — governs Step 1 (client construction, DI registration, `HttpClient` lifetime).
- `dotnet-authentication` — governs Step 1 (Basic auth property names, credential loading).
- `dotnet-calling-endpoints` — governs Steps 2–7 (named args, `ct`, pagination, envelope reads one level down).
- `dotnet-models` — governs Steps 4–5 (request construction, enums, unions, wire names, `required`).
- `dotnet-error-handling` — governs all try/catch; see mandatory hazard rows below.
- `dotnet-configuration-resilience` — governs Step 1 retries/timeouts; note transport-failure retries apply to `POST`; non-idempotent writes can execute more than once and `MaxRetries = 0` is rejected.
- `dotnet-testing` — governs test seams (`HttpClient` constructor arg) if tests added later.
- `maxio-getting-started` — already loaded; confirms package id `AsadAli.AdvancedBilling.Sdk`, namespace `MaxioAdvancedBilling`, map boundary, and clone-only-on-gap rule (not needed here — all contracts answered by map).

**Mandatory `dotnet-error-handling` hazard rows (verbatim, both directions — boundary written early):**

> - A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
> - A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**UNVERIFIED (only live traffic can confirm):**
- Whether `ReadProductFamily(int id)` actually accepts the handle string `handle:my-family` on the wire when the parameter is declared `int id`; map Notes say yes, signature says `int`. Defensively: try `int` first (if handle resolves to numeric id) or prepare to read source `Api/ProductFamilies.cs` if the wire rejects the string. Treat as defensive: if lookup by handle fails with `int`, extract best-effort from `ReadProductFamily` and fall back to listing by `ListProductFamilies()` + filter client-side (not a live-wire claim — only a defensive directive).
- Whether `CreateSubscription` requires `payment_profile_id` or card details for the specific site/product; live traffic confirms. Defensive: pass only required fields from request model, capture 422 with `TryGetErrorListResponse1`, and expose `action_link` if present.

---

## 5. Assumptions & Blockers

Assumptions (application design belongs to implementer, not SDK):
- The application will hold `MaxioAdvancedBillingClient` as a singleton / scoped service using `HttpClient` via `IHttpClientFactory`; persistence, concurrency, and caller-request contracts are the implementer's.
- Idempotency for customer by email is implemented by the application (search by `q`, store returned `id`/`reference`, use `reference` on create) — SDK does not provide an "upsert by email" operation.
- "Plan" is interpreted as `Product` (`ProductResponse`) in this SDK; if the live site uses a different concept, that is a live-traffic gap, not a map gap.
- `SubscriptionComponent` / usage allocation is not in direct call scope unless needed for subscriptions with components — omit until requirement changes.

Blockers (nothing stops planning — all requested operations exist in map):
- None. All 7 requested capabilities have map rows.
- If the site requires EU hosting, `ServerEnvironment.Eu` must be set — this is configuration, not a blocker.

---

## 6. Source citations (map pages only — never the clone path)

- Client/auth/server: `sdk-map.md` (sections Getting a client, Servers & auth)
- Product families / lists / products: `map/operations/ProductFamilies.md`
- Products / plans: `map/operations/Products.md`
- Customers / references / subscriptions-by-customer: `map/operations/Customers.md`
- Subscriptions create / read / list: `map/operations/Subscriptions.md`
- Components: `map/operations/Components.md` (if needed)
- Model envelopes / fields: `map/models/records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md`, `unions.md`, `enums.md`
- Error model / Case A/B / accessors: `sdk-map.md` §Error-handling model; per-operation accessors from the operation pages above.
