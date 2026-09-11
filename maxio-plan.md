# Maxio Advanced Billing .NET SDK — Contract Sheet (eShopOnWeb integration)

Path dictated by brief: `C:\claude-runs\t1oczaid-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-008\repo\maxio-plan.md`
Mode: Plan (no project-file edits). Source: bundled SDK map (`sdk-map.md` + `map/operations/*.md` + `map/models/*.md`) — clone not required; no gap found.

---

## 1. Scope & sequence

Integration adds subscription billing to the eShopOnWeb `PublicApi` project via `AsadAli.AdvancedBilling.Sdk` (`MaxioAdvancedBilling`). The SDK is called only from the backend; the three user-facing endpoints (`GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`) are the application's controllers — their layout is YOUR CALL.

Sequence (each step uses the SDK operations below):
1. **Client & auth** — bind `MaxioAdvancedBillingClient` from config section (`ApiKey`, `Subdomain`, `ProductFamilyHandle`, optional `BaseUrl` override); set Basic auth (`Username` = API key, `Password` = `"x"`). **MUST load `dotnet-client-initialization`**, **`dotnet-authentication`**.
2. **Plan/component verification** — confirm family `eshop-subscribe` (id 3023074) and plans `eshop-pro` (7126957, $299/mo) / `basic-plan` (7126958, $29/mo) and metered component `api-call` (3057195) via `ProductFamilies` / `Components`. **MUST load `dotnet-calling-endpoints`**, **`dotnet-models`**.
3. **Customer idempotency** — map eShopOnWeb user (email / user-id) to Maxio customer: try `ReadCustomerByReference` (if reference = user id) or `ListCustomers(q=email)`; if missing, `CreateCustomer`. **MUST load `dotnet-error-handling`** before any catch.
4. **Subscription idempotency** — list existing via `ListCustomerSubscriptions(customerId)`; if no subscription for `(customer, plan)` exists, `CreateSubscription`. Confirm `plan` (handle/id), `state`, `next_billing_at`, price back to caller. **MUST load `dotnet-configuration-resilience`** for retries/timeouts; `Timeout` bounds per-attempt, not total.
5. **Public API surface** — `GET subscription-plans` returns verified plan list; `POST subscriptions` performs steps 3–4 and confirms back; `GET my-subscriptions` returns `ListCustomerSubscriptions` for the mapped customer. **MUST load `dotnet-testing`** before stubbing.

No capability is missing in the map; nothing is a Blocker (§5).

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side-by-side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Namespaces required (add `using` per kind; child namespaces not transitive)

| Kind | Namespace (verbatim from `sdk-map.md`) |
|---|---|
| Client & options | `MaxioAdvancedBilling` |
| Controllers | `MaxioAdvancedBilling.Api` |
| Request/response records | `MaxioAdvancedBilling.Models` |
| Enums (`StringEnum<T>`) | `MaxioAdvancedBilling.Models.Enums` |
| Unions (`OneOf`/`AnyOf`) | `MaxioAdvancedBilling.Models.AnyOf` · `MaxioAdvancedBilling.Models.OneOf` |
| Errors (typed) | `MaxioAdvancedBilling.Errors` |
| Core exceptions / auth / config | `MaxioAdvancedBilling.Core.Authentication.Basic`, `MaxioAdvancedBilling.Core.Configuration`, `MaxioAdvancedBilling.Core.Exceptions` |
| Servers / environment | `MaxioAdvancedBilling.Servers` |

### 2.2 Client construction & auth (map: `sdk-map.md` §§ Getting a client / Servers & auth)

- Class: `MaxioAdvancedBillingClient` (root namespace `MaxioAdvancedBilling`; source `MaxioAdvancedBillingClient.cs`)
- Constructor: `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` — only constructor.
- Options: `MaxioAdvancedBillingClientOptions` (`Environment`: `ServerEnvironment`; `BasicAuth`: `BasicAuthCredentials?`; `Retry`: `RetryOptions`; `Server`: `ServerOptions`).
- Auth: HTTP Basic — `BasicAuthCredentials` (`Username` = API key, `Password` = literal `"x"`). Source `Core/Authentication/Basic/BasicAuthCredentials.cs`.
- Environments: `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com`. `site` = subdomain (config key `Subdomain`).
- Server override (optional `BaseUrl`): `options.Server.Production.Us.BaseUrl = "..."`; `options.Server.Production.Us.Site = "eshop-subscribe"` (or from config). Source `Servers/ServerOptions.cs`, `ProductionOptions.cs`.
- Retry (`RetryOptions`, namespace `MaxioAdvancedBilling.Core.Configuration`): members `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries` (floor 1 — `0` rejected), `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. All `required`; build via `RetryOptions.Default()` + setters or full instance.

### 2.3 Operations in scope

| Controller property | Signature (params in order; `ct` = `CancellationToken`) | Request model + fields (`Name (wire): Type`, required?) | Response envelope + inner fields read back | Error case A/B + accessors + payload type | Pagination | Source (map page) |
|---|---|---|---|---|---|---|
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — body nullable, must pass explicitly | `CreateCustomerRequest`: `Customer (customer): CreateCustomer !req`. `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?`, `Address (address): string?`, etc. (full: `Models/CreateCustomer.cs`) | `CustomerResponse`: `Customer (customer): Customer !req`. Read `Id`, `Email`, `Reference`, `State`? (Customer record: `Id`, `Email`, `Reference`, `FirstName`, `LastName`, `CreatedAt`, `UpdatedAt` — source `Models/Customer.cs`) | **Case A** `SdkException<CreateCustomerError>`. Accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]. Payload: `CustomerErrorResponse1` (errors: `Errors?`) | none | `map/operations/Customers.md` |
| `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 nullable params must pass explicitly (pass `null`); defaults `page=1`, `perPage=50` | — (query only; `q` for email search) | `IReadOnlyList<CustomerResponse>` — read `.Customer.Email`, `.Customer.Id`, `.Customer.Reference` | **Case B** `SdkException<RawError>` — accessors: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | manual `page`+`perPage` | `map/operations/Customers.md` |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` must pass explicitly | — | `CustomerResponse` — same inner fields as above | **Case B** `SdkException<RawError>` | none | `map/operations/Customers.md` |
| `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` required | — | `IReadOnlyList<SubscriptionResponse>` — inner `Subscription?` (read `Id`, `State`, `ProductHandle`, `ProductPricePointHandle`, `CustomerId`, `NextBillingAt`, `CurrentPeriodStartedAt`, etc.) | **Case B** `SdkException<RawError>` | none | `map/operations/Customers.md` |
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — body must pass explicitly | `CreateSubscriptionRequest`: `Subscription (subscription): CreateSubscription !req`. Key `CreateSubscription` fields: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `Reference (reference): string?`, `NextBillingAt (next_billing_at): DateTimeOffset?`, `InitialBillingAt (initial_billing_at): DateTimeOffset?`, `PaymentProfileId (payment_profile_id): int?`, etc. | `SubscriptionResponse`: `Subscription (subscription): Subscription?` (nullable — verify non-null on 2xx before reading). Read `Id`, `State`, `ProductHandle`, `ProductPricePointHandle`, `CustomerId`, `NextBillingAt`, `CurrentPeriodStartedAt`, `PriceInCents`, `State` (enum) | **Case A** `SdkException<CreateSubscriptionError>`. Accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] | none | `map/operations/Subscriptions.md` |
| `client.Subscriptions` | `ListSubscriptions(...)` (14 params; only `state`/`product`/etc. as needed; `page=1`, `perPage=20`) — many must pass explicitly | — (query filters: `state`, `product` (int), `productPricePointId`, `customer` not a direct filter; prefer `ListCustomerSubscriptions` for per-customer idempotency) | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage` | `map/operations/Subscriptions.md` |
| `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 nullable params must pass explicitly | — | `IReadOnlyList<ProductFamilyResponse>` (inner `ProductFamily`: `Id`, `Handle`, `Name`, etc.) | **Case B** `SdkException<RawError>` | none | `map/operations/ProductFamilies.md` |
| `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse` (verify handle `eshop-subscribe`) | **Case B** `SdkException<RawError>` | none | `map/operations/ProductFamilies.md` |
| `client.Components` | `ListComponents(...)` (check `map/operations/Components.md`; controller `client.Components`) — use to verify `api-call` metered component handle/id 3057195 | — (query filters available) | `IReadOnlyList<ComponentResponse>` (inner `Component`: `Id`, `Handle`, `Name`, `PricingScheme`, `UnitPrice`, `Kind`) | varies; confirm per-row | manual / none | `map/operations/Components.md` |

Notes on idempotency (contract, not application design):
- Customer exists-check: `ListCustomers(q = email)` then verify `.Customer.Email`; or `ReadCustomerByReference(userId)` if `Reference` set to user id at creation. If not found → `CreateCustomer` with `Reference = userId`, `Email = email`, `FirstName`/`LastName` from profile.
- Subscription exists-check: `ListCustomerSubscriptions(customerId)` → scan for `Subscription.ProductHandle == "eshop-pro"` (or `"basic-plan"`) and `Subscription.State != "cancelled"`. Only if absent → `CreateSubscription` with `Subscription.ProductHandle`, `Subscription.CustomerId`, optional `Components` for metered `api-call`.
- The brief fixes plan ids/handles (`4426957` etc.) to the correct values given (`7126957` / `7126958` / family `3023074` / component `3057195`). Map verifies handles via `ListProductFamilies` / `ReadProductFamily` / `ListComponents`; does not confirm live existence — label UNVERIFIED if relying on provider accepting those handles.

### 2.4 Request/response envelope shapes (critical — read one level down)

- `CreateCustomerRequest` → `CreateCustomer` inner (not flat).
- `CustomerResponse` → `Customer` inner (`CustomerResponse.Customer`).
- `CreateSubscriptionRequest` → `CreateSubscription` inner.
- `SubscriptionResponse` → `Subscription` inner (`SubscriptionResponse.Subscription`) — nullable `Subscription?`; verify non-null before accessing fields.
- `ProductFamilyResponse` → `ProductFamily` inner.
- `ComponentResponse` → `Component` inner.

### 2.5 Key enum values needed (from `map/models/enums.md`; namespace `MaxioAdvancedBilling.Models.Enums`)

- `SubscriptionState` (or `SubscriptionStateFilter`): values include `Active`, `Canceled`, `PastDue`, `Trialing`, `AwaitingSignup`, etc. — read `Subscription.State` via this enum.
- `CollectionMethod` (if passing `PaymentCollectionMethod`): values include `Invoice`, `ChargeCard`.
- `PricingScheme`: values include `PerUnit`, `Tiered`, `Volume`, `Stairstep` (for component verification).
- `ComponentKind`: values include `MeteredComponent`, `QuantityBasedComponent`, etc. — confirm `api-call` is `MeteredComponent`.
- `SortingDirection`: `Asc`, `Desc` (optional, for list calls).

(Exact literal member names from `enums.md`; do not invent wire values.)

---

## 3. Trap notes (consequence only; resolve by loading skill)

- ⚠ Step 1 (client registration / DI) — `HttpClient` must be long-lived/reused via `IHttpClientFactory`; the SDK wrapper over it may be transient. The `Retry` / `Timeout` option names alone don't reveal which calls retry, what `Timeout` bounds (per-attempt, not total), or that `MaxRetries = 0` is rejected at construction (floor 1). **MUST load `dotnet-client-initialization`**, **`dotnet-configuration-resilience`**.
- ⚠ Step 2 (auth) — Basic auth: `Username` = API key, `Password` = literal `"x"`. Must be set before constructing client or in DI callback; load from config, not hardcoded. **MUST load `dotnet-authentication`**.
- ⚠ Step 3 (call list/search) — many optional params have no C# default and mis-bind in positional calls; use named arguments with exact parameter names (`q:`, `page:`, `ct:`). **MUST load `dotnet-calling-endpoints`**.
- ⚠ Step 4 (request/response) — request bodies are wrapped (`Customer`, `Subscription` inner); responses are envelopes (`CustomerResponse.Customer`, `SubscriptionResponse.Subscription`). Unions use factory methods / `TryGet…`; enums are `StringEnum<T>` not C# enums; unmodeled JSON fields dropped on deserialize. **MUST load `dotnet-models`**.
- ⚠ Step 5 (error boundary) — two opposite `JsonException` paths reach the catch block: (a) a drifted/malformed 2xx body (missing `required` member) surfaces as `JsonException` from deserialization, not `SdkException`; (b) a non-2xx body that doesn't match the operation's generated `{Operation}Error` shape throws `JsonException` while the error object is being constructed, replacing the `SdkException` and destroying the HTTP status. A boundary mapping every `JsonException` to 5xx reports deterministic rejections as outages; retrying 5xx retries non-retryable errors. **MUST load `dotnet-error-handling`**.
- ⚠ Step 6 (idempotency / retries) — `HttpMethodsToRetry` gates only status-trigger retries; `HttpRequestException` (transport) retries on **every** verb including `POST`, so a non-idempotent write can execute more than once and no setting disables it (`MaxRetries = 0` rejected). If re-sending a write, protect via pre-check (list before create). **MUST load `dotnet-configuration-resilience`**.
- ⚠ Step 7 (tests) — match framework/style; stub via `HttpClient` constructor seam. **MUST load `dotnet-testing`**.

---

## 4. REQUIRED READING (load before any code)

All names below come from the sheet above; only the usage guidance lives in the skills.

- `dotnet-client-initialization` — governs Step 1 (client/DI construction, `HttpClient` lifetime).
- `dotnet-authentication` — governs Step 2 (Basic auth shape, credentials binding).
- `dotnet-calling-endpoints` — governs Steps 3–4 (named args, envelope read-down, cancellation token `ct`).
- `dotnet-models` — governs Steps 3–4 (request/response wrapping, enums, unions, wire names, required members).
- `dotnet-error-handling` — governs Step 5 (Case A/B accessors, `TryGetRawError`, `SdkException<T>`, error payload types) and **mandatory** for both `JsonException` paths below.
- `dotnet-configuration-resilience` — governs Step 6 (retry semantics, `Timeout` as per-attempt, `MaxRetries` floor, base-URL override, pagination).
- `dotnet-testing` — governs Step 7.

These are loaded **before implementation starts**, not lazily.

### Mandatory error-boundary caveats (both `JsonException` directions — do not drop either)

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary. These rows belong in the first sheet, not a later revision.

---

## 5. Assumptions & Blockers

- **Caller identity / user-to-customer mapping** (`Reference` vs `Email`, whether to use `ReadCustomerByReference` or `ListCustomers(q=...)`) — `YOUR CALL — not in the map` (application identity path).
- **BaseUrl override key / server-node binding** (whether `BaseUrl` is set from config key `BaseUrl` or derived from `Subdomain`) — `YOUR CALL — not in the map`; default from `ServerEnvironment.Us` + subdomain.
- **Plan/handle validity** (`eshop-pro` 7126957, `basic-plan` 7126958, family `eshop-subscribe` 3023074, component `api-call` 3057195) — given in brief; the SDK map means read them back with `ListProductFamilies`/`ReadProductFamily`/`ListComponents`, but whether the live provider accepts those exact handles is `UNVERIFIED` (only live traffic can confirm). Defensively: extract best-effort from `SubscriptionResponse.Subscription.ProductHandle`, fall back to the configured handle when confirming back to user.
- **Public endpoint designs** (`/api/subscription-plans`, `/api/subscriptions`, `/api/my-subscriptions`) — `YOUR CALL — not in the map`; not SDK operations.
- **Idempotency key** for subscription — using `(customerId, productHandle)` pair; no SDK-level idempotency key exists. If provider allows duplicate subscriptions for same customer/product, the pre-check (`ListCustomerSubscriptions`) is the only guard. Uncertain behavior → defensive pre-check + log ambiguity (`UNVERIFIED` only for live duplicate behavior).
- **Metered component allocation** (`api-call`) — whether to include `Components` in `CreateSubscriptionRequest` with `Quantity` / `AllocatedQuantity` / `PricePointId` depends on desired billing model; not specified fully. If omitted, subscription may not include component billing. Recommendation: carry the optional `Components` list as a `YOUR CALL` based on billing requirements.
- **Blockers**: none. All required SDK operations exist in the map (`Customers` 7 ops, `Subscriptions` 12 ops, `ProductFamilies` 4 ops, `Components` 12 ops). No missing controller or method.

---

## 6. Source citations (per-row; no open lookups)

- SDK identity / package / namespace / auth / environments / clients: `sdk-map.md` (lines 1–225, §§ SDK identity / Getting a client / Servers & auth).
- `CreateCustomer` / `ListCustomers` / `ReadCustomerByReference` / `ListCustomerSubscriptions`: `map/operations/Customers.md`.
- `CreateSubscription` / `ListSubscriptions` / `ListCustomerSubscriptions`: `map/operations/Subscriptions.md`.
- `ListProductFamilies` / `ReadProductFamily`: `map/operations/ProductFamilies.md`.
- `ListComponents`: `map/operations/Components.md`.
- `CreateCustomerRequest` / `Customer` / `CustomerResponse` / `CreateSubscription` / `SubscriptionResponse` / `Component`: `map/models/records-1-Ac-Cr.md` (line 124 `CreateCustomer` / 125 `CreateCustomerRequest`; `CustomerResponse` at `records-2-Cr-Ne.md` line 43; `SubscriptionResponse` at `records-4-Su-We.md` line 66; `Component` at `records-1-Ac-Cr.md` line 81).
- `CreateSubscriptionRequest`: `map/models/records-2-Cr-Ne.md` line 21.
- Enums: `map/models/enums.md`.
- Error core (`SdkException<T>`, `ApiError`, `RawError`): `sdk-map.md` §§ Error-handling model (lines 81–117) + `Core/ErrorResponse/` sources named by map.

No clone path appears here; clone never left system temp (rule) and is not referenced.
