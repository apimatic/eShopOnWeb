# Maxio Advanced Billing — SDK integration plan (eShopOnWeb)

Plan path (dictated by brief): `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-002\repo\maxio-plan.md`

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

## 1. Scope & sequence

Steps in order; each names the operation(s) used.

1. Client registration + auth (DI in `Program.cs` / `Startup`) — `MaxioAdvancedBillingClient`, `MaxioAdvancedBillingClientOptions`, `BasicAuthCredentials`, `ServerEnvironment`.
2. Find-or-create customer by reference — `client.Customers.ReadCustomerByReference` / `CreateCustomer` / `ListCustomers` (search `q`).
3. List product family + plans — `client.ProductFamilies.ListProductFamilies` / `ListProductsForProductFamily`; also `client.Subscriptions` for later.
4. Create subscription — `client.Subscriptions.CreateSubscription` with `CreateSubscriptionRequest`; include metered component via `Components` if needed (`api-call` id ~3057195 — use handle/ID per map notes; handle preferred since IDs unstable).
5. List customer subscriptions — `client.Customers.ListCustomerSubscriptions`.

No `No-throw` (`.Result`) variants exist in this SDK — every call is throw-only.

## 2. CONTRACT SHEET

Source pages: `maxio-getting-started/sdk-map.md`; `map/operations/Customers.md`; `map/operations/Subscriptions.md`; `map/operations/ProductFamilies.md`; `map/operations/Products.md`; `map/models/records-2-Cr-Ne.md` (CreateSubscription fields); `map/models/enums.md` for filters/enums.

### Client / auth (required for every call)

| Item | Fully-qualified form | Source note |
|---|---|---|
| Client constructor | `MaxioAdvancedBilling.MaxioAdvancedBillingClient(System.Net.Http.HttpClient, MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions)` | `sdk-map.md` |
| Options type | `MaxioAdvancedBilling.MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Auth property on options | `BasicAuth` (`MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md`; username = API key, password = literal `"x"` |
| Environment | `MaxioAdvancedBilling.Servers.ServerEnvironment` (default `Us`) | `sdk-map.md` |
| Retry | `MaxioAdvancedBilling.Core.Configuration.RetryOptions` (all members required; use `RetryOptions.Default()` or build full) | `sdk-map.md` |

### Operations

| Controller / accessor | Signature (param order + types) | Request body (nullable unless noted) | Response envelope + inner resource | Error case | Pagination / notes |
|---|---|---|---|---|---|
| `client.Customers` — `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | none | `MaxioAdvancedBilling.Models.CustomerResponse` → `.Customer` (`Customer` !req) | B (`SdkException<RawError>`) | `map/operations/Customers.md` |
| `client.Customers` — `CreateCustomer` | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` — **must pass explicitly** | `CreateCustomerRequest` (wraps `CustomerAttributes` / fields); see `records-2-Cr-Ne.md` for `CustomerAttributes` fields (`first_name`, `last_name`, `email`, `reference`, `organization`, `address`, etc.) | `CustomerResponse` → `.Customer` | A (`SdkException<CreateCustomerError>`) — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError` [fallback] | `map/operations/Customers.md`; `reference` must be unique |
| `client.Customers` — `ListCustomers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 nullable params must be passed (use `null`) | none | `IReadOnlyList<CustomerResponse>` (each `.Customer`) | B (`SdkException<RawError>`) | manual `page`/`perPage`; use `q` to search by `reference` / email / org / id |
| `client.Customers` — `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | none | `IReadOnlyList<SubscriptionResponse>` (each `.Subscription`) | B (`SdkException<RawError>`) | `map/operations/Customers.md` |
| `client.ProductFamilies` — `ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | none | `IReadOnlyList<ProductFamilyResponse>` (each `.ProductFamily`) | B (`SdkException<RawError>`) | `map/operations/ProductFamilies.md` |
| `client.ProductFamilies` — `ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | none | `IReadOnlyList<ProductResponse>` (each `.Product`) | A (`SdkException<ListProductsForProductFamilyError>`) — `TryGetString(out string)` [404], `TryGetRawError` | manual pagination; `productFamilyId` can be handle (`eshop-subscribe`) per `ReadProductFamily` notes |
| `client.Subscriptions` — `CreateSubscription` | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` — **must pass explicitly** | `CreateSubscriptionRequest` wraps `Subscription` (`CreateSubscription`) — key fields: `product_handle` / `product_id`, `product_price_point_handle` / `id`, `customer_id` / `customer_reference`, `components` (`IReadOnlyList<CreateSubscriptionComponent>?`), `reference`, `payment_profile_attributes`, `credit_card_attributes`, etc. Full fields in `map/models/records-2-Cr-Ne.md` line 21 | `SubscriptionResponse` → `.Subscription` (`Subscription` !req) | A (`SdkException<CreateSubscriptionError>`) — `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError` [fallback] | `map/operations/Subscriptions.md`; Notes say specify product by `product_id` or `product_handle`; customer by `customer_id` or `customer_reference`; metered component `api-call` (id ~3057195) added via `Components` array with `component_id` / `quantity` / `enabled` |
| `client.Subscriptions` — `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, ... IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — many nullable params | none | `IReadOnlyList<SubscriptionResponse>` | B (`SdkException<RawError>`) | `map/operations/Subscriptions.md`; can filter by `product`, `customer_reference` via metadata/query not needed if using `ListCustomerSubscriptions` instead |

### Request / response model notes (from map, not memory)

- `CreateSubscriptionRequest` (namespace `MaxioAdvancedBilling.Models`) has exactly one required field: `Subscription` (`CreateSubscription` !req) — wire name `subscription`. Source: `map/models/records-2-Cr-Ne.md` line 21.
- `CreateSubscription` fields include `product_handle` (`string?`), `product_id` (`int?`), `customer_id` (`int?`), `customer_reference` (`string?`), `components` (`IReadOnlyList<CreateSubscriptionComponent>?`), `reference` (`string?`), `payment_profile_attributes`, `credit_card_attributes`, etc. Source: `map/models/records-2-Cr-Ne.md` line 17.
- `CreateSubscriptionComponent` fields: `component_id` (`ComponentId1?` union), `enabled` (`bool?`), `quantity` (`int?`), `price_point_id` (`PricePointId2?` union), `custom_price` (`ComponentCustomPrice?`). Source same page line 18.
- `CustomerResponse` = `{ Customer (customer): Customer !req }`. `SubscriptionResponse` = `{ Subscription (subscription): Subscription !req }`. Source via `map/models/records-2-Cr-Ne.md` lines 43 and search for SubscriptionResponse.
- All response payload properties are accessed one level down: `resp.Customer`, `resp.Subscription`.
- `BasicDateField`, `SortingDirection`, `SubscriptionStateFilter`, `SubscriptionListInclude`, `ListProductsFilter`, `ListProductsInclude` — enums/values from `map/models/enums.md`; load that file before constructing filter values.

### Auth / environment binding keys

- Option property: `BasicAuth` (not `ApiKey`). Value: `new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials { Username = "...", Password = "x" }`.
- Option property: `Environment` (type `MaxioAdvancedBilling.Servers.ServerEnvironment`); default `Us`. Sandbox `cp-exp-1` is likely US; confirm with site config, do NOT assume EU.
- Client registration via `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ...; })` is available per `sdk-map.md` (DI alternative).

## 3. Required reading (load BEFORE implementing)

These are the companion skills named by the map and the integration gate; load them, do not copy their contents into this sheet.

- `maxio-getting-started` (mandatory first — identity, package `AsadAli.AdvancedBilling.Sdk`, namespace `MaxioAdvancedBilling`, map boundary, source-clone rules) — load now, already loaded.
- `dotnet-authentication` — governs `BasicAuthCredentials`, password literal `"x"`, 401/403 diagnosis.
- `dotnet-client-initialization` — `MaxioAdvancedBillingClient` construction, `HttpClient` lifetime / ownership, DI `AddMaxioAdvancedBillingClient`, `ServerEnvironment`.
- `dotnet-calling-endpoints` — parameter order, nullable-passing rule (`must pass explicitly` for nullable-no-default params, pass `null` to skip), `ct:` naming, response envelope read-down (`.Customer`, `.Subscription`), pagination `page`/`perPage`.
- `dotnet-models` — `CreateSubscriptionRequest` / `CreateSubscription` construction, `!req` vs `?`, union access (`ComponentId1` etc.), wire-name vs C# property, `required` members in initializers.
- `dotnet-configuration-resilience` — `RetryOptions` (all members required), timeout semantics (what `Timeout` actually bounds — per the skill, not assumed), `ServerOptions`, base-URL / server-node selection.
- `dotnet-error-handling` — Case A (`SdkException<CreateCustomerError>` with `TryGet…`) vs Case B (`SdkException<RawError>`), both `JsonException` directions (2xx malformed body AND non-2xx body that mismatches `{Op}Error`), and why never parse `.ToString()` when an accessor exists.
- `dotnet-testing` — seam to fake (`HttpClient` / `IMaxioAdvancedBillingClient` if exposed; otherwise use `HttpMessageHandler` mock); cover 422 / 404 / 401 paths; keep tests independent of SDK internals.
- `integrate-maxio` — the five binding gates (plan-file path dictated, no project edits during agent, plan must exist before code, load all named `dotnet-*` skills, map boundary).

## 4. Trap notes (hazard + consequence; do NOT resolve)

- ⚠ Step 1 (client registration) — `RetryOptions` members are all `required`; building partial instance breaks. `Timeout` does NOT bound the whole call (it bounds per-attempt); `HttpClient.Timeout` is separate. **MUST load `dotnet-configuration-resilience`** before wiring retries / server node.
- ⚠ Step 2 (find customer) — `ReadCustomerByReference` takes `string reference`; if missing, it throws `SdkException<RawError>` (404 via Case B) — use `TryGetRawError` to read `StatusCode`, not exception message. `ListCustomers` requires passing all 7 nullable params explicitly (`null` to skip); forgetting one is a build error because no default is declared for the first seven. **MUST load `dotnet-calling-endpoints`** before writing the query call.
- ⚠ Step 2 (create customer) — `CreateCustomer` error is Case A (`CreateCustomerError`) with `TryGetCustomerErrorResponse1` (422); any other status falls through `TryGetRawError`; do not assume 422 is the only failure. `reference` must be unique — if duplicate, provider rejects; the map notes this but not which exact error shape — label unverified for duplicate-reference payload shape. **MUST load `dotnet-error-handling`** before writing the catch ladder.
- ⚠ Step 3 (list plans) — `ListProductsForProductFamily` takes `string productFamilyId` (handle format `eshop-subscribe` accepted per `ReadProductFamily` notes); IDs are unstable (`~3023074` etc.) — prefer handle. Response wraps `ProductResponse`; read `.Product`. **MUST load `dotnet-models`** before constructing filter enums.
- ⚠ Step 4 (create subscription) — `CreateSubscriptionRequest` has exactly one required field (`Subscription`); passing a bare `CreateSubscription` is wrong — must wrap. Payment info may be required depending on product options (map Notes say “Payment information may be required… depends on the options for the Product being subscribed”). This is a provider-level gate; the SDK contract cannot tell you whether your `Pro Plan` requires a card — treat as BLOCKER until verified against live site options. Component addition (`api-call`) uses `CreateSubscriptionComponent`; union fields (`component_id`, `price_point_id`) must be constructed via the union factory, not raw `int`. **MUST load `dotnet-models`** before building payload.
- ⚠ Step 5 (list subscriptions) — `ListCustomerSubscriptions` returns `IReadOnlyList<SubscriptionResponse>` (not a paginated envelope with `total_entries`) — paginate via `page`/`perPage` on `ListSubscriptions` if needed, but this path is per-customer and unpaginated per map. **MUST load `dotnet-calling-endpoints`**.
- ⚠ Boundary (applies to every step) — `System.Text.Json.JsonException` reaches the boundary from two directions with opposite handling: (a) drifted/malformed 2xx body (e.g. missing `required` member on `SubscriptionResponse`) surfaces as `JsonException` from deserialization, not `SdkException`; (b) non-2xx body that does not match `{Op}Error` throws `JsonException` while constructing the error object, replacing `SdkException` and destroying the HTTP status. A catch ladder that catches only `SdkException<...>` lets (a) escape; mapping every `JsonException` to 5xx turns deterministic rejections into false outages; retrying 5xx retries something never succeeding. **MUST load `dotnet-error-handling`** before writing any boundary.

## 5. Assumptions & Blockers

### Assumptions (user intent / sandbox setup — these are YOUR CALL, not contract)

- Sandbox site is `cp-exp-1`; handles (`eshop-subscribe`, `eshop-pro`, `basic-plan`, `api-call`) are stable, IDs (~3023074, ~7126957, ~7126958, ~3057195) are unstable per brief — plan uses handles where supported (`product_handle`, `customer_reference`), falls back to IDs only if handle unsupported by the specific endpoint (checked: `CreateSubscription` supports both; `ListProductsForProductFamily` accepts handle per `ReadProductFamily` notes; `ReadProductFamily` accepts `id` or `handle:id` format).
- Customer lookup key is the app’s own `reference` value (stored in `CustomerAttributes.Reference` / `CreateSubscription.CustomerReference` / `CreateSubscriptionRequest` fields); the integration treats `reference` as the canonical external identifier.
- The metered component `api-call` ($0.01/unit) is added via `CreateSubscriptionComponent` inside `CreateSubscription.Components`; quantity and enabled state will be set by the implementer at call time (not specified in brief).
- The project is ASP.NET Core (`PublicApi` / `Infrastructure` / `ApplicationCore`); DI registration will happen in `PublicApi` / `Program.cs`; no CLI / background worker scope is in this brief.
- No real card details are used for testing (per SDK Notes); if the sandbox product requires a payment profile and the integration does not supply one, the call will be rejected — that is a provider validation, not an SDK bug.
- Package `AsadAli.AdvancedBilling.Sdk` is not yet referenced in any `.csproj` (`grep` over all `.csproj` returned nothing); installation is a required first code step, not a contract step.

### Blockers (must resolve before implementation / live verification)

- **B1 — Product payment requirement unknown**: `CreateSubscription` Notes state “Payment information may be required to create a subscription, depending on the options for the Product being subscribed.” For `eshop-pro` / `basic-plan` / `eshop-subscribe`, we do not know (from map or brief) whether a `payment_profile_attributes` / `credit_card_attributes` block is required. The SDK contract cannot tell us. **Resolution needed**: either confirm with sandbox UI / live test, or design defensively (build payload with optional payment attributes, catch 422 via `TryGetErrorListResponse1`, and let provider reject if missing). Label `UNVERIFIED` for the required-payment flag.
- **B2 — Component union construction**: `CreateSubscriptionComponent.ComponentId` is `ComponentId1?` (union). The exact factory/accessor names for this union are in `map/models/unions.md` (not fully read this session). Before constructing `Components`, load `unions.md` or confirm source file `Models/ComponentId1.cs` in clone. **Not a blocker if the implementer uses handle string via union factory; if attempting raw `int`, build breaks.** Resolve via `dotnet-models` / clone before writing payload.
- **B3 — Customer reference uniqueness on create**: `CreateCustomer` Notes say “you may only create one customer for a given reference value.” If the integration tries to create a customer that already exists (e.g. after a partial failure), it receives 422 (`TryGetCustomerErrorResponse1`). The integration must either (a) always try `ReadCustomerByReference` first, or (b) handle 422 as “already exists” and switch to read. This is an application-design decision, not SDK-set; document in implementer notes.
- **B4 — Environment / server node for `cp-exp-1`**: `ServerEnvironment.Us` is the SDK default; if `cp-exp-1` is EU-hosted, auth will fail (401/403 or wrong host) regardless of key. Confirm environment from sandbox settings before binding `Environment`; if unknown, use defensive config (bind from app settings, default to `Us`, log actual server URL from `ServerOptions` if exposed). **Not a contract gap — a deployment config gap.**

### Unverified (only live traffic can confirm)

- Whether the live wire payload for `CreateSubscription` with metered component exactly matches `CreateSubscriptionComponent` shape (particularly whether `enabled` or `quantity` are required when component is included, and whether `price_point_id` is required for metered). Design defensively: provide `component_id` (handle/ID), `enabled: true`, `quantity: 0` (or intended), omit optional union fields unless needed, and fall back to generic message / `TryGetRawError` if 422 payload does not match expected `ErrorListResponse1`. Label `UNVERIFIED`.
- Whether `cp-exp-1` requires `payment_profile_attributes` for `basic-plan` / `eshop-pro`. Treat as unverified; build with optional payload, handle 422.

---

No project file edited; no clone path included (clone, if needed for union source, must stay in `C:\claude-runs\t1ocaliusman-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-002\tmp\opencode` per rules and never appear in `maxio-plan.md` or replies).
