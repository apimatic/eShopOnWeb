# Maxio Advanced Billing .NET SDK — integration plan (eShopOnWeb subscription billing)

> Plan mode output — file path dictated by brief (`repo/maxio-plan.md` default, brief specified exact absolute path, writing to that path). No project files edited; build / code left to implementer.

**Confirmation of SDK identity (map-verified, not memory):** package `AsadAli.AdvancedBilling.Sdk`; root namespace `MaxioAdvancedBilling`; client `MaxioAdvancedBillingClient`; auth HTTP Basic (`Username` = API key, `Password` = literal `"x"`); source commit `v1.0.2` (`15db14b`). Target environment: sandbox `cp-exp-1` (US-hosted `ServerEnvironment.Us` is default; base URL overridden to sandbox via `Server.Production.Us.BaseUrl`). Source: `sdk-map.md` lines 9–13, 201–223.

---

## 1. Scope & sequence

Implement 3 HTTP endpoints on `src/PublicApi` (existing JWT auth — SDK auth is independent):
- `GET /api/subscription-plans` — lookup family `eshop-subscribe` / family id `3023074`; return plan handles/ids `eshop-pro` (`7126957`) and `basic-plan` (`7126958`) with price/state/next-billing-date from product/plan lookup.
- `POST /api/subscriptions` — idempotent customer create/find by reference (mapped from eShopOnWeb user), then `CreateSubscription`; return subscription state / plan / price / next billing.
- `GET /api/my-subscriptions` — find current user’s subscriptions (`ListSubscriptions` filtered by customer reference / `customer_id`, or `FindSubscription` if reference set on sub); return plan/state/next billing.

Sequence per endpoint: client init (Step 1) → auth/config (Step 2) → family/plan lookup (Step 3) → customer idempotency (Step 4) → subscription create/find (Step 5) → response map (Step 6) → error boundary (Step 7).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Operations (map pages cited)

| Controller (`client.X`) | Method signature (param order, nullable flags, `ct`) | Source map page | Source file (map-named) |
|---|---|---|---|
| `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, …, CancellationToken ct = default)` — 5 nullable no-default (pass `null`) | `map/operations/ProductFamilies.md` | `Api/ProductFamilies.cs` |
| `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` | `map/operations/ProductFamilies.md` | `Api/ProductFamilies.cs` |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, …, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 nullable + 2 defaults | `map/operations/ProductFamilies.md` | `Api/ProductFamilies.cs` |
| `client.Products` | `ReadProduct(int productId, CancellationToken ct = default)` | `map/operations/Products.md` | `Api/Products.cs` |
| `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `map/operations/Products.md` | `Api/Products.cs` |
| `client.Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, …, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `map/operations/Products.md` | `Api/Products.cs` |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — Case B (`SdkException<RawError>`) | `map/operations/Customers.md` | `Api/Customers.cs` |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — Case A; accessors `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | `map/operations/Customers.md` | `Api/Customers.cs` |
| `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — Case B; pagination manual `page`/`perPage` | `map/operations/Customers.md` | `Api/Customers.cs` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — Case A; accessors `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | `map/operations/Subscriptions.md` | `Api/Subscriptions.cs` |
| `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` — Case A; accessors `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` | `map/operations/Subscriptions.md` | `Api/Subscriptions.cs` |
| `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — Case B; pagination manual | `map/operations/Subscriptions.md` | `Api/Subscriptions.cs` |
| `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — Case B | `map/operations/Subscriptions.md` | `Api/Subscriptions.cs` |

### 2.2 Request / response envelope shapes (wire names from map records pages)

- `CreateCustomerRequest` — namespace `MaxioAdvancedBilling.Models`; fields include `reference (reference): string?` (unique id from app), `email (email): string?`, `first_name (first_name): string?`, `last_name (last_name): string?`, `organization (organization): string?`, `country (country): string?`, etc. Source: `map/models/records-2-Cr-Ne.md` (index entry); file `Models/CreateCustomerRequest.cs`. **Notes** (map): only validation = one customer per `reference`; `reference` must be unique if provided; country = 2-char ISO.
- `CreateSubscriptionRequest` — namespace `MaxioAdvancedBilling.Models`; fields include `product_id (product_id): int?`, `product_handle (product_handle): string?`, `product_price_point_id (product_price_point_id): int?`, `product_price_point_handle (product_price_point_handle): string?`, `customer_id (customer_id): int?`, `customer_reference (customer_reference): string?`, `subscription_reference (subscription_reference): string?`, `payment_profile_id (payment_profile_id): int?`, plus `customer_attributes (customer_attributes): CreateCustomerRequest?` (embed new customer). Source: `map/models/records-2-Cr-Ne.md`; file `Models/CreateSubscriptionRequest.cs`.
- `CustomerResponse` — envelope field `customer: Customer`; inner `Customer` record with `id (id): int`, `reference (reference): string?`, `email (email): string?`, `first_name (first_name): string?`, etc. Source: records pages / `map/operations/Customers.md` (returns `CustomerResponse`).
- `SubscriptionResponse` — envelope `subscription: Subscription`; inner fields `id`, `state (state): string`, `product_id`, `product_handle`, `plan_handle`, `price_in_cents`, `next_billing_at`, `current_period_started_at`, `customer_id`, `customer_reference`. Source: `map/operations/Subscriptions.md` (returns `SubscriptionResponse`); model in `map/models/records-3-Of-Su.md` / `records-4-Su-We.md`.
- `ProductFamilyResponse` — `product_family: ProductFamily`; inner `id`, `handle (handle): string`, `name`. Source: `map/operations/ProductFamilies.md`.
- `ProductResponse` — `product: Product`; inner `id`, `family_id`, `handle (handle): string`, `name`, `price_in_cents` / price-point info. Source: `map/operations/Products.md`.

### 2.3 Error accessors per operation (contract to handle)

- `CreateCustomer` → `SdkException<CreateCustomerError>` (Case A). `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` (422) + `TryGetRawError(out RawError)`.
- `ReadCustomerByReference` → `SdkException<RawError>` (Case B). Use `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`.
- `CreateSubscription` → `SdkException<CreateSubscriptionError>` (Case A). `TryGetErrorListResponse1(out ErrorListResponse1)` (422) + `TryGetRawError`.
- `FindSubscription` → `SdkException<FindSubscriptionError>` (Case A). `TryGetNoContent(out RawError)` (404) + `TryGetRawError`.
- `ListSubscriptions` / `ListCustomers` → `SdkException<RawError>` (Case B) — pagination manual via `page`/`perPage`. No typed accessors.
- `ReadSubscription` → `SdkException<RawError>` (Case B).

---

## 3. File layout / build order

Target project: `src/PublicApi` (existing ASP.NET Core / minimal-API-style public API with JWT auth; SDK is additional dependency, no existing SDK code). Order:

1. **Add package** → `dotnet add src/PublicApi/PublicApi.csproj package AsadAli.AdvancedBilling.Sdk` (package id, not namespace). No project reference to clone.
2. **Configuration / binding** (`appsettings.{env}.json` + env vars) — keys defined below (§4); load via `IConfiguration` / `IOptions<MaxioOptions>`.
3. **Client registration** (`Program.cs` / `Startup.cs`) — `services.AddMaxioAdvancedBillingClient(...)` or manual `new MaxioAdvancedBillingClient(httpClient, options)` with long-lived `HttpClient` via `IHttpClientFactory`. Order: auth set before construction.
4. **Contract / service layer** new folder `src/PublicApi/Services/Maxio/` (or `Billing/`) — `IMaxioBillingService` + impl: `GetSubscriptionPlansAsync`, `CreateSubscriptionAsync`, `GetMySubscriptionsAsync`. Keeps endpoints thin.
5. **Endpoint controllers / minimal endpoints** in `src/PublicApi/Controllers/SubscriptionBillingController.cs` (or endpoint group) — call service, return DTOs (not raw SDK responses — decouple).
6. **Error boundary / middleware** — handle both `SdkException<T>` cases and `System.Text.Json.JsonException` (see §6 / required reading). Do not map all `JsonException` to 5xx blindly.
7. **Tests** — `dotnet test` on touched tests if they exist (not required for plan, but listed in required reading).

No edits to existing project files except `PublicApi.csproj` package add and `Program.cs`/`Startup.cs` DI; new files only.

---

## 4. Auth / config binding (env → SDK)

Binding keys (from brief + SDK contract):

| Config key / env var | SDK property / usage | Default / note | Source |
|---|---|---|---|
| `Maxio:ApiKey` (env `MAXIO_API_KEY`) | `BasicAuth.Username` | none required; password always `"x"` | `sdk-map.md` 201; `dotnet-authentication` |
| `Subdomain` (env / config `Maxio:Subdomain`) | `options.Server.Production.Us.Site = subdomain` (or full `BaseUrl` override) | no default site; must set | `sdk-map.md` 220 |
| `BaseUrl` (env `MAXIO_BASE_URL`, config `Maxio:BaseUrl`) | `options.Server.Production.Us.BaseUrl = baseUrl` (sandbox `cp-exp-1` override) | `https://{site}.chargify.com` if unset | `sdk-map.md` 217–223 |
| `ProductFamilyHandle` (env / config `Maxio:ProductFamilyHandle`) | application-level key; used to call `ReadProductFamily` by handle or `ListProductFamilies` then filter | brief specifies `eshop-subscribe`; family id `3023074` is verified value — use handle for lookup, id for `ListProductsForProductFamily` param | `map/operations/ProductFamilies.md` (handle format `handle:my-family`) |
| `Maxio:Environment` | `options.Environment = ServerEnvironment.Us` (default) or `.Eu` | `ServerEnvironment.Us` | `sdk-map.md` 206–211 |

Namespace notes for `using`:
- `MaxioAdvancedBilling` (client, options)
- `MaxioAdvancedBilling.Core.Authentication.Basic` (`BasicAuthCredentials`)
- `MaxioAdvancedBilling.Servers` (`ServerEnvironment`; server overrides via `ServerOptions` / `ProductionOptions` — names from `sdk-map.md` 215–223)
- `MaxioAdvancedBilling.Core.Configuration` (`RetryOptions`, `RetryOptions.Default()`) — required members, all required; do not partial-construct.
- `MaxioAdvancedBilling.Api` (controllers)
- `MaxioAdvancedBilling.Models` (requests/responses)
- `MaxioAdvancedBilling.Models.Enums` (`SubscriptionStateFilter`, `SubscriptionListInclude`, `BasicDateField`, `SortingDirection`, `SubscriptionSort`, `SubscriptionDateField` — string-based, not C# `enum`; build with `.FromValue(...)` or static members — see `map/models/enums.md` and `dotnet-models`)
- `MaxioAdvancedBilling.Errors` (typed error classes)

---

## 5. Idempotency strategy (customer + subscription)

- **Customer idempotency** — before `CreateCustomer`, call `ReadCustomerByReference(string reference)` (`client.Customers.ReadCustomerByReference`). Map reference from eShopOnWeb user id / email / stable key (application decision — `YOUR CALL — not in the map`). If 404 (`SdkException<RawError>` with `.StatusCode == HttpStatusCode.NotFound`), proceed to create; else use returned `CustomerResponse.Customer.id`. **Contract fact:** `CreateCustomer` notes (map) — “you may only create one customer for a given reference value”; `reference` is unique identifier from app. Use `reference`, not just email, for idempotency.
- **Subscription idempotency / lookup** — if user already has an active subscription (by `customer_reference` or via `ListSubscriptions` filtering by `customer_id` + `product` id / handle), return existing instead of creating duplicate. `FindSubscription(string? reference)` can look up by `subscription_reference` if set at creation; else filter `ListSubscriptions` (Case B, pagination). If duplicate-risk is unacceptable, set `subscription_reference` on `CreateSubscriptionRequest` to derived key (e.g., `eshop-{userId}-{planHandle}`) and use `FindSubscription` before create — `YOUR CALL` if that key format is adopted.
- **Create-subscription customer embedding** — if customer not found, `CreateSubscriptionRequest.customer_attributes = new CreateCustomerRequest { reference = ..., email = ... }` lets subscription creation also create customer (map notes on `CreateSubscription` say “Identify an existing customer with `customer_id` or `customer_reference` … To create a new customer, pass `customer_attributes`”). This avoids separate call but still requires idempotency check for reference uniqueness.
- **Retry / write-safety** — `RetryOptions` default retries `StatusCodesToRetry`; `HttpMethodsToRetry` gates status retries but `HttpRequestException` retries on all verbs (`POST` included). No setting disables retries (`MaxRetries = 0` rejected at construction; floor 1). Because `POST /subscriptions.json` is not idempotent by wire, a transport retry could duplicate; defend with reference-based lookup before call (`YOUR CALL` — defensive pattern, not SDK feature). Source: `dotnet-configuration-resilience` / `sdk-map.md` 66–78.

---

## 6. Required reading (must load before implementation — per `integrate-maxio` workflow)

These are the `dotnet-*` skills named by trap steps / contract needs; sheet does not carry their contents — implementer loads them:

- `dotnet-client-initialization` — Step 1 (client + `HttpClient`/factory lifetime, `MaxioAdvancedBillingClientOptions` construction, DI `AddMaxioAdvancedBillingClient`). Must load before wiring.
- `dotnet-authentication` — Step 2 (Basic auth credentials shape; `Username` = API key, `Password` = `"x"`; load from config, not hardcode).
- `dotnet-calling-endpoints` — Step 3/5 (named args required for many optional params; response envelopes read one level down: `SubscriptionResponse.Subscription`, `ProductFamilyResponse.ProductFamily`, etc.).
- `dotnet-models` — Step 3/5 (request/response construction; `StringEnum<T>` enums; union `TryGet…`; `required` init-only properties; `init` setters). Needed before building `CreateSubscriptionRequest` / `CreateCustomerRequest`.
- `dotnet-error-handling` — Step 7 / boundary (Case A / Case B mechanics; `TryGet…` accessors; `SdkException<T>`; **both** required `JsonException` caveat rows below). Must load before writing catch ladder.
- `dotnet-configuration-resilience` — Step 2/4 (retry/timeout semantics — `Timeout` is per-attempt, not total; `HttpMethodsToRetry` only status-triggered; transport retries all verbs; `MaxRetries` floor 1; `RetryOptions` all members required; server-node override points `Server.Production.Us.BaseUrl` / `.Site`).
- `dotnet-testing` — Step 7 (test seam is `HttpClient` constructor argument; match existing framework).
- `integrate-maxio` (this session’s mandate) — load first; confirms file path (`maxio-plan.md`), no project edits during agent run, map boundary, and the 5 binding gates.

---

### Required reading — mandatory `dotnet-error-handling` boundary caveats (verbatim, from instruction — these two directions must both be handled, not collapsed)

> - A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
> - A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. Defensive directive: extract best-effort from `SdkException<T>` accessors; for any `JsonException` at boundary, log, fall back to generic message with HTTP status preserved from caller context (do not invent a status from exception alone), do not auto-retry 5xx that came from deserialization failures. Label uncertainty `UNVERIFIED` only for live-wire payload match (not needed for this plan; contract is from map + source).

---

## 7. Assumptions & Blockers

- **Assumption** (`YOUR CALL`): `reference` mapping from eShopOnWeb user is the implementer’s identity path (e.g., `user.Id.ToString()` or `user.Email`). Not in SDK map; must be decided by implementer.
- **Assumption** (`YOUR CALL`): endpoint route shape (`/api/subscription-plans`, etc.) is implementer’s design; SDK provides operations, not URLs.
- **Assumption** (`YOUR CALL`): response DTOs (plan/price/state/next-billing-date) are application-level projections from `SubscriptionResponse.Subscription` / `ProductResponse.Product`; exact projection fields not in SDK.
- **Blocker / verification needed** (`UNVERIFIED` — only live traffic can confirm): whether the live wire payload for `CreateSubscription` / `CustomerResponse` exactly matches the generated model fields documented in map (e.g., whether `subscription_reference` is returned, whether `price_in_cents` vs `price` is used). Plan directive: extract best-effort from mapped fields (`id`, `state`, `product_handle`, `next_billing_at`, `plan_handle`), fall back to generic message if field missing.
- **No blocker on SDK availability** — package `AsadAli.AdvancedBilling.Sdk` exists (NuGet); namespace `MaxioAdvancedBilling`; auth Basic confirmed; environment `ServerEnvironment.Us` default, overridden to sandbox via `BaseUrl`; no missing operations (customers find/create, subscriptions create/find, product families/plans lookup all present in map).
- **No clone path appears here** — per rule, clone stays in temp, never in `maxio-plan.md` / replies. Source consulted only via map (primary) and named files (`Api/Customers.cs`, etc.) only if gap — none needed for this plan.

---

## 8. Verified facts cited from map (not from training memory)

- Package id and namespace: `sdk-map.md` 12, 15, 189.
- Client class / options / Basic auth / `ServerEnvironment`: `sdk-map.md` 26–38, 199–223; `map/operations/Customers.md` / `Subscriptions.md` / `ProductFamilies.md` header rows.
- Operation signatures, return envelopes, error cases (A/B) with exact `TryGet…` names and status mappings: `map/operations/Customers.md` (CreateCustomer 422 `CustomerErrorResponse1`); `Subscriptions.md` (CreateSubscription 422 `ErrorListResponse1`; FindSubscription 404 `NoContent`); `ProductFamilies.md` (ReadProductFamily Case B; ListProductsForProductFamily 404 `String`).
- Request/response model names and wire names: `map/models/records-2-Cr-Ne.md` (index + `CreateSubscriptionRequest`); `map/models/records-3-Of-Su.md` / `records-4-Su-We.md`; `map/operations/*.md` envelope fields (`CustomerResponse.customer`, `SubscriptionResponse.subscription`, `ProductFamilyResponse.product_family`, `ProductResponse.product`).
- Enum/string values and construction: `map/models/enums.md`; `dotnet-models` references.
- Idempotency note (reference uniqueness): `map/operations/Customers.md` `CreateCustomer` Notes (line 9).
- Retry / timeout semantics and `MaxRetries` floor 1: `sdk-map.md` 63–78; `dotnet-configuration-resilience`.
- No `{Operation}Result` variants exist: `sdk-map.md` 118.
