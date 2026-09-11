# Maxio Advanced Billing — eShopOnWeb integration plan

Output path (dictated by brief): `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-003\repo\maxio-plan.md`

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

---

## 1. Scope & sequence

Steps in order; each names the SDK operation(s) used.

1. **Client / DI / auth setup** — `MaxioAdvancedBillingClient` + `MaxioAdvancedBillingClientOptions`; `BasicAuthCredentials`; `ServerEnvironment.Us`; site set via `Server.Production.Us.Site`; optional `BaseUrl` override via `Server.Production.Us.BaseUrl`. **MUST load `dotnet-client-initialization`** and **`dotnet-authentication`**.
2. **Bind settings** — config section `Maxio` (keys: `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl` optional). `ApiKey` → `BasicAuthCredentials.Username`; `Subdomain` → `options.Server.Production.Us.Site`; `ProductFamilyHandle` → `handle` to use with family/product endpoints; `BaseUrl` (if present) → `options.Server.Production.Us.BaseUrl`. **YOUR CALL — not in the map** for config key names (app design); SDK contract only defines `BasicAuthCredentials`, `ServerOptions`, `ServerEnvironment`.
3. **Get product family** — `client.ProductFamilies.ReadProductFamily(id, ct)` (map notes allow `handle:my-family` format in the `id` param; family handle `shoop-subscribe`, ID `3023074`). Also `ListProductFamilies` if needed. Source: `map/operations/ProductFamilies.md`. **UNVERIFIED** whether passing a handle string through an `int`-typed param works at wire level — defensive: try `3023074` (int ID) first, fall back to handle only if live traffic confirms.
4. **List products for family / list plans** — `client.ProductFamilies.ListProductsForProductFamily(productFamilyId, …)` (string `productFamilyId`; can pass handle or ID). Source: `map/operations/ProductFamilies.md`. Plans in scope by wire IDs: `shoop-pro` / `7126957` ($299/mo), `basic-plan` / `7126958` ($29/mo).
5. **Idempotent customer creation** — `client.Customers.ListCustomers(q: email/handle, …)` (query `q` searches by email, reference, name, org) → if empty, `client.Customers.CreateCustomer(body, ct)` (`CreateCustomerRequest` → `Customer` inner record with `email`, `handle`, etc.). If found, use existing `id`. Source: `map/operations/Customers.md`. Idempotency key choice (`email` vs `handle`) is **YOUR CALL — not in the map**.
6. **Create subscription** — `client.Subscriptions.CreateSubscription(body, ct)` (`CreateSubscriptionRequest` → inner `Subscription` record; wire fields include `product_handle`/`product_id`, `customer_id`, `subscription` wrapper). Source: `map/operations/Subscriptions.md`. Must supply customer by ID from step 5 and plan/product reference.
7. **List customer subscriptions** — `client.Customers.ListCustomerSubscriptions(customerId, ct)` returns `IReadOnlyList<SubscriptionResponse>`. Source: `map/operations/Customers.md`.
8. **Error boundary** — every call is throw-only (no `Result` variants). **MUST load `dotnet-error-handling`** before coding `try/catch`. See REQUIRED READING for the two `System.Text.Json.JsonException` directions.

---

## 2. CONTRACT SHEET

### Client / auth / server (map index + source links)

| Fact | Value / type | Namespace | Source |
|---|---|---|---|
| Client | `MaxioAdvancedBillingClient` | `MaxioAdvancedBilling` (root) | `sdk-map.md` §Getting a client |
| Options | `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` | `sdk-map.md` §Getting a client |
| Auth | `BasicAuthCredentials` (`Username`, `Password`) | `MaxioAdvancedBilling.Core.Authentication.Basic` | `sdk-map.md` §Auth |
| Auth pattern | `Username = API key`; `Password = "x"` | — | `sdk-map.md` (line 201–203) |
| Environment | `ServerEnvironment.Us` / `.Eu`; enum members `US`/`EU` | `MaxioAdvancedBilling.Servers` | `sdk-map.md` §Environments |
| Server override | `options.Server.Production.Us.BaseUrl`; `options.Server.Production.Us.Site` | `MaxioAdvancedBilling.Servers` / `.Core.Configuration` | `sdk-map.md` §Servers (line 214–223) |
| Retry | `RetryOptions` (required members; start from `.Default()`) | `MaxioAdvancedBilling.Core.Configuration` | `sdk-map.md` (line 63–77) |

### Operations in scope

#### ProductFamilies — get family / list products by family

| Controller prop | Method signature (params in order, types, required-but-nullable) | Request model + fields (wire) | Response envelope + inner read | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` — `id`: `int`; notes allow `handle:my-family` format in this param | — | `ProductFamilyResponse` (single, no inner wrapper named differently — read `.ProductFamily?` verify from record) | **Case B** — `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `TryGetRawError`) | none | `map/operations/ProductFamilies.md` (ReadProductFamily) |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — first 8 params nullable, no default; must pass explicitly to skip | — | `IReadOnlyList<ProductResponse>` | **Case A** — `SdkException<ListProductsForProductFamilyError>` (`TryGetString` [404], `TryGetRawError`) | manual `page`+`perPage` | `map/operations/ProductFamilies.md` |

**Notes / missing / unverified:**
- The family handle `shoop-subscribe` has ID `3023074` (user-provided). Use int `3023074` with `ReadProductFamily`. Passing handle via `int` param is **UNVERIFIED** by live wire — defensive code tries int first.
- No separate "search family by handle" operation in map; only `ReadProductFamily`. If needed, `ListProductFamilies` can filter, but no `handle` query param documented.

#### Products — list plans (site-wide) / read by handle

| Controller prop | Method signature | Request | Response | Error | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | — | `IReadOnlyList<ProductResponse>` | **Case B** (`RawError`) | manual | `map/operations/Products.md` |
| `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` | **Case B** | none | `map/operations/Products.md` |

Plan references by wire IDs / handles given by user (`shoop-pro` / 7126957, `basic-plan` / 7126958). Use `ReadProductByHandle("shoop-pro")` or `ListProducts` + filter to confirm.

#### Customers — idempotent creation + list by customer subscriptions

| Controller prop | Method signature | Request | Response | Error | Source |
|---|---|---|---|---|---|
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default; must pass explicitly | `CreateCustomerRequest` → inner `CreateCustomer` record (`email`, `handle`, `first_name`, `last_name`, etc.; wire names from `Models/CreateCustomerRequest.cs` / records page) | `CustomerResponse` (wraps `Customer`) | **Case A** — `SdkException<CreateCustomerError>` (`TryGetCustomerErrorResponse1` [422], `TryGetRawError`) | `map/operations/Customers.md` |
| `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params nullable, no default; `q` is search query | — | `IReadOnlyList<CustomerResponse>` | **Case B** (`RawError`) | `map/operations/Customers.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` | **Case B** (`RawError`) | `map/operations/Customers.md` |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | — | `CustomerResponse` | **Case B** | `map/operations/Customers.md` |

**Idempotency approach (YOUR CALL — not in map):** Recommended defensive flow — `ListCustomers(q: email)`; if non-empty take first `.Customer?.Id`; else `CreateCustomer(new CreateCustomerRequest { Customer = new CreateCustomer { Email = ..., Handle = ... } })`. If using `reference` as idempotency key, use `ReadCustomerByReference`. The map does not state which field is the idempotency key for creation; `CreateCustomer` Notes say `reference` must be unique if provided.

#### Subscriptions — create + list by customer (already covered above via Customers controller)

| Controller prop | Method signature | Request | Response | Error | Source |
|---|---|---|---|---|---|
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, no default; must pass explicitly | `CreateSubscriptionRequest` → inner `Subscription` record (`customer_id`, `product_handle`/`product_id`, `subscription` wrapper with billing params; wire names in `Models/CreateSubscriptionRequest.cs`) | `SubscriptionResponse` (wraps `Subscription`) | **Case A** — `SdkException<CreateSubscriptionError>` (`TryGet…` accessors per `Errors/CreateSubscriptionError.cs`; confirm via `map/operations/Subscriptions.md`) | `map/operations/Subscriptions.md` |
| `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` | **Case B** (`RawError`) | `map/operations/Subscriptions.md` |

---

## 3. Trap notes (load skills, don't resolve inline)

- **Step 2 (client registration)** — `RetryOptions` members are `required`; `RetryOptions.Default()` exists but the floor `MaxRetries = 1` is enforced at construction (`0` rejected). `Timeout` is per-attempt, not total call time. The `HttpClient` passed to `MaxioAdvancedBillingClient` must be long-lived (reuse via `IHttpClientFactory`); do not build per-request. **MUST load `dotnet-client-initialization`**.
- **Step 2 (auth)** — Basic auth: `Username` = API key, `Password` = literal `"x"`. Set on `options.BasicAuth` before constructing `client` or in DI callback; never hard-code in call code. **MUST load `dotnet-authentication`**.
- **Step 3–7 (calls)** — Many optional params have no C# default; mis-binding positional args breaks. Call with named args (`productFamilyId: ...`, `ct: ...`). The cancellation token is named `ct`, not `cancellationToken`. **MUST load `dotnet-calling-endpoints`**.
- **Step 3–7 (requests)** — Request bodies are records with `required` members; `init`-only setters; wire names differ from C# property names (`email` vs `Email`; verify via `map/models/records-*.md` and the named `.cs` source file the map cites). Unions use factory methods; enums are `StringEnum<T>` (use `.FromValue(...)` or static members). **MUST load `dotnet-models`**.
- **Step 3–7 (errors)** — All 4 operations above are throw-only; no `Result` variants exist in this SDK. Case A (typed `SdkException<{Op}Error>`) vs Case B (`SdkException<RawError>`) differ by operation; do not assume `TryGetRawError` is always present or always sufficient. **MUST load `dotnet-error-handling`**.
- **Step 7 / retry** — `HttpMethodsToRetry` gates *status-code* retries; `POST` only retries on transport failures (`HttpRequestException`) automatically, but a `503` on `POST` is **not** resent. Non-idempotent writes (CreateCustomer, CreateSubscription) can execute more than once if transport fails. Defensive: make customer creation idempotent by pre-check; consider whether re-submission of subscription is safe. **MUST load `dotnet-configuration-resilience`**.
- **Testing** — Test seam is the `HttpClient` constructor arg; stub via `MockHttpMessageHandler` or similar per project framework. **MUST load `dotnet-testing`**.

---

## 4. REQUIRED READING (load before coding — these contain defaults, examples, and the full mechanics; this sheet only names hazards and points to them)

- `dotnet-client-initialization` — Step 2 (construction / DI / `HttpClient` lifetime)
- `dotnet-authentication` — Step 2 (`BasicAuthCredentials`, `ServerEnvironment`, site/base-url wiring)
- `dotnet-calling-endpoints` — Steps 3–7 (named args, `ct:`, envelope reads, pagination params)
- `dotnet-models` — Steps 3–7 (request records, wire names, enums, unions, `required` / `init`)
- `dotnet-error-handling` — Step 8 / all steps (Case A vs B, `SdkException<T>`, `TryGet…`, `RawError`; **mandatory for every `try/catch`**)
- `dotnet-configuration-resilience` — Step 2 / Step 7 (retry floor, timeout bounds, idempotency hazard on writes)
- `dotnet-testing` — test seam planning (before writing tests)

**Both `System.Text.Json.JsonException` directions are mandatory in every error boundary (must be read from `dotnet-error-handling`):**
- A drifted/malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization — **not** `SdkException`; an SDK-exception-only ladder lets it escape.
- A **non-2xx** body that doesn't match its operation's generated `{Operation}Error` shape throws `JsonException` *while constructing the error object*, **replacing** the `SdkException`; the HTTP status is destroyed.

---

## 5. Assumptions & Blockers

Assumptions:
- User's `eShopOnWeb` project already exists at repo root; SDK package `AsadAli.AdvancedBilling.Sdk` will be added via `dotnet add package`; namespace `MaxioAdvancedBilling` used.
- Sandbox site subdomain is `cp-exp-1`; environment is US (default) unless EU explicitly required.
- Product family handle `shoop-subscribe` / ID `3023074`; plan handles/IDs provided by user are correct in live site.
- Idempotent customer key choice (`email` vs `handle` vs `reference`) is application decision — not settled by SDK contract.
- `BaseUrl` override, if supplied, replaces `https://{site}.chargify.com` for Production; if omitted, default host is used with `Site = Subdomain`.

Blockers (none resolving to a missing map contract for the in-scope operations — all four operation groups have rows):
- **UNVERIFIED — live-wire payload match:** Whether `ReadProductFamily(int id)` really accepts `handle:my-family` through an `int` parameter (map Notes say yes; parameter type is `int`). Defensive: use int `3023074`; verify handle form separately if needed.
- **UNVERIFIED — subscription creation payload:** The exact required fields inside `CreateSubscriptionRequest.Subscription` for this site's billing rules (trial, payment profile, etc.) are not in the map (map carries only envelope shape). Implementer must load `dotnet-models`, read `map/models/records-2-Cr-Ne.md` / `Models/CreateSubscription.cs`, and confirm with live traffic or site docs.
- No map page defines a direct "find customer by email only" endpoint — `ListCustomers(q: ...)` is the contract path; if that returns multiple results, selection logic is **YOUR CALL — not in the map**.

---

## 6. Source citations for every contract row (map pages, not clone paths)

- Client / auth / server: `sdk-map.md` (lines 24–225)
- ProductFamilies: `map/operations/ProductFamilies.md`
- Products: `map/operations/Products.md`
- Customers: `map/operations/Customers.md`
- Subscriptions: `map/operations/Subscriptions.md`
- Request/response model wire names: `map/models/records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md` (per operation's named `.cs` source file in the row)
- Enums / unions: `map/models/enums.md`, `map/models/unions.md`
- Errors (typed accessors): `map/operations/*.md` error-accessor cells; source files named `Errors/*Error.cs`

Clone path never appears here (rule: clone stays in system temp, invisible to main agent; never written to `maxio-plan.md`).
