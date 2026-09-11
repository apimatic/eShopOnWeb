# Maxio Advanced Billing — recurring-subscription integration plan

> Path: `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-005\repo\maxio-plan.md`
> Agent: maxio-sdk · Mode: Plan (no project edits made)
> Grounded in: `maxio-getting-started/sdk-map.md`, `map/operations/{Customers,Subscriptions,Products,ProductFamilies}.md`, `map/models/records-*.md`, companion `dotnet-*` skills.

---

## 1. Scope & sequence

1. **Config / env / client registration** — bind `Maxio:` section; build `MaxioAdvancedBillingClient` with Basic auth; register via DI; wire `HttpClient` long-lived (`dotnet-client-initialization`).
2. **Service layer** — `IMaxioBillingService` + `MaxioBillingService` (idempotent customer ensure, plan lookup, subscription create/find, subscription detail, list my-subscriptions by caller identity from JWT).
3. **PublicApi endpoints** (`src/PublicApi/`):
   - `GET /api/subscription-plans` — list plans (products in family) via `client.Products.ListProducts` / `client.ProductFamilies`.
   - `POST /api/subscriptions` — subscribe idempotently (ensure customer then subscribe; return existing if already subscribed to plan).
   - `GET /api/my-subscriptions` — JWT-authenticated; caller identity from token; list customer's subscriptions via `client.Customers.ListCustomerSubscriptions` + read details.
4. **Error boundary** — `try/catch` around SDK calls; read `SdkException<T>` accessors (`dotnet-error-handling`).
5. **Verification** — build; `DOTNET_ROLL_FORWARD=Major`; `rollForward: Major` in csproj; in-memory DB; bind ports; `UseShellExecute=true`; run endpoint-level tests.

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Client / auth / server (map: `sdk-map.md` §Getting a client / Servers & auth)

| Item | Declaration (map-cited) | Namespace |
|---|---|---|
| Client | `new MaxioAdvancedBillingClient(httpClient, options)` | `MaxioAdvancedBilling` |
| Options | `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` |
| Basic auth credentials | `BasicAuthCredentials { Username = "<key>", Password = "x" }` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| Server env | `ServerEnvironment.Us` (default), `ServerEnvironment.Eu` | `MaxioAdvancedBilling.Servers` |
| Server override | `options.Server.Production.Us.BaseUrl` / `.Site` | `MaxioAdvancedBilling.Servers` (from `ServerOptions`) |
| Retry options | `RetryOptions` (all members `required`; start from `.Default()`) | `MaxioAdvancedBilling.Core.Configuration` |

### 2.2 Operations used (map pages cited per row)

| controller · source file | Method (exact params, must-pass explicit) | Request model (fields: `Name (wire): Type, required?`) | Response envelope | Error case + accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Customers` (`Api/Customers.cs`) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → explicit | `CreateCustomerRequest` — fields from `map/models/records-*.md` (e.g. `email`, `first_name`, `last_name`, `reference`, `organization`, etc.); required flags per model | `CustomerResponse` with inner `Customer` (`Customer?`) | Case A: `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError` | none | `map/operations/Customers.md` |
| `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 params nullable no-default (pass `null` to skip) | — | `IReadOnlyList<CustomerResponse>` | Case B: `SdkException<RawError>` — `.StatusCode`, `.ReadAsString()` | manual `page`+`perPage` (defaults 1/50) | `map/operations/Customers.md` |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | — | `CustomerResponse` (inner `Customer`) | Case B: `SdkException<RawError>` | none | `map/operations/Customers.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | — | `IReadOnlyList<SubscriptionResponse>` | Case B: `SdkException<RawError>` | none | `map/operations/Customers.md` |
| `client.Subscriptions` (`Api/Subscriptions.cs`) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` explicit | `CreateSubscriptionRequest` — `product_id` or `product_handle`; `product_price_point_handle`/`id`; `customer_id`/`customer_reference`; `customer_attributes`; `payment_profile_id`; see notes | `SubscriptionResponse` (inner `Subscription`) | Case A: `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` | none | `map/operations/Subscriptions.md` |
| `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params nullable no-default | — | `IReadOnlyList<SubscriptionResponse>` | Case B: `SdkException<RawError>` | manual `page`+`perPage` | `map/operations/Subscriptions.md` |
| `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` explicit | — | `SubscriptionResponse` | Case B: `SdkException<RawError>` | none | `map/operations/Subscriptions.md` |
| `client.Products` (`Api/Products.cs`) | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | — | `IReadOnlyList<ProductResponse>` | Case B: `SdkException<RawError>` | manual `page`+`perPage` | `map/operations/Products.md` |
| `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | — | `ProductResponse` (inner `Product`) | Case B: `SdkException<RawError>` | none | `map/operations/Products.md` |
| `client.ProductFamilies` (`Api/ProductFamilies.cs`) | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | — | `IReadOnlyList<ProductFamilyResponse>` | Case B: `SdkException<RawError>` | none | `map/operations/ProductFamilies.md` |
| `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` — can also use `handle:my-family` format (map note) | — | `ProductFamilyResponse` | Case B: `SdkException<RawError>` | none | `map/operations/ProductFamilies.md` |

Notes on response envelopes (verified from `map/models/records-4-Su-We.md` line 66): `SubscriptionResponse` has exactly one payload field `Subscription (subscription): Subscription?`. Equivalent pattern holds for `CustomerResponse` / `Customer`, `ProductResponse` / `Product`. All reads go one level down (`resp.Subscription`).

### 2.3 Config binding (exact section/name — YOUR CALL on app wiring, not SDK)

Bind from config section key `"Maxio:"` (the colon is the section delimiter):

| Config key (exact) | Source | Default / derivation | Notes |
|---|---|---|---|
| `Maxio:ApiKey` | env `MAXIO_API_KEY` | required | Basic auth `Username` |
| `Maxio:Subdomain` | env `MAXIO_SITE_SUBDOMAIN` | required | site token for base URL |
| `Maxio:ProductFamilyHandle` | env `MAXIO_DEFAULT_PRODUCT_FAMILY` | required | used to locate family / products (`eship-subscribe` per brief) |
| `Maxio:BaseUrl` | optional override | if non-empty, verbatim (`options.Server.Production.Us.BaseUrl = value`; if not set, derive `https://{Subdomain}.chargify.com` from `Subdomain` + `ServerEnvironment.Us`) | Must never hard-code; when set, use verbatim |

Never hard-code values; derive subdomain-host URL when `BaseUrl` absent. `BaseUrl` is an override point on `ServerOptions` — not a direct `ClientOptions` property.

### 2.4 Environment / runtime

- `rollForward: "Major"` in `.csproj` (`<RollForward>Major</RollForward>`).
- `DOTNET_ROLL_FORWARD=Major` env at build/run.
- In-memory DB: `UseOnlyInMemoryDatabase = true`; no LocalDB.
- Ports: bind in `Properties/launchSettings.json` (or `appsettings`-derived) — YOUR CALL on port numbers, but must be bound.
- Process detach: `UseShellExecute = true` (prevents blocking parent when launching).
- SDK package: `AsadAli.AdvancedBilling.Sdk`; namespace `MaxioAdvancedBilling`.

---

## 3. Architecture (service layer + endpoints)

```
src/PublicApi/
  SubscriptionPlanEndpoints.cs   // GET /api/subscription-plans
  SubscriptionEndpoints.cs       // POST /api/subscriptions (idempotent assume + subscribe)
  MySubscriptionEndpoints.cs     // GET /api/my-subscriptions (JWT caller)

src/Infrastructure/Maxio/ (YOUR CALL — file names not mandated)
  IMaxioBillingService.cs
  MaxioBillingService.cs        // wraps SDK; idempotent customer + subscribe
  MaxioClientFactory.cs         // builds MaxioAdvancedBillingClient from IOptions<MaxioSettings>

settings / config:
  MaxioSettings.cs              // binds "Maxio:" section exactly
```

Service responsibilities (not application design — SDK contracts only):
- **EnsureCustomerAsync(email or handle)** — try `ReadCustomerByReference` (reference = user's persistent handle / email-derived) or `ListCustomers(q=email)`. If found, return `CustomerResponse.Customer.Id`; else create via `CreateCustomer` with `reference` set to user's handle (idempotency anchor). **Important:** reference must be unique per notes (`CreateCustomer` — only one customer per reference).
- **SubscribeIdempotentAsync(customerId, productHandle)** — call `ListCustomerSubscriptions(customerId)`, filter by `Subscription.Product.Id` or handle match; if exists, return existing `SubscriptionResponse`; else `CreateSubscription` with `customer_id` + `product_handle`. Never double-subscribe.
- **ListPlansAsync(familyHandle)** — resolve family via `ReadProductFamily` (if needed) or `ListProductFamilies`; list products in family via `ListProductsForProductFamily` or `ListProducts` + filter; return DTOs (not SDK models directly — application design).
- **GetSubscriptionDetailAsync(subId)** — `ReadSubscription(subId)`; return DTO.
- **ListMySubscriptionsAsync(customerId)** — `ListCustomerSubscriptions(customerId)`; for each, optionally `ReadSubscription` for full detail.

---

## 4. Idempotency strategy (contract-driven)

- **Customer**: use `reference` (the application's user ID / email-derived handle) as idempotency key. First call `ReadCustomerByReference(reference)`; if 404 (`TryGetNoContent` / `RawError.StatusCode == 404`), create with same `reference`. This prevents double-creation because `CreateCustomer` validates uniqueness of `reference`. **UNVERIFIED**: live wire acceptance of `reference` as search key on lookup — defensive: if lookup fails for any reason, fall back to `ListCustomers(q=reference/or email)` and match by email/handle; only create when zero match.
- **Subscription**: before `CreateSubscription`, call `ListCustomerSubscriptions(customerId)`; filter responses (`SubscriptionResponse.Subscription`) by product handle / ID. If any match target plan, return existing (do not call create). **UNVERIFIED**: whether live payload always includes product handle in list; defensive: extract best-effort from `Subscription.Product.Id`/handle, else fall back to comparing `Subscription.Product.Id` via `ReadSubscription`; if ambiguous, prefer returning a generic message rather than creating duplicate.
- **Plan listing**: no mutation — safe to call repeatedly.

---

## 5. Auth / setup (map-cited)

- Basic auth: `BasicAuthCredentials { Username = options.ApiKey, Password = "x" }` (literal `"x"`). Source: `sdk-map.md` §Getting a client.
- Set before `new MaxioAdvancedBillingClient(...)`.
- Client constructed with long-lived `HttpClient` (register via `IHttpClientFactory` / `AddHttpClient` / `IServiceCollection` extensions — load `dotnet-client-initialization`). `HttpClient` not rebuilt per request.
- DI registration option: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ...; o.Environment = ServerEnvironment.Us; o.Server = ...; })`. Source: `ServiceCollectionExtensions.cs` per `sdk-map.md`.
- `ServerOptions`: to override base URL from config, set `options.Server.Production.Us.BaseUrl = settings.BaseUrl` when non-empty; else leave default (`https://{site}.chargify.com`) and set `options.Server.Production.Us.Site = settings.Subdomain`.

---

## 6. Config / settings binding (exact keys)

```csharp
public class MaxioSettings
{
    public string ApiKey { get; set; } = null!;          // binds Maxio:ApiKey
    public string Subdomain { get; set; } = null!;       // binds Maxio:Subdomain
    public string ProductFamilyHandle { get; set; } = null!; // binds Maxio:ProductFamilyHandle
    public string? BaseUrl { get; set; }                 // binds Maxio:BaseUrl (optional)
}
```

Binding in `Program.cs` / `Startup.cs`: `services.Configure<MaxioSettings>(Configuration.GetSection("Maxio"));`. Never invent a different section name.

---

## 7. Trap notes (load companion skills — must open before coding)

> ⚠ Step 1 (client registration / DI) — `HttpClient` must be long-lived; SDK client's `HttpClient` constructor is the test seam. Retry `Timeout` is per-attempt, not total call; `MaxRetries` floor is 1 (0 rejected). **MUST load `dotnet-client-initialization`** before wiring DI.
>
> ⚠ Step 2 (auth) — Basic password is literal `"x"`; username = API key (not app user). Config key `ApiKey` must come from `MAXIO_API_KEY` env; never hard-code in source. **MUST load `dotnet-authentication`**.
>
> ⚠ Step 3 (calling endpoints / request bodies) — many optional params have no C# default and must be passed explicitly (e.g., `CreateSubscription` `body`; `ListSubscriptions` 14 params). Call with named args (`ct: ct`). Response envelopes wrap payload (one field) — read one level down. **MUST load `dotnet-calling-endpoints`**.
>
> ⚠ Step 4 (models / enums) — enums are `StringEnum<T>` (build via `.FromValue("wire")` or static members); unions use factory + `TryGet…`; `required` properties must be initialized. Any unmodeled JSON fields dropped on deserialize — defensive extract only known fields for idempotency checks. **MUST load `dotnet-models`**.
>
> ⚠ Step 5 (error boundary) — operations use Case A (`SdkException<OpError>`) or Case B (`SdkException<RawError>`) per map row; never assume. `JsonException` from a malformed 2xx body (missing required member) reaches boundary as deserialization failure, not `SdkException`. A non-2xx body that mismatches `{Op}Error` throws `JsonException` while constructing the error, replacing `SdkException` — status destroyed. Boundary must handle both directions defensively (extract best-effort, fall back to generic message). **MUST load `dotnet-error-handling`**.
>
> ⚠ Step 6 (idempotency / list filtering) — `ListSubscriptions` and `ListCustomerSubscriptions` return arrays; filtering by product handle is application logic, not SDK guarantee. If payload lacking handle, fall back to `ReadSubscription` per item. **UNVERIFIED**: live wire always carries product handle in list response. Defensive: best-effort match, else fall back to generic message.
>
> ⚠ Step 7 (resilience / retries) — `HttpMethodsToRetry` gates status-triggered retry only; transport failures (`HttpRequestException`) retry on all verbs including `POST`, so non-idempotent writes can execute >1 time. No setting disables retries (`MaxRetries = 0` rejected). `Timeout` per-attempt. **MUST load `dotnet-configuration-resilience`** before tuning retries.
>
> ⚠ Step 8 (tests) — fake via `HttpClient` constructor seam; match existing framework (likely xUnit/NUnit in eShopOnWeb). **MUST load `dotnet-testing`**.

---

## 8. REQUIRED READING (load before implementation starts — not resolved inline)

- `dotnet-client-initialization` — Step 1 (client/DI/HttClient ownership)
- `dotnet-authentication` — Step 2 (Basic auth; key from config/env)
- `dotnet-calling-endpoints` — Step 3 (parameter names, named args, envelopes, pagination)
- `dotnet-models` — Step 4 (enums, unions, required/init, wire names)
- `dotnet-error-handling` — Step 5 (Case A/B accessors, `RawError`, `JsonException` boundary — mandatory both hazard rows from skill)
- `dotnet-configuration-resilience` — Step 7 (retries bound, timeout per-attempt, base URL override)
- `dotnet-testing` — Step 8 (fake seam, assertion style)
- `integrate-maxio` — binding gates (plan-file path equality, no edits until plan read, map boundary)

These skills carry defaults, worked examples, and what remains unwired — the sheet deliberately does not restate their contents.

---

## 9. Assumptions & Blockers

- **Assumption (YOUR CALL)**: endpoint route names `/api/subscription-plans`, `/api/subscriptions`, `/api/my-subscriptions` are application design; SDK paths are `/subscriptions.json`, `/products.json`, etc. No conflict.
- **Assumption (YOUR CALL)**: JWT authentication mechanism (middleware / `Authorize` attribute / token validation) is pre-existing in `PublicApi`; caller identity extracted from `sub` / `email` claim — not defined by SDK.
- **Assumption (YOUR CALL)**: `MaxioSettings` class name and `Program.cs` / `Startup.cs` binding syntax are application conventions; must match project style.
- **Assumption (YOUR CALL)**: file layout inside `src/Infrastructure/Maxio/` and endpoint file names; not mandated by SDK.
- **Blocker — NONE identified in plan**; however: product / plan IDs in sandbox are stated *unstable* in brief (`Product Family handle eshop-subscribe id unstable`; `Pro Plan handle eshop-pro id unstable`; `Basic Plan handle basic-plan`; components). Must rely on handles (`product_handle`) rather than numeric IDs in `CreateSubscription`, and must resolve `ReadProductByHandle` / list by handle. If live handle differs from brief, subscription creation will 422. **Defensive directive**: always resolve plan by handle first; if handle lookup 404, return error rather than creating with guessed ID.
- **Blocker — UNVERIFIED (live traffic)**: whether `SubscriptionResponse` in `ListCustomerSubscriptions` always includes `Product` reference with handle; defensive code extracts best-effort and falls back to generic message if missing.
- **Blocker — UNVERIFIED (live traffic)**: whether `reference` lookup (`ReadCustomerByReference`) is accepted for email-derived handles; defensive: try lookup, on any failure fall back to `ListCustomers(q=...)` and match, create only on zero matches.

---

## 10. Verification steps (do not edit source until after reading this plan)

1. Confirm package added: `dotnet add package AsadAli.AdvancedBilling.Sdk`.
2. Confirm `rollForward: Major` in `.csproj`; confirm `DOTNET_ROLL_FORWARD=Major` in env / `launchSettings`.
3. Confirm config section `"Maxio:"` present with required keys mapped from env; no hard-coded key.
4. Confirm `UseOnlyInMemoryDatabase = true`; confirm `UseShellExecute = true`; confirm port bindings.
5. Build: `dotnet build`. If `CS0103`/`CS0246` on SDK names — check `using MaxioAdvancedBilling.Models.Enums;` etc. (namespaces spread across child namespaces, not transitive).
6. Run endpoint smoke: call `GET /api/subscription-plans` → expect plan list from SDK via `Products`/`ProductFamilies`; confirm no 401/403 (auth configured with key from env).
7. Idempotency check: two rapid `POST /api/subscriptions` with same user + plan → second returns existing (no new `SubscriptionResponse` with new `id`). If it creates duplicate, the defensive fallback (list-first filter) failed — fix filter logic.
8. Error-boundary check: inject bad payload / force 422 → confirm catch handles `SdkException<CreateSubscriptionError>` accessors correctly; confirm `JsonException` from bad body does not leak unhandled.

---

## Source references (map pages only — no clone path exposed)

- Client/auth/servers: `maxio-getting-started/sdk-map.md`
- Customers: `map/operations/Customers.md`
- Subscriptions: `map/operations/Subscriptions.md`
- Products / ProductFamilies: `map/operations/Products.md`, `map/operations/ProductFamilies.md`
- Response envelope `SubscriptionResponse`: `map/models/records-4-Su-We.md`
- Namespaces / model groups: `map/models/records-1-Ac-Cr.md` through `records-4-Su-We.md`; `map/models/enums.md`; `map/models/unions.md`
- Error core / case definitions: `sdk-map.md` §Error-handling model

*No SDK source clone path appears in this file; clone (if needed on real gap) stays in system temp per `maxio-getting-started` §SDK source and is never named here.*
