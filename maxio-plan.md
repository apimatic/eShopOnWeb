# Maxio Advanced Billing — recurring-subscription billing plan (eShopOnWeb)

Path: `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-016\repo\maxio-plan.md`
Grounded in `.opencode/skills/maxio-getting-started/sdk-map.md` + `map/operations/*.md` + `map/models/*.md`. No SDK source opened (no map-side gap). Clone not needed.

---

## 1. Scope & sequence

Implement recurring-subscription billing for a logged-in eShopOnWeb user against sandbox site `cp-exp-2`, using `AsadAli.AdvancedBilling.Sdk` (`MaxioAdvancedBilling` root namespace; client `MaxioAdvancedBillingClient`; options `MaxioAdvancedBillingClientOptions`). All operations below are throw-only (no `…Result` variants exist per `sdk-map.md` §Error-handling model).

Sequence:
1. **Client + auth** — register `MaxioAdvancedBillingClient` with Basic auth (`Username` = API key, `Password` = literal `"x"`) and site subdomain (`cp-exp-2`) via `Server.Production.Us.Site`. Load `dotnet-client-initialization`, `dotnet-authentication`.
2. **Customer idempotent ensure** — look up by `reference` (`ReadCustomerByReference`) or create (`CreateCustomer`) using logged-in user's identity. Load `dotnet-calling-endpoints`, `dotnet-models`.
3. **Plan / family lookup** — `ReadProductFamily` by numeric id (family 3023074) and `ReadProductByHandle` / `ListProductsForProductFamily` for plans (pro `eshop-pro` 7126957, basic `7126958`, metered component `api-call` 3057195). Load `dotnet-models`.
4. **Subscription create + list** — `CreateSubscription` (enroll to plan by `product_handle` / `product_id` with `customer_id` or `customer_reference`), `ListSubscriptions` filtered by `customer_id` (via `ListCustomerSubscriptions` actually returns customer subscriptions, or `ListSubscriptions` with `product`), `ReadSubscription` to confirm state/plan/price/next-billing-date. Load `dotnet-calling-endpoints`, `dotnet-error-handling`.
5. **Error boundary** — catch `SdkException<CreateCustomerError>` (Case A), `SdkException<RawError>` (Case B), plus the `JsonException` dual-direction trap (see §4). Load `dotnet-error-handling`.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row. Controllers: `MaxioAdvancedBilling.Api` (property `client.Customers`, `client.Products`, `client.Subscriptions`, `client.ProductFamilies`). Requests / responses: `MaxioAdvancedBilling.Models`. Enums: `MaxioAdvancedBilling.Models.Enums`. Errors: `MaxioAdvancedBilling.Errors`. Auth: `MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials`. Config: `MaxioAdvancedBilling.Core.Configuration.RetryOptions`. Servers: `MaxioAdvancedBilling.Servers.ServerEnvironment`.

### 2.1 Client construction / server / auth

| Fact | Value / binding | Source |
|---|---|---|
| Package | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` §SDK identity |
| Client ctor | `new MaxioAdvancedBillingClient(HttpClient, MaxioAdvancedBillingClientOptions)` | `sdk-map.md` §Getting a client |
| Auth scheme | HTTP Basic | `sdk-map.md` §Auth |
| Auth property | `options.BasicAuth = new BasicAuthCredentials { Username = "<API_KEY>", Password = "x" }` | `sdk-map.md`; namespace `MaxioAdvancedBilling.Core.Authentication.Basic` |
| Env default | `ServerEnvironment.Us` | `sdk-map.md` §Servers |
| Site subdomain (sandbox) | `options.Server.Production.Us.Site = "cp-exp-2"` (or override `BaseUrl` to sandbox host if needed) | `sdk-map.md` §Servers |
| Retry / timeout | `RetryOptions` (namespace `MaxioAdvancedBilling.Core.Configuration`) — `RetryOptions.Default()`; all members `required`; `Timeout` is per-attempt not total; `HttpMethodsToRetry` gates status triggers only; `POST` retries on transport failure regardless of method list; `MaxRetries` floor is 1 (`0` rejected) | `sdk-map.md` §Getting a client; `dotnet-configuration-resilience` (must load) |

### 2.2 Operations in scope

| Controller | Op (map page) | Signature (verbatim) | Request fields (key wire names from notes / record refs) | Response envelope + inner read fields | Error case + accessors | Notes / pagination |
|---|---|---|---|---|---|---|
| `client.Customers` | `CreateCustomer` (`Customers.md` 7–16) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` (`Models/CreateCustomerRequest.cs`) — `first_name`, `last_name`, `email`, `reference` (your app's shared ID — must be unique), `organization` optional; country 2-char ISO, state 2-char (US) / 2–3 char (non-US); `locale` optional | `CustomerResponse` (`CustomerResponse` envelope) — inner `Customer` model: `id`, `email`, `reference`, `first_name`, `last_name`, `organization`, etc. | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` fallback | `reference` must be unique per site; can co-create with subscription via `customer_attributes` inside `CreateSubscriptionRequest`. No pagination. |
| `client.Customers` | `ReadCustomerByReference` (`Customers.md` 61–69) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query `reference` (C# param `reference`) | `CustomerResponse` (inner `Customer`) | **Case B** `SdkException<RawError>` — `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()` | Lookup by app reference (idempotent ensure path). No pagination. |
| `client.Customers` | `ListCustomers` (`Customers.md` 38–49) | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` | Query params; `q` = search (email, ref, name, org, id) | `IReadOnlyList<CustomerResponse>` | **Case B** `SdkException<RawError>` | 7 nullable params before `page` must be passed explicitly (pass `null` to skip). Manual pagination `page`/`perPage`. |
| `client.Customers` | `ListCustomerSubscriptions` (`Customers.md` 28–36) | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Path `customer_id` → C# `customerId` | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | List by customer id. No pagination. |
| `client.Products` | `ReadProductByHandle` (`Products.md` 51–59) | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | Path `api_handle` → C# `apiHandle` | `ProductResponse` (inner `Product`) — `id`, `api_handle`, `name`, `product_family`, `default_price_point_id`, etc. | **Case B** `SdkException<RawError>` | Lookup plan by handle (`eshop-pro`, `7126958`, etc.). No pagination. |
| `client.Products` | `ListProducts` (`Products.md` 28–39) | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `filter` can restrict; `includeArchived`; `include` enum `ListProductsInclude` | `IReadOnlyList<ProductResponse>` | **Case B** `SdkException<RawError>` | 8 nullable params must be explicit; manual pagination. |
| `client.ProductFamilies` | `ReadProductFamily` (`ProductFamilies.md` 43–51) | `ReadProductFamily(int id, CancellationToken ct = default)` | Path `id` (numeric) | `ProductFamilyResponse` (inner `ProductFamily`) | **Case B** `SdkException<RawError>` | Notes say family can be specified by `handle:my-family`; however **signature takes `int id`** — handle-based lookup is not directly exposed by this method. Use numeric id `3023074`. **UNVERIFIED** whether live wire accepts `handle:` in path when SDK forces `int`; defensive: try numeric id first, extract best-effort from `RawError` if 404. |
| `client.ProductFamilies` | `ListProductsForProductFamily` (`ProductFamilies.md` 30–42) | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | Path `product_family_id` (string — can be numeric or handle?) + query filters | `IReadOnlyList<ProductResponse>` | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError` fallback | 8 nullable params explicit; pagination manual. |
| `client.Subscriptions` | `CreateSubscription` (`Subscriptions.md` 31–40) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` (`Models/CreateSubscriptionRequest.cs`) — `product_handle` or `product_id`; `customer_id` or `customer_reference`; `payment_profile_id`; `customer_attributes` (nested `CreateCustomerRequest`-like) for co-creation; `reference` (subscription ref); `trial_ends_at`, `activated_at`, etc. Check map/record page for exact field list — not fully reproduced here; use the request model directly. | `SubscriptionResponse` (inner `Subscription`) — `id`, `state`, `product_id`, `product_handle`, `current_period_started_at`, `current_period_ends_at`, `next_billing_at`, `trial_ends_at`, `subscription_details` (price/sumry), etc. | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` fallback | Creates subscription; co-create customer via `customer_attributes`. No pagination. Confirm returned `SubscriptionResponse` fields for state/plan/price/next-billing-date. |
| `client.Subscriptions` | `ListSubscriptions` (`Subscriptions.md` 54–65) | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `state` (enum `SubscriptionStateFilter`), `product` (int id), `include` list | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | 14 params; most nullable must be explicit; pagination manual. Filter by `product` for plan-specific list; filter by `customer_id` is not a direct param — use `ListCustomerSubscriptions` or filter via `q`-style search if available. |
| `client.Subscriptions` | `ReadSubscription` (`Subscriptions.md` 101–111) | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | Path `subscription_id`; `include` optional (e.g. `self_service_page_token`) | `SubscriptionResponse` (inner `Subscription`) | **Case B** `SdkException<RawError>` | Confirm state/price/next-billing-date from response. No pagination. |

### 2.3 Key request/response model notes (not full dumps)

- `CreateSubscriptionRequest` / `UpdateSubscriptionRequest` / `CustomerResponse` / `SubscriptionResponse` / `ProductResponse` / `ProductFamilyResponse` live in `MaxioAdvancedBilling.Models`. All records are immutable `record` with `init`-only setters; `required` members must be set; `?` = nullable.
- `SubscriptionResponse` is an envelope: read `Subscription` property (the inner payload). Same pattern for `CustomerResponse` (`Customer`), `ProductResponse` (`Product`), `ProductFamilyResponse` (`ProductFamily`). The map does not specify whether envelope property is named exactly as inner type; confirm via `Models/` source file the map names if a compile error on `.Subscription` / `.Customer` / `.Product` occurs. Per `sdk-map.md` §Response envelopes, reads go one level down.
- Enums needed: `SubscriptionStateFilter` (for list filter), `SubscriptionState` (if reading state), `SubscriptionListInclude`, `SortingDirection`, `ListProductsInclude`, `SubscriptionInclude`, `BasicDateField`. All in `MaxioAdvancedBilling.Models.Enums`; build via `.FromValue("wire")` or static members (names are literal C# identifiers, wire values different — see `map/models/enums.md`).
- Unions: `Quantity` (on allocations / components) — use factory / `TryGet…`; not expected for basic subscription creation unless using metered components.

---

## 3. Required reading (load BEFORE implementation)

- `dotnet-client-initialization` — client ctor, `HttpClient` lifetime (reuse via `IHttpClientFactory`; SDK wrapper may be transient), DI `AddMaxioAdvancedBillingClient`.
- `dotnet-authentication` — Basic auth (`Username` = key, `Password` = `"x"`); set before constructing client; load from config binding (`MAXIO_API_KEY`), never raw env string in source.
- `dotnet-calling-endpoints` — named arguments required (many nullable params have no C# default; `ct:` exact); response envelope read one level down; list/search with `q` / filters.
- `dotnet-models` — `record` init-only, `required`, wire-name mapping (`[JsonPropertyName]`), `StringEnum<T>` vs C# enums, union factories / `TryGet…`; do not assume JSON field names match C#.
- `dotnet-error-handling` — **mandatory for boundary**. Case A (`SdkException<{Op}Error>` with `TryGet…`) vs Case B (`SdkException<RawError>`). The `JsonException` dual-direction hazard (§4). Every op is throw-only; no `ApiResult`.
- `dotnet-configuration-resilience` — `RetryOptions` defaults, `Timeout` per-attempt, `HttpMethodsToRetry`, transport failure retry on `POST` (non-idempotent write risk), `MaxRetries` floor 1, no built-in logging hook.
- `dotnet-testing` — test seam is `HttpClient`; match project framework; avoid SDK-internal assertions.

---

## 4. Trap notes (named hazard + consequence + MUST load pointer)

- ⚠ Step 1 (client/auth) — `BasicAuth` property is on options, not constructor overloads; site subdomain is `Server.Production.Us.Site`, not a raw `BaseUrl` unless overriding. **MUST load `dotnet-authentication`** + `dotnet-client-initialization`.
- ⚠ Step 2 (customer ensure) — idempotent ensure by `reference` uses `ReadCustomerByReference(string reference)` (Case B); creation uses `CreateCustomer` (Case A, 422 typed). If `reference` collides on create, 422 `CustomerErrorResponse1`. **MUST load `dotnet-calling-endpoints`** + `dotnet-error-handling` before writing the ensure loop.
- ⚠ Step 3 (plan lookup by handle) — `ReadProductByHandle` takes `string apiHandle`; family lookup by handle is **not directly exposed** (only `int id` in `ReadProductFamily` — see Contract Sheet). Use numeric ids provided (`3023074`, `7126957`, `7126958`, `3057195`). **UNVERIFIED**: whether live wire accepts `handle:` in `ReadProductFamily` path despite SDK `int`; defensive: prefer numeric id, fall back to `RawError` extraction on 404. **MUST load `dotnet-models`**.
- ⚠ Step 3 (plan list for subscriber) — `ListProductsForProductFamily` requires `string productFamilyId`; `ListProducts` can filter by `ListProductsFilter`; both paginate manually (`page`/`perPage`). **MUST load `dotnet-calling-endpoints`**.
- ⚠ Step 4 (subscription create) — `CreateSubscriptionRequest` can co-create customer via `customer_attributes`; confirm that `customer_attributes.ref` is unique or reuse existing `reference`. Subscription state/price/next-billing returned inside `SubscriptionResponse.Subscription`. Read these, not assume live wire always matches generated model. **UNVERIFIED**: whether live `SubscriptionResponse` contains all documented sub-fields; defensive — read best-effort, fall back to generic message if a field is missing. **MUST load `dotnet-calling-endpoints`** + `dotnet-models`.
- ⚠ Step 5 (list my subscriptions) — `ListSubscriptions` does not take `customer_id` directly; use `ListCustomerSubscriptions(int customerId)` to get customer's subs, or filter `ListSubscriptions` by `product`. Confirm `SubscriptionResponse` list items wrap inner `Subscription`. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Boundary (error handling) — `System.Text.Json.JsonException` reaches boundary two ways: (a) drifted/malformed **2xx** body (missing `required` member) → `JsonException`, not `SdkException`; (b) **non-2xx** body that doesn't match `{Operation}Error` shape → `JsonException` replaces `SdkException`, destroying HTTP status. A boundary mapping all `JsonException` to 5xx mis-reports deterministic rejections as outages; retrying 5xx retries never-successful calls. **MUST load `dotnet-error-handling`** before writing boundary. Handle both directions explicitly.
- ⚠ Retry / idempotency — `HttpMethodsToRetry` only gates status-triggered retries; `POST` retries on transport failure (`HttpRequestException`) unconditionally. Non-idempotent `CreateSubscription` / `CreateCustomer` can execute >1 time; no setting disables (`MaxRetries` floor 1). **MUST load `dotnet-configuration-resilience`**.

---

## 5. Assumptions & Blockers

- **Assumptions (app design belongs to implementer)**:
  - Logged-in eShopOnWeb user's identity path (email / local id / reference) is resolved by the app; this plan says only to pass it as `reference` / `email` / `customer_reference`. Not in SDK map.
  - Sandbox site `cp-exp-2` exists and API key `MAXIO_API_KEY` has write access; if not, 401/403 is a config failure (check auth + `Server.Production.Us.Site` / `BaseUrl` override), not a code fix.
  - `MAXIO_DEFAULT_PRODUCT_FAMILY` env binding maps to family numeric id `3023074` (handled by app config, not SDK).
  - The plan uses numeric product handles/ids given (`eshop-pro` 7126957, basic-plan 7126958, metered `api-call` 3057195, family 3023074). If handles differ in live sandbox, lookup will 404 — fix is data update, not SDK change.
- **Blockers (nothing stops planning; no map gap)**:
  - None identified from map. All required operations (`Customers`, `Products`, `ProductFamilies`, `Subscriptions`) have map pages with signatures. No missing operation; no model footgun found (envelopes follow standard response-shape; error cases named).
  - One **contract tension** (not a blocker, but a defensive-coding directive): `ReadProductFamily` takes `int id` yet Notes mention `handle:my-family`. Treat as `UNVERIFIED` for handle-in-path; use numeric `id` and extract best-effort from `RawError` on failure (already in Trap notes).

---

*File written: `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-016\repo\maxio-plan.md`*
*No SDK source clone performed (map answered all in-scope facts); clone path never appears here or in reply per rules.*
*Every contract fact above cites a map page (`Customers.md`, `Products.md`, `ProductFamilies.md`, `Subscriptions.md`, `SubscriptionProducts.md`, `sdk-map.md`); no training-data claims used.*
