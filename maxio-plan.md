# Maxio Advanced Billing — contract sheet (eShopOnWeb integration)

Plan written at: `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-007\repo\maxio-plan.md`
Mode: Plan (contract-only; no project edits yet — integrate-maxio gate: plan read before code).

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

---

## 1. Scope & sequence

Steps in order; each names the operations and companion skills to load first.

1. **Client + auth + server** (step loads `dotnet-client-initialization`, `dotnet-authentication`, `dotnet-configuration-resilience`) — build `MaxioAdvancedBillingClient` with `BasicAuth`, set `Server.Production.Us.BaseUrl` if overriding host, confirm `Environment`.
2. **Find / create customer (idempotent)** — `client.Customers.ReadCustomerByReference(string reference)` (Case B) → if missing / 404, `client.Customers.CreateCustomer(CreateCustomerRequest? body)` (Case A). Use `reference` to make idempotent; map Notes: only one customer per reference value.
3. **Catalog read** (step loads `dotnet-calling-endpoints`) — `client.ProductFamilies.ListProductFamilies(...)` + `ReadProductFamily(int id)` / `ListProductsForProductFamily(...)` to resolve `product_handle` / `product_id` and price-point IDs/handles.
4. **Create subscription** — `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body)` (Case A). Body must include `Subscription` (required) with either `ProductHandle`/`ProductId` and optionally `CustomerReference`/`CustomerId`; notes say specify product via `product_id` or `product_handle`, price point via `product_price_point_handle`/`product_price_point_id`, customer via `customer_id` or `customer_reference`.
5. **List subscriptions for customer** — `client.Customers.ListCustomerSubscriptions(int customerId)` (Case B) returns `IReadOnlyList<SubscriptionResponse>`; also `client.Subscriptions.ListSubscriptions(...)` (Case B) if site-wide filter needed.
6. **Error boundary** (step loads `dotnet-error-handling`) — catch `SdkException<T>` per operation case; never parse `.ToString()` when `TryGet…` exists; handle both `JsonException` directions per §4.

---

## 2. CONTRACT SHEET

All controllers referenced by property on `MaxioAdvancedBillingClient` (`client.Customers`, `client.Subscriptions`, `client.ProductFamilies`, etc.). Source namespaces: controllers in `MaxioAdvancedBilling.Api`; records in `MaxioAdvancedBilling.Models`; enums in `MaxioAdvancedBilling.Models.Enums`; errors in `MaxioAdvancedBilling.Errors`; auth / server / core config in child namespaces per map.

### Client / auth / server (map: `sdk-map.md` §Getting a client / Servers & auth)

| Item | Fact | Source / namespace |
|---|---|---|
| Package | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` | `MaxioAdvancedBilling` (root) |
| Options | `MaxioAdvancedBillingClientOptions` | `MaxioAdvancedBilling` |
| Auth | HTTP Basic — `BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" }` | `MaxioAdvancedBilling.Core.Authentication.Basic` |
| Constructor | `new MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `MaxioAdvancedBilling` |
| DI | `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ... })` | `ServiceCollectionExtensions` |
| Env | `ServerEnvironment.Us` (default) / `.Eu` | `MaxioAdvancedBilling.Servers` |
| Production US base | `https://{site}.chargify.com`; EU: `https://{site}.ebilling.maxio.com` | `Servers/ServerOptions.cs` |
| Override host | `options.Server.Production.Us.BaseUrl = "http://localhost:8080"`; also `.Site` | `sdk-map.md` §Servers |
| Retry | `RetryOptions` (`MaxioAdvancedBilling.Core.Configuration`) — `RetryOptions.Default()`; `MaxRetries` floor is 1 (0 rejected); `Timeout` is per-attempt, not call-total; `HttpMethodsToRetry` gates status-trigger only, but `HttpRequestException` retries on every verb including POST | `sdk-map.md`; `dotnet-configuration-resilience` |

### Operations (map pages cited per row)

| # | Controller · Method (signature verbatim) | Request (key fields; full defs in map records) | Response envelope (inner read) | Error case / accessors | Pagination / notes | Source |
|---|---|---|---|---|---|---|
| 1 | `client.Customers.ReadCustomerByReference(string reference, CancellationToken ct = default)` | query `reference` | `CustomerResponse` → `.Customer` (`MaxioAdvancedBilling.Models.Customer`) | **B** `SdkException<RawError>` — `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()` | none; lookup by app reference | `map/operations/Customers.md` |
| 2 | `client.Customers.CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → must pass explicitly | `CreateCustomerRequest` requires `Customer (customer): Customer !req` (see record page for fields: `Reference (reference): string?`, etc.) | `CustomerResponse` → `.Customer` | **A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` | none; Notes: reference must be unique (one customer per reference) | `map/operations/Customers.md`; `map/models/records-1-Ac-Cr.md` (CreateCustomerRequest / Customer) |
| 3 | `client.Customers.ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | path `customerId` | `IReadOnlyList<SubscriptionResponse>` → each `.Subscription` (`Subscription`) | **B** `SdkException<RawError>` | none | `map/operations/Customers.md` |
| 4 | `client.Subscriptions.CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, must pass | `CreateSubscriptionRequest` requires `Subscription (subscription): CreateSubscription !req`; `CreateSubscription` fields include `ProductHandle`/`ProductId`, `ProductPricePointHandle`/`ProductPricePointId`, `CustomerId`/`CustomerReference`, `Reference`, `CustomerAttributes`, `PaymentProfileAttributes`, etc. (see `records-2-Cr-Ne.md`) | `SubscriptionResponse` → `.Subscription` | **A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` | none; Notes: specify product + price point + existing/new customer via attributes/reference | `map/operations/Subscriptions.md`; `map/models/records-2-Cr-Ne.md` |
| 5 | `client.Subscriptions.ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | 14 nullable params (must pass explicitly, `null` to skip); defaults `page=1`, `perPage=20`; wire names: `state`, `product`, `product_price_point_id`, `coupon`, `coupon_code`, `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `metadata`, `direction`, `sort`, `include`, `page`, `per_page` | `IReadOnlyList<SubscriptionResponse>` | **B** `SdkException<RawError>` | manual `page`+`perPage`; include via `include[]=` | `map/operations/Subscriptions.md` |
| 6 | `client.ProductFamilies.ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | 5 nullable params, all must pass explicitly | `IReadOnlyList<ProductFamilyResponse>` → `.ProductFamily` | **B** `SdkException<RawError>` | none | `map/operations/ProductFamilies.md` |
| 7 | `client.ProductFamilies.ReadProductFamily(int id, CancellationToken ct = default)` | path `id`; can also pass `handle:my-family` format per notes (but param is `int` — handle format likely via other endpoint; stick to `int`) | `ProductFamilyResponse` → `.ProductFamily` | **B** `SdkException<RawError>` | none | `map/operations/ProductFamilies.md` |
| 8 | `client.ProductFamilies.ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `productFamilyId` (string; can be id or handle); other filters optional; defaults `page=1`, `perPage=20` | `IReadOnlyList<ProductResponse>` → `.Product` | **A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` | manual pagination | `map/operations/ProductFamilies.md` |

**Response envelopes (always read one level down)** — `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`, `ProductFamilyResponse.ProductFamily`, `ProductResponse.Product`. Source patterns from `map/models/records-...`: `CustomerResponse` has `Customer (customer): Customer !req`; `SubscriptionResponse` has `Subscription (subscription): Subscription !req`; `ProductFamilyResponse`/`ProductResponse` analogous.

**Enum values needed (StringEnum — build via `FromValue("wire")` or static members; namespace `MaxioAdvancedBilling.Models.Enums`):**

- `SubscriptionState` (`Models/Enums/SubscriptionState.cs`): `Pending`, `FailedToCreate`, `Trialing`, `Assessing`, `Active`, `SoftFailure`, `PastDue`, `Suspended`, `Canceled`, `Expired`, `Paused`, `Unpaid`, `TrialEnded`, `OnHold`, `AwaitingSignup`.
- `SubscriptionStateFilter` (`Models/Enums/SubscriptionStateFilter.cs`): `Active`, `Canceled`, `Expired`, `ExpiredCards`, `OnHold`, `PastDue`, `PendingCancellation`, `PendingRenewal`, `Suspended`, `TrialEnded`, `Trialing`, `Unpaid`.
- `CollectionMethod` (used by `CreateSubscription.CollectionMethod` via `PaymentCollectionMethod`): see `enums.md`.
- `SortingDirection`: `Asc`, `Desc` (from `enums.md`).

### Key request-model fields (summary — full field lists in record pages; never invent fields)

- `CreateCustomerRequest` → `.Customer` (`Customer`) — fields include `Reference (reference): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Organization (organization): string?`, `Id (id): int?`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, etc. (see `map/models/records-1-Ac-Cr.md`). Required: `.Customer` itself is `!req`; inner fields mostly optional (nullable).
- `CreateSubscriptionRequest` → `.Subscription` (`CreateSubscription`) — key for endpoint catalog use: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, `CustomerAttributes (customer_attributes): CustomerAttributes?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`. Required: `.Subscription` is `!req`; inside it the product identifier (handle or id) is required by the live API but not mechanically `required` in the generated record — confirm with live traffic / docs; sheet notes this as a contract gap.

---

## 3. Trap notes (do NOT resolve — load companion)

- ⚠ Step 1 (client) — retry/timeout options do **not** bound a whole call and are **not** the `HttpClient` timeout; `Timeout` is per-attempt; `MaxRetries` has a floor of 1. **MUST load `dotnet-configuration-resilience`** before wiring.
- ⚠ Step 2 (find/create) — `ReadCustomerByReference` is Case B (`RawError`); `CreateCustomer` is Case A (`CreateCustomerError`); do not assume same error type across the idempotent pair. **MUST load `dotnet-error-handling`**.
- ⚠ Step 2 (idempotency) — the caller must implement the lookup-then-create sequence; the SDK has no combined "find-or-create" operation. Whether to use `reference` or `id` for matching is the application's call — not in the map.
- ⚠ Step 4 (subscription) — `CreateSubscription` notes say product can be specified by `product_id` or `product_handle`, and price point by `product_price_point_handle`/`product_price_point_id`; whether to pass one or both, and whether `CustomerAttributes` vs `CustomerId`/`CustomerReference`, determines acceptance — only Notes + live wire confirm. **UNVERIFIED: exact required subset for a minimal accepted payload** — defensive directive: build with best-effort fields (product handle + customer reference + reference), fall back to generic error message if 422 returns `ErrorListResponse1`.
- ⚠ Step 5 (list subscriptions) — `ListSubscriptions` takes 14 nullable params, none with a C# default except `page`/`perPage`; mis-binding a positional call is easy. Use named args (`state:`, `product:`, `ct:`). **MUST load `dotnet-calling-endpoints`**.
- ⚠ Step 6 (catalog) — `ListProductsForProductFamily` takes a `string productFamilyId` that can be numeric id or `handle:` format; the parameter type does not distinguish. **MUST load `dotnet-calling-endpoints`** and validate your identifier format before calling.
- ⚠ All steps (models) — unions / `OneOf` fields (e.g., some component IDs) use factory methods + `TryGet…`; no `new`. **MUST load `dotnet-models`** before constructing `CreateSubscriptionComponent` or any union field.

---

## 4. REQUIRED READING (load before writing any code / error boundary)

- `dotnet-client-initialization` — client construction / DI / `HttpClient` lifetime.
- `dotnet-authentication` — Basic auth property names (`Username`, `Password`); load from config, not hardcoded.
- `dotnet-calling-endpoints` — named-arg requirement (`ct:`); response-envelope read-down (`SubscriptionResponse.Subscription`); pagination (`page`/`perPage`); query-param wire-name mapping.
- `dotnet-models` — `StringEnum<T>` vs C# enum; `required` / `init` / nullable; union factories; wire names vs C# names.
- `dotnet-configuration-resilience` — `RetryOptions`, `Timeout` (per-attempt), `HttpMethodsToRetry`, `BaseUrl` override, retry floor = 1.
- `dotnet-error-handling` — Case A (`SdkException<{Op}Error>` + `TryGet…`) vs Case B (`SdkException<RawError>`); `TryGetRawError` not a catch-all on typed errors; boundary design.
- `dotnet-testing` — `HttpClient` constructor seam; match project framework/style.

**Mandatory error-boundary caveats (both `JsonException` directions — never omit):**

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

Assumptions (application decisions — not SDK contract):
- Caller identity / site subdomain come from app config; not in SDK.
- The idempotent "find or create" sequence (lookup by `reference` → create if missing) is implemented by the caller, not by a single SDK call.
- Whether to include `CustomerAttributes`, `PaymentProfileAttributes`, or use `CustomerReference` vs `CustomerId` on `CreateSubscription` is the caller's design — only the operation Notes constrain it.
- Catalog endpoint selection (`ReadProductFamily` vs `ListProductsForProductFamily`) depends on whether the caller knows the family id/handle ahead of time.

Blockers (must resolve before implementation / plan is incomplete):
- **UNVERIFIED — minimal `CreateSubscription` payload:** the generated `CreateSubscription` record does not mechanically mark `ProductHandle`/`ProductId` or `CustomerReference`/`CustomerId` as `required`, while the operation Notes say a product and a customer must be specified. Only live traffic / provider docs confirm which fields are required. Sheet directive: start with `ProductHandle` + `CustomerReference` + `Reference`; on 422 read `ErrorListResponse1`; never assume a 422 is always the same missing field.
- **BaseUrl / site subdomain:** must come from deployment config; if missing, the SDK defaults to the `US` production template with default subdomain (`subdomain`), which likely fails auth/404. Confirm config binding before first call.
- No `{Operation}Result` variants exist — every call throws; the caller must always wrap in `try/catch`. (Confirmed by `sdk-map.md` §Error-handling model and `dotnet-calling-endpoints` note.)

---

*No SDK source clone path appears in this file (rule). All contract facts cite map pages (`map/operations/*.md`, `map/models/*.md`, `sdk-map.md`) or are marked `YOUR CALL — not in the map` / `UNVERIFIED`. No project source was inspected (project-file-edit window not open; integration agent handles code after plan read).*
