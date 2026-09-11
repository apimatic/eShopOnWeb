# Maxio Advanced Billing .NET SDK — Integration Plan (eShopOnWeb)

Output path: `C:\claude-runs\t1fgali-maxio-sdk-oc-openrouterthinkingmachinesinklingsmallhigh-001\repo\maxio-plan.md` (dictated; not chosen by agent).
Status: PLAN ONLY — no project file edited (hard gate per `integrate-maxio`).

---

## 1. Scope & sequence

Integration target: `src/PublicApi` controllers exposing endpoints for subscription billing.
Sequence (implementation order, each names SDK operations it uses):

1. **Client registration / auth** — build `MaxioAdvancedBillingClient`, set sandbox site, basic auth, server override (see §4). Apps call only after gate passes.
2. **Customer identity** (`Customers`) — `CreateCustomer`, `ReadCustomerByReference` (lookup by app reference / email) to get `id` for linkage. `ListCustomers` for search (`q`).
3. **Product family / plans** (`ProductFamilies`, `Products`) — `ReadProductFamily`, `ListProductsForProductFamily`; `ListProducts`; `ReadProduct`; `ListProducts` / `ReadProductByHandle` for plan identification by handle (`eshop-pro` / `basic-plan`). Plan IDs given: 7126957, 7126958; family id 3023074; handle `eshop-subscribe`; metered component `api-call` id 3057195.
4. **Subscription create** (`Subscriptions`) — `CreateSubscription` with `product_id` / `product_handle` + `customer_id` / `customer_reference`; optionally include `customer_attributes` to co-create customer.
5. **Subscription list / find** (`Subscriptions`) — `ListSubscriptions` (filter by `product`, `state`, pagination), `FindSubscription` (by reference), `ReadSubscription`.
6. **My-subscriptions endpoint** (app-layer) — calls `ListSubscriptions` + `ReadSubscription` filtered to the calling customer identity (app resolves caller; SDK has no "current user" concept — `YOUR CALL — not in the map`).
7. **Component / metered usage** (`SubscriptionComponents` — not fully detailed here; reference only) — for `api-call` metered component 3057195, usage is submitted via `SubscriptionComponents` endpoints; exact operation names deferred to a follow-up plan when usage-ingest is required.

Sandbox / site configuration (contract facts only — app must set):
- Site (subdomain / production override): `cp-exp-1` (sandbox identifier from brief; SDK server-node uses `Site` property, see §4).
- Family handle: `eshop-subscribe` (id 3023074).
- Plan handles/ids: `eshop-pro` (7126957), `basic-plan` (7126958).
- Component: `api-call` (3057195), metered.
- Auth: Basic — `Username` = Maxio API key, `Password` = literal `"x"`. Source: `sdk-map.md` §Servers & auth; `BasicAuthCredentials.cs`.
- Environment: `ServerEnvironment.Us` (default) unless EU hosting. Source: `sdk-map.md`.
- Server group: Production (`https://{site}.chargify.com`). For sandbox / mock redirect, override `options.Server.Production.Us.BaseUrl` (or `.Us.Site`). Source: `sdk-map.md` §Servers & auth.
- Package / namespace: NuGet `AsadAli.AdvancedBilling.Sdk`; root namespace `MaxioAdvancedBilling`; operations in `MaxioAdvancedBilling.Api`; records `MaxioAdvancedBilling.Models`; enums `MaxioAdvancedBilling.Models.Enums`; errors `MaxioAdvancedBilling.Errors`; config `MaxioAdvancedBilling.Core.Configuration`. Source: `sdk-map.md` §Models / Namespaces.

**Blockers / assumptions (see §5).**

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 Client construction / auth / server (source: `sdk-map.md` §Getting a client / Servers & auth; `MaxioAdvancedBillingClientOptions.cs`; `Core/Authentication/Basic/BasicAuthCredentials.cs`; `Servers/ServerEnvironment.cs`; `Servers/ServerOptions.cs` / `ProductionOptions.cs`)

| Fact | Value / Type | Source / notes |
|---|---|---|
| Client constructor | `MaxioAdvancedBillingClient(HttpClient httpClient, MaxioAdvancedBillingClientOptions options)` | `sdk-map.md` line 38; root namespace `MaxioAdvancedBilling`; `HttpClient` is caller-owned — `YOUR CALL — not in the map` whether to register singleton / scoped / transient |
| Options type | `MaxioAdvancedBillingClientOptions` (`MaxioAdvancedBilling`) | `sdk-map.md` |
| Auth | `BasicAuthCredentials { Username = "<api_key>", Password = "x" }` — `Username` is the Maxio API key, `Password` literal `"x"` | `sdk-map.md` lines 201–203 |
| Environment | `ServerEnvironment` (`MaxioAdvancedBilling.Servers`) — members `Us`, `Eu`; default `Us` | `sdk-map.md` lines 205–210 |
| Server group (production) | `options.Server.Production.Us.BaseUrl` / `.Us.Site`; template `https://{site}.chargify.com` (US), `https://{site}.ebilling.maxio.com` (EU) | `sdk-map.md` lines 214–223 |
| Retry | `RetryOptions` (`MaxioAdvancedBilling.Core.Configuration`) — `required` members, build full or `RetryOptions.Default()`; includes `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry` | `sdk-map.md` lines 63–77 |
| No `…Result` variants exist | Every operation is throw-only | `sdk-map.md` line 118 |

### 2.2 Operations — Customers (`client.Customers`; source `Api/Customers.cs`; `map/operations/Customers.md`)

| Controller prop | Method signature (literal params, order, nullables) | Request model (`MaxioAdvancedBilling.Models`) + key fields (wire names) | Response envelope + inner payload | Error (case) + accessors + payload type | Pagination | Source page |
|---|---|---|---|---|---|---|
| `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` nullable, no default → must pass explicitly | `CreateCustomerRequest` (record, `Models/CreateCustomerRequest.cs`) — fields include customer identity (`first_name`, `last_name`, `email`, `reference`, `organization`, `country` 2-char ISO, `state` 2/3-char, `locale`, etc.); see record page for full `!req` list | `CustomerResponse` (`Models/CustomerResponse.cs`) — single field `Customer (customer): Customer !req` | **Case A** `SdkException<CreateCustomerError>` — `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` fallback | none | `Customers.md` lines 7–16; `records-1-Ac-Cr.md` line 125 |
| `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` nullable, no default | — | `CustomerResponse` (same envelope) | **Case B** `SdkException<RawError>` — `StatusCode`, `ReadAsString()`, etc. | none | `Customers.md` lines 61–69 |
| `client.Customers` | `ListCustomers(SortingDirection? direction, BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, string? q, int? page = 1, int? perPage = 50, CancellationToken ct = default)` — 7 filter params + pagination; all nullable, no default → pass `null` to skip; defaults `page`=1, `perPage`=50 | — (filter via query) | `IReadOnlyList<CustomerResponse>` (list of envelopes) | **Case B** `SdkException<RawError>` | manual `page`+`perPage`; query `direction`/`page`/`per_page`/`date_field`/`start_date`/`end_date`/`start_datetime`/`end_datetime`/`q` | `Customers.md` lines 38–49 |
| `client.Customers` | `ReadCustomer(int id, CancellationToken ct = default)` | — | `CustomerResponse` | **Case B** `SdkException<RawError>` | none | `Customers.md` lines 51–59 |

**Notes for customer create (from `Customers.md` line 9):** reference must be unique if provided; represents app's own customer ID; country 2-char ISO; state codes per country; locale for invoice language.

### 2.3 Operations — Subscriptions (`client.Subscriptions`; source `Api/Subscriptions.cs`; `map/operations/Subscriptions.md`)

| Controller prop | Method signature | Request model + key fields | Response envelope | Error (case) + accessors + payload | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` nullable, must pass | `CreateSubscriptionRequest` (`Models/CreateSubscriptionRequest.cs`; `records-*`) — `product_id`/`product_handle`, `customer_id`/`customer_reference`; optional `customer_attributes` (co-create), `payment_profile_id`, `subscription_components`, `coupon`, etc. | `SubscriptionResponse` (`Subscription (subscription): Subscription !req`) | **Case A** `SdkException<CreateSubscriptionError>` — `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError` fallback | none | `Subscriptions.md` 31–40 |
| `client.Subscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string,string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 filter params; all nullable no default → `null` skips; defaults `page`=1, `perPage`=20 | — | `IReadOnlyList<SubscriptionResponse>` | **Case B** `SdkException<RawError>` | manual `page`+`perPage`; query params mapped in row | `Subscriptions.md` 54–65 |
| `client.Subscriptions` | `FindSubscription(string? reference, CancellationToken ct = default)` — `reference` nullable, must pass | — | `SubscriptionResponse` | **Case A** `SdkException<FindSubscriptionError>` — `TryGetNoContent(out RawError)` [404] · `TryGetRawError` fallback | none | `Subscriptions.md` 42–52 |
| `client.Subscriptions` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `include` nullable must pass | — | `SubscriptionResponse` | **Case B** `SdkException<RawError>` | none | `Subscriptions.md` 101–111 |

**Notes from `Subscriptions.md` (Create, line 33):** specify product by `product_id` or `product_handle`; price point by `product_price_point_handle`/`_id`; customer by `customer_id` or `customer_reference`; co-create customer via `customer_attributes`; payment info required depends on product options; 3DS post-auth flows raise 422 with `action_link`. No-throw variant absent.

### 2.4 Operations — Product Families (`client.ProductFamilies`; source `Api/ProductFamilies.cs`; `map/operations/ProductFamilies.md`)

| Controller prop | Method signature | Request | Response envelope | Error | Pagination | Source |
|---|---|---|---|---|---|---|
| `client.ProductFamilies` | `ReadProductFamily(int id, CancellationToken ct = default)` | — | `ProductFamilyResponse` (`ProductFamily (product_family): ProductFamily !req`) | **Case B** `SdkException<RawError>` | none | `ProductFamilies.md` 43–51 |
| `client.ProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` — 5 nullable filter params, must pass explicitly | — | `IReadOnlyList<ProductFamilyResponse>` | **Case B** `SdkException<RawError>` | none | `ProductFamilies.md` 18–28 |
| `client.ProductFamilies` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 filter params, must pass; defaults paginated | — | `IReadOnlyList<ProductResponse>` (`Product (product): Product !req`) | **Case A** `SdkException<ListProductsForProductFamilyError>` — `TryGetString(out string)` [404] · `TryGetRawError` fallback | manual `page`+`perPage` | `ProductFamilies.md` 30–41 |

**Plan IDs / handles:** family id `3023074` (`eshop-subscribe`); products/plans `7126957` (`eshop-pro`) and `7126958` (`basic-plan`). Read family by id or handle; list products inside family; read individual product by `product_id` via `client.Products.ReadProduct(int productId, ct)` (`Products.md` 41–49) — **Case B** `SdkException<RawError>`.

### 2.5 Operations — Products / Price Points (for plan identification; source `Api/Products.cs`; `map/operations/Products.md`)

| Controller prop | Signature | Response envelope | Error | Pagination | Source |
|---|---|---|---|---|---|
| `client.Products` | `ReadProduct(int productId, CancellationToken ct = default)` | `ProductResponse` | **Case B** `SdkException<RawError>` | none | `Products.md` 41–49 |
| `client.Products` | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` | `ProductResponse` | **Case B** `SdkException<RawError>` | none | `Products.md` 51–59 |
| `client.Products` | `ListProducts(...)` (8 filter params; `page`=1, `perPage`=20) | `IReadOnlyList<ProductResponse>` | **Case B** | manual | `Products.md` 28–39 |

Plan/price-point details (e.g., which price point is default) come from `ProductPricePoints` (`client.ProductPricePoints`); not fully scoped here — `YOUR CALL — not in the map` whether the endpoint needs price-point-level selection or just product-level.

### 2.6 Enum value tables (namespace `MaxioAdvancedBilling.Models.Enums`; source `map/models/enums.md`; construct via static members or `FromValue` — NOT C# enum)

| Enum | Members (literal C# name → wire) | Used by |
|---|---|---|
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` | `ListCustomers`, `ListSubscriptions`, `ListProductFamilies`, etc. |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | List filters |
| `SubscriptionStateFilter` | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` | `ListSubscriptions` `state` param |
| `SubscriptionListInclude` | `SelfServicePageToken (self_service_page_token)` | `ListSubscriptions` `include` |
| `SubscriptionInclude` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | `ReadSubscription` `include` |
| `SubscriptionSort` | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` | `ListSubscriptions` `sort` |
| `SubscriptionDateField` | `CurrentPeriodEndsAt (current_period_ends_at)`, `CurrentPeriodStartsAt (current_period_starts_at)`, `CreatedAt (created_at)`, `ActivatedAt (activated_at)`, `CanceledAt (canceled_at)`, `ExpiresAt (expires_at)`, `TrialStartedAt (trial_started_at)`, `TrialEndedAt (trial_ended_at)`, `UpdatedAt (updated_at)` | `ListSubscriptions` `dateField` |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | Subscription/model fields |
| `IntervalUnit` | `Day (day)`, `Month (month)` | Component/price-point fields |
| `ComponentKind` | `MeteredComponent (metered_component)`, `QuantityBasedComponent (quantity_based_component)`, `OnOffComponent (on_off_component)`, `PrepaidUsageComponent (prepaid_usage_component)`, `EventBasedComponent (event_based_component)` | Component identification (metered `api-call`) |

---

## 3. Trap notes (named hazard + `MUST load <skill>`; never resolved inline)

- **Step 2 (customer identity / subscription create)** — `CreateSubscription`'s `body` is nullable but **must be passed explicitly**; passing `null` will compile but the call may be rejected (Notes describe required fields). Must read `Subscriptions.md` Notes + `CreateSubscriptionRequest` source for required fields (e.g., `product_id`/`handle`, `customer_ref`/`id`). **MUST load `dotnet-calling-endpoints`** before writing the call.
- **Step 2 / 5 (request payloads)** — request records use `init`-only setters; `required` properties (`!req`) must be set in object initializer; wire names (`[JsonPropertyName]`) differ from C# names (e.g., `product_id`). Wrong property name = compile or wrong wire payload. **MUST load `dotnet-models`** before constructing payloads.
- **Step 3 (client registration)** — `RetryOptions` members are `required`; `Timeout` is a `TimeSpan?` that does **not** bound the `HttpClient` lifetime — the SDK retry timeout is separate from any `HttpClient.Timeout` the app configures. **MUST load `dotnet-configuration-resilience`** before wiring `Retry` / `HttpClient`.
- **Step 2 (auth)** — `BasicAuthCredentials` lives in `MaxioAdvancedBilling.Core.Authentication.Basic`; `Username` = API key, `Password` = literal `"x"`. 401/403 → verify this first, before changing call code. **MUST load `dotnet-authentication`**.
- **Step 1 (DI / client lifetime)** — SDK constructor takes `HttpClient`; whether to register `HttpClient` singleton / use `IHttpClientFactory` / manage `MaxioAdvancedBillingClient` lifetime is the app's design, not the SDK's. **MUST load `dotnet-client-initialization`**.
- **Step 4 / 7 (error boundary)** — every operation throws `SdkException<T>`; access payload via typed accessors (`TryGet…`) or `TryGetRawError`. Non-2xx bodies that don't match the operation's `…Error` shape throw `JsonException` during error-object construction, destroying the HTTP status — boundary must handle both `SdkException` and `JsonException` differently. **MUST load `dotnet-error-handling`** before writing catch ladders (see §4). Also `System.Text.Json.JsonException` reaches boundary from 2xx (missing `required` member) and from bad 2xx error bodies — opposite handling needed.
- **Step 6 (tests)** — which seam to fake (the `HttpClient` / `MaxioAdvancedBillingClient` interface?) and how to assert on error paths without relying on SDK internals. **MUST load `dotnet-testing`**.

---

## 4. REQUIRED READING (load BEFORE implementation starts)

List every `dotnet-*` skill named in trap notes / contract requirements, with the step it governs:

- `dotnet-authentication` — Step 1 (auth wiring), also any 401/403 fix.
- `dotnet-client-initialization` — Step 1 (client construction / DI / `HttpClient` ownership).
- `dotnet-configuration-resilience` — Step 1 (retry / timeout / server-node / pagination semantics; verify `Timeout` meaning before setting).
- `dotnet-calling-endpoints` — Step 2/3/4/5 (finding controller, parameter rules, cancellation token named `ct`, response envelope depth).
- `dotnet-models` — Step 2 (request/response records, `required`, wire names, enums, unions, envelope shapes).
- `dotnet-error-handling` — Step 4 (error boundary; both `JsonException` directions; `SdkException<T>` accessors). **Mandatory even if no others named** — every integration writes a boundary.
- `dotnet-testing` — Step 7 (seam choice, error-path assertions).

Both `System.Text.Json.JsonException` hazard rows (mandatory verbatim from contract rules):

- A drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary.
- A **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

---

## 5. Assumptions & Blockers

**Assumptions (user intent / app design — these are `YOUR CALL — not in the map` decisions for the implementer):**

- `Caller identity` (who is the customer / which reference / email to look up) — resolved from app's own identity path (e.g., ASP.NET Core `User` / session / JWT claim); SDK has no auth/user concept.
- `Persistence` (where customer reference → Maxio id mapping is stored, how subscription state is cached) — app's design; SDK only provides the call surface.
- `Concurrency rules` for `my-subscriptions` (whether to allow concurrent create/update, how to handle race on reference) — `YOUR CALL — not in the map`.
- `Endpoint paths` under `src/PublicApi` (names, route templates, HTTP verbs) — dictated by brief (`subscription plans`, `subscriptions`, `my-subscriptions`) but exact routes / DTO names not in SDK; design belongs to implementer.
- `Metered usage ingestion` (`api-call` 3057195) — the brief names the component but does not specify which `SubscriptionComponents` operation(s) submit usage (e.g., event-based vs usage-based endpoints). Blocker only if usage must be implemented now; otherwise deferred.

**Blockers (stop planning / require resolution before coding):**

- **None that stop this plan** — all operations in scope have map rows; signatures, envelopes, error accessors, enum values are all cited. The one gap (`SubscriptionComponents` specific usage-ingest operation for metered `api-call`) is noted above and labeled deferred, not invented.
- If live traffic shows `CreateSubscription` payloads differ from generated `CreateSubscriptionRequest` model (e.g., provider requires fields the model omits), treat as `UNVERIFIED` and add defensive-coding directive: "extract best-effort from `CreateSubscriptionRequest`, fall back to generic message / manual payload construction" — but only after traffic observation. Currently unverified; do not invent fields.

---

## 6. Source index (every contract row above cites a map page; no clone path appears; clone never left system temp, never in this file)

- Client/auth/server: `sdk-map.md` (lines 24–225)
- Customers operations: `map/operations/Customers.md`
- Subscriptions operations: `map/operations/Subscriptions.md`
- ProductFamilies operations: `map/operations/ProductFamilies.md`
- Products operations: `map/operations/Products.md`
- Request/response records: `map/models/records-1-Ac-Cr.md`, `records-2-Cr-Ne.md`, `records-3-Of-Su.md`, `records-4-Su-We.md` (specific names cited per row)
- Enums: `map/models/enums.md`
- Unions: `map/models/unions.md` (if `Quantity`/`PreviousQuantity` etc. needed for component allocations — deferred)
- Error core / accessors: `sdk-map.md` §Error-handling model (lines 81–116)
- Companion skills: `dotnet-authentication`, `dotnet-calling-endpoints`, `dotnet-client-initialization`, `dotnet-configuration-resilience`, `dotnet-error-handling`, `dotnet-models`, `dotnet-testing` (all listed in `.opencode/skills/`)

---

Return: file written to `C:\claude-runs\t1fgali-maxio-sdk-oc-openriverthinkingmachinesinklingsmallhigh-001\repo\maxio-plan.md`. No project file edited; clone path absent; all contract facts sourced from map pages read this session.
