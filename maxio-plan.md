# Maxio SDK — Plan for eShopOnWeb PublicApi subscription billing

Path dictated by brief: `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-011\repo\maxio-plan.md`

## 1. Scope & sequence

Integration targets `PublicApi` (JWT-authenticated on API layer; SDK uses Basic auth with API key). In-memory DB (`UseInMemoryDatabase` / rollForward `latestMajor`; `DOTNET_ROLL_FORWARD=Major` in env / launch settings).

Sequence (each step names SDK operations from map pages):
1. **Client / DI / auth setup** — build `MaxioAdvancedBillingClient` with `BasicAuthCredentials`; load from `Maxio:` config + env (see §6). `dotnet-client-initialization`, `dotnet-authentication`.
2. **Customer idempotence** — `client.Customers.ListCustomers(q: email/reference)` then `ReadCustomerByReference`; if missing `CreateCustomer`. Source: `operations/Customers.md`.
3. **Catalog resolution (stable handles given)** — `client.ProductFamilies.ListProductFamilies` / `ReadProductFamily`; `client.Products.ListProducts`; `client.Components.ListComponents`; resolve handles `eshop-subscribe` (family id 3023074), plans `eshop-pro` (7126957) / `basic-plan` (7126958), metered `api-call` (3057195). Source: `operations/ProductFamilies.md`, `operations/Products.md`, `operations/Components.md`. Catalog handles/stable; no mutation of catalog expected.
4. **Subscription-plan list endpoint (`GET /api/subscription-plans`)** — read from resolved catalog (plan + component price-point info); no SDK mutation. Use `SubscriptionProducts` / `SubscriptionComponents` if needed for plan details.
5. **Subscription enroll (`POST /api/subscriptions`) — idempotent** — `client.Subscriptions.ListSubscriptions(customer: id, product: planProductId)` to check existing by customer+plan; if absent call `CreateSubscription`. Source: `operations/Subscriptions.md`. Request model fields per map (`CreateSubscriptionRequest`); set `trial_ends_at` null / no trial, `setup_fee` null / 0, `expires_at` never / null, `taxable` false, `payment_method_required` false (verify wire names from `map/records-...`).
6. **My-subscriptions (`GET /api/my-subscriptions`)** — `client.Subscriptions.ListSubscriptions` filtered to JWT identity's customer id (looked up via email/reference from token claim). Source: `operations/Subscriptions.md`.
7. **Error boundary / retries** — wrap every SDK call; handle Case A (`SdkException<CreateCustomerError>`, `SdkException<CreateSubscriptionError>`) and Case B (`SdkException<RawError>`). `dotnet-error-handling`.

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and config types are spread across different child namespaces.

### Client construction / auth / server

| Fact | Value / Type | Source |
|---|---|---|
| Client class | `MaxioAdvancedBilling.MaxioAdvancedBillingClient` (namespace `MaxioAdvancedBilling`) | `sdk-map.md` |
| Options | `MaxioAdvancedBillingClientOptions` (namespace `MaxioAdvancedBilling`) | `sdk-map.md` |
| Auth scheme | HTTP Basic — `BasicAuthCredentials` (namespace `MaxioAdvancedBilling.Core.Authentication.Basic`) with `Username = API key`, `Password = "x"` (literal) | `sdk-map.md` |
| Environments | `MaxioAdvancedBilling.Servers.ServerEnvironment.Us` (default → `https://{site}.chargify.com`); `Eu` → `https://{site}.ebilling.maxio.com` | `sdk-map.md` |
| Sandbox site | `cp-exp-1`; target `sandbox`; base URL override via `ServerOptions` / `Maxio:BaseUrl` if needed | `YOUR CALL — not in the map` (site name from brief) |
| Server-node / base URL | `ServerOptions` configured via `MaxioAdvancedBillingClientOptions.Server` (namespace `MaxioAdvancedBilling.Servers`); optional `Maxio:BaseUrl` overrides | `dotnet-client-initialization` / `sdk-map.md` |

### Operations in scope (signature from map, verbatim parameter names)

| Controller / property | Method signature (params order, types, required-but-nullable) | Request model + fields (wire names, required?) | Response envelope + inner payload | Error case + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 nullable params must pass explicitly (pass `null`) | — | `IReadOnlyList<CustomerResponse>` (each `CustomerResponse.Customer` per envelope rule — verify from `map/models/records-...`) | **Case B** `SdkException<RawError>` (`StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`) | manual `page`/`perPage` (defaults `page=1`, `perPage=50`) | `operations/Customers.md` |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | — | `CustomerResponse` | **Case B** `SdkException<RawError>` | none | `operations/Customers.md` |
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default, must pass explicitly | `CreateCustomerRequest` fields per `map/models/records-...` (email, reference, etc.; verify wire names) | `CustomerResponse` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `operations/Customers.md` |
| `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — many nullable, pass explicitly | — | `IReadOnlyList<SubscriptionResponse>` (envelope: `.Subscription`) | **Case B** `SdkException<RawError>` | manual `page=1`, `perPage=20` | `operations/Subscriptions.md` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass explicitly | `CreateSubscriptionRequest` fields per `map/models/records-...`: `product_family_id`, `product_handle` / `product_id`, `plan_handle` / `plan_id` / `product_price_point_id`, `customer_id` / `customer_reference`, `trial_ends_at` (null = no trial), `setup_fee` (null/0), `expires_at` (null = never), `taxable` (false), `payment_method_required` (false); verify wire names exactly | `SubscriptionResponse` (envelope `.Subscription`) | **Case A** `SdkException<CreateSubscriptionError>` — accessors per `operations/Subscriptions.md` (load page) | none | `operations/Subscriptions.md` |
| `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse` (envelope `.ProductFamily`) | **Case B** `SdkException<RawError>` | none | `operations/ProductFamilies.md` |
| `client.Products` | `ListProducts(...)` / `ReadProduct(...)` — use to resolve handles `eshop-pro`, `basic-plan` to IDs | — | `ProductResponse` / list | Case B / per-row | — | `operations/Products.md` |
| `client.Components` | `ListComponents(...)` / `ReadComponent(...)` — resolve `api-call` metered (3057195) | — | `ComponentResponse` / list | Case B / per-row | — | `operations/Components.md` |

> Note on response envelopes: the SDK's response records wrap payload in one field (e.g., `SubscriptionResponse.Subscription`, `CustomerResponse.Customer`). Read the payload from that inner field, not from the wrapper directly — confirm per `map/models/records-...`.

### Catalog / plan details (stable, not mutated)

| Catalog item | Handle / ID | Use in plan |
|---|---|---|
| Product Family | `eshop-subscribe` (id 3023074) | Filter products by family |
| Pro Plan | `eshop-pro` (id 7126957), $299/mo | Plan option for POST |
| Basic Plan | `basic-plan` (id 7126958), $29/mo | Plan option for POST |
| Metered component | `api-call` (3057195), $0.01/unit | Enroll via `SubscriptionComponents` / `ComponentUsage` if required; brief lists it but endpoint specs don't mandate usage — see Blockers |

### Config / settings (binding keys, not raw env vars)

| Setting key (binding) | Source | Default / behavior |
|---|---|---|
| `Maxio:ApiKey` | `IConfiguration` / `Maxio:ApiKey`; load into user-secrets (`dotnet user-secrets set Maxio:ApiKey ...`); never commit | Required; sandbox key from `MAXIO_API_KEY` |
| `Maxio:Subdomain` | `IConfiguration`; load from `MAXIO_SITE_SUBDOMAIN`; site = `cp-exp-1` | `cp-exp-1` (sandbox) |
| `Maxio:ProductFamilyHandle` | `IConfiguration`; load from `MAXIO_DEFAULT_PRODUCT_FAMILY`; value `eshop-subscribe` | `eshop-subscribe` |
| `Maxio:BaseUrl` | `IConfiguration`; optional override (`MAXIO_ENVIRONMENT` -> `sandbox` determines server); if set, configure `ServerOptions` / base URL | Optional; default sandbox via `ServerEnvironment` |

Env-to-secrets mapping (do NOT write to repo): `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT` (`sandbox`), `MAXIO_DEFAULT_PRODUCT_FAMILY` → load into `.NET user-secrets` via CLI before running; reference by binding key `Maxio:...`.

### Idempotence rules (application-level; SDK is throw-only)

- Customer: `ListCustomers(q: userEmail)` or `ReadCustomerByReference(reference: userIdFromToken)`. If missing → `CreateCustomer` with unique `reference` (user id / token sub). Note: reference must be unique per `Customers.md` notes.
- Subscription: `ListSubscriptions(customer: customerId, product: planProductId)` (filter by `product` = plan's product id). If any returned with matching product + active state → skip create (idempotent). Else `CreateSubscription`.

## 3. Trap notes (named hazard + MUST load pointer; do NOT resolve inline)

- ⚠ Step 2 (customer lookup) — `ListCustomers` has 7 nullable params with no C# default; passing positional `null`s in wrong order binds wrong query params. **MUST load `dotnet-calling-endpoints`** before first `ListCustomers` call; use named args (`direction: null, dateField: null, ...`).
- ⚠ Step 3 (catalog) — `ProductFamily` / `Product` / `Component` IDs in brief are stable but the SDK only exposes them via `int id` reads; handle-to-id mapping must be done once and cached, otherwise every call hits catalog. **MUST load `dotnet-configuration-resilience`** for caching / base-URL selection scope.
- ⚠ Step 4 (subscription list endpoint) — subscription-plan listing is read-only; no SDK mutation needed, but the response envelope (`SubscriptionResponse.Subscription`) must be unwrapped. **MUST load `dotnet-models`** to confirm wire/names on `SubscriptionResponse`. 
- ⚠ Step 5 (enroll / idempotence) — `CreateSubscription` is Case A typed error (`CreateSubscriptionError`) but `ListSubscriptions` is Case B raw; the boundary must handle both shapes. `RetryOptions` with `MaxRetries` floor 1 and transport-failure retry on POST risks double-enrollment if idempotence check races; enforce check-then-create sequentially, not concurrent. **MUST load `dotnet-configuration-resilience`** before tuning retries; do NOT set `MaxRetries = 0` (rejected). **MUST load `dotnet-error-handling`** before writing catch ladder.
- ⚠ Step 6 (my-subscriptions) — JWT identity claim (email / sub) must be mapped to `reference` or `email` for customer lookup; the SDK has no JWT awareness. **MUST load `dotnet-authentication`** only for SDK Basic auth — API-layer JWT is YOUR CALL, not in SDK.
- ⚠ All SDK operations are throw-only (no `…Result` variants). A `JsonException` from a 2xx body (missing required member) reaches the boundary as deserialization failure, not `SdkException`; a non-2xx body that doesn't match `{Operation}Error` throws `JsonException` during error construction, destroying HTTP status. Boundary must distinguish `JsonException` source (deser vs error-construction) defensively. **MUST load `dotnet-error-handling`** — both `JsonException` directions are mandatory per that skill.

## 4. REQUIRED READING (load BEFORE implementation starts)

- `dotnet-client-initialization` — client/DI construction; `HttpClient` reuse; `MaxioAdvancedBillingClientOptions` members (namespace `MaxioAdvancedBilling`; `BasicAuth` in `Core.Authentication.Basic`, `RetryOptions` in `Core.Configuration`).
- `dotnet-authentication` — Basic auth pattern (`Username = API key`, `Password = "x"`); credential source must be config, not hardcoded.
- `dotnet-calling-endpoints` — named-argument usage for many-nullable signatures; cancellation-token param named `ct`; response-envelope unwrap.
- `dotnet-models` — `StringEnum<T>` enums; union/accessor patterns; request required vs optional; wire names different from C# property names; `CreateSubscriptionRequest`/`SubscriptionResponse` fields.
- `dotnet-configuration-resilience` — `RetryOptions` required members; `StatusCodesToRetry` / `HttpMethodsToRetry` gate status-only retries; transport-failure retries ALL verbs including POST; `Timeout` is per-attempt; `MaxRetries` floor 1; no built-in logging hook; server selection / `ServerOptions`.
- `dotnet-error-handling` — Case A (`SdkException<{Op}Error>` + `TryGet…`) vs Case B (`SdkException<RawError>`); both `JsonException` directions (2xx deser failure, non-2xx error-shape mismatch); never parse `.ToString()` when accessor exists.
- `dotnet-testing` — seam is `HttpClient`; match existing project framework.

These contain defaults / worked examples / hooks the sheet deliberately excludes.

## 5. Assumptions & Blockers

Assumptions (your call):
- `PublicApi` identity is derived from JWT claim (email or sub); mapping to Maxio `reference` / `email` is application design — not SDK.
- Catalog entries (`eshop-subscribe` family, plan handles, `api-call`) remain stable by handle; if handles change, resolution breaks.
- In-memory DB + `rollForward latestMajor` + `DOTNET_ROLL_FORWARD=Major` is sufficient; no persistence of Maxio IDs required outside in-memory session (but idempotence requires consistent customer reference across calls within session — okay for in-memory if reference derived deterministically from JWT).
- Metered component `api-call` (3057195) is listed but endpoints (`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`) do not explicitly require usage reporting; if usage must be recorded, additional endpoint (`Usage`) is needed — not in brief.

Blockers (would stop planning / require clarification):
- **UNVERIFIED** — whether `CreateSubscriptionRequest`'s wire fields for `no_trial`, `no_setup_fee`, `never_expires`, `taxable: false`, `payment_method_required: false` exactly match the brief's "no trial, no setup fee, expires never, taxable no, payment method not required". Only live traffic confirms payload acceptance; defensive code: construct request with those fields explicitly set per map model, and if 422 returned, extract `CustomerErrorResponse1` / `SubscriptionErrorResponse` via `TryGet…` and fall back to generic message. **UNVERIFIED**.
- **Blocker** — exact `subscription-plans` endpoint contract: brief asks `GET /api/subscription-plans`; SDK has no direct "list plan objects" endpoint — plan info must come from `Products` + `ProductPricePoints` + `SubscriptionProducts`. Confirm whether endpoint returns a custom DTO or SDK-derived plan list; if custom, SDK is only used for enroll/lookup, not for the GET response shape. This is `YOUR CALL — not in the map`.
- **Blocker / assumption** — `MAXIO_ENVIRONMENT` value mapping to `ServerEnvironment`: brief says `sandbox`; SDK environment is `ServerEnvironment.Us` / `Eu`. Sandbox site `cp-exp-1` may use US endpoint with subdomain override. Confirm `Maxio:BaseUrl` override format (e.g., `https://cp-exp-1.chargify.com` or sandbox-specific). Until confirmed, bind `Maxio:BaseUrl` explicitly from env and configure `ServerOptions`.

## 6. Source citations (map pages actually read this session)

- `maxio-getting-started/sdk-map.md` (identity, client, auth, error core, namespaces)
- `.opencode/skills/maxio-getting-started/map/operations/Customers.md` (ListCustomers, ReadCustomerByReference, CreateCustomer)
- `.opencode/skills/maxio-getting-started/map/operations/Subscriptions.md` (ListSubscriptions, CreateSubscription, PreviewSubscription)
- `.opencode/skills/maxio-getting-started/map/operations/ProductFamilies.md` (ListProductFamilies, ReadProductFamily)
- `.opencode/skills/maxio-getting-started/map/operations/Products.md` (listed for handle resolution)
- `.opencode/skills/maxio-getting-started/map/operations/Components.md` (listed for metered component)
- `.opencode/skills/maxio-getting-started/map/operations/SubscriptionComponents.md` (if usage enrollment added)
- Companion skills loaded by reference: `dotnet-client-initialization`, `dotnet-authentication`, `dotnet-calling-endpoints`, `dotnet-models`, `dotnet-error-handling`, `dotnet-configuration-resilience`, `dotnet-testing`.

No SDK source clone was needed — map answered all operation signatures; no gap triggered clone (per rule: clone only on real map-side issue; none occurred). Clone path never appears here; not in repo.
