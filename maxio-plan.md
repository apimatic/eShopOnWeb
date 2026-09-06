# Maxio Advanced Billing Integration Plan — eShopOnWeb Recurring Subscriptions

## Scope & Sequence

1. **API infrastructure** — HTTP client registration, Maxio SDK client initialization, basic auth credentials from configuration
2. **Customer management** — ensure Maxio customer exists (idempotent via `reference` field tying to app user ID)
3. **Plan browsing** — list available subscription plans by product family and price point
4. **Subscription creation** — create and activate a subscription for a customer on a selected plan
5. **Subscription reading** — fetch subscription details (state, next-billing-date, price, plan info)
6. **Subscription listing** — list active subscriptions for a logged-in user

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Step 1: Read/Lookup Customer by Reference (Idempotent)

| Item | Detail |
|---|---|
| **Controller** | `client.Customers` |
| **Method** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Parameters** | `reference` (C# literal field name) — user's unique ID from eShopOnWeb, passed as query param `reference`; required |
| **Request body** | none |
| **Response** | `CustomerResponse` (namespace `MaxioAdvancedBilling.Models`) → field `Customer (customer): Customer !req` — unwrap to read ID |
| **Error** | `SdkException<RawError>` (Case B) — 404 if not found, 200 if found |
| **Pagination** | none |
| **Notes** | **404 expected on first call** (customer does not yet exist). Caller must catch and create. Wire name: `reference` ← C# `reference`. |
| **Source** | `map/operations/Customers.md` |

### Step 2: Create Customer (Called if Lookup Returns 404)

| Item | Detail |
|---|---|
| **Controller** | `client.Customers` |
| **Method** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` — nullable, no default → **must pass explicitly** |
| **Request model** | `CreateCustomerRequest` (namespace `MaxioAdvancedBilling.Models`) |
| **Request fields** | `Subscription (subscription): CreateCustomer !req` — wrap all fields in the required `CreateCustomer` inner record. Inner fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` (wire: `reference`), `Organization (organization): string?`, `Address (address): string?`, `City (city): string?`, `State (state): string?`, `Zip (zip): string?`, `Country (country): string?` — all strings. **Field name in outer record is literally `Subscription` (the envelope wrapper).** |
| **Response** | `CustomerResponse` (namespace `MaxioAdvancedBilling.Models`) → field `Customer (customer): Customer !req` — extract to get `Id` and other fields |
| **Error** | `SdkException<CreateCustomerError>` (Case A) — accessor `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] or `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | none |
| **Notes** | Wrapping in `CreateCustomerRequest` is mandatory (`!req` on `Subscription` field). Use `Reference` field to store the app's user ID for future lookups (idempotent). |
| **Source** | `map/operations/Customers.md`, `map/models/records-2-Cr-Ne.md` |

### Step 3: List Available Plans (Products + Price Points by Family Handle)

**Option A: List all products in family, then fetch price points per product**

| Item | Detail |
|---|---|
| **Controller** | `client.Products` |
| **Method** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | All optional before `page`; pass `null` for ones not used. Filter by product family using `ListProductsFilter` (see models). Set `page` and `perPage` explicitly. |
| **Request model** | None (query params only) |
| **Query params** | `filter` maps to `filter`, `page` to `page`, `per_page` to `perPage`, etc. — see operation signature. To filter by product family, construct `ListProductsFilter` with `Ids` set to known product IDs in the family (or list all and filter in code). **Note:** SDK has no built-in "product family" filter in this operation. Filter client-side by product-family ID or use hard-coded product IDs. |
| **Response** | `IReadOnlyList<ProductResponse>` (namespace `MaxioAdvancedBilling.Models`) — unwrap each to read `Product` record: fields `Id (id): int?`, `Handle (handle): string?`, `Name (name): string?`, `Description (description): string?`, `DefaultProductPricePointId (default_product_price_point_id): int?`, `ProductPricePointId (product_price_point_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, etc. |
| **Error** | `SdkException<RawError>` (Case B) |
| **Pagination** | manual `page`+`perPage` (defaults: `page`=1, `perPage`=20) |
| **Notes** | SDK does not expose product-family-filtered list directly; implement family filter in code or hard-code product IDs (`eshop-pro`, `basic-plan` handles from user config). Extract `ProductPricePointHandle` or `DefaultProductPricePointId` for each product. |
| **Source** | `map/operations/Products.md`, `map/models/records-3-Of-Su.md` |

**Then, for each product, list its price points:**

| Item | Detail |
|---|---|
| **Controller** | `client.ProductPricePoints` |
| **Method** | `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, CancellationToken ct = default)` |
| **Parameters** | `productId` (required, no default → pass explicitly) — of type `ProductIdModel` (namespace `MaxioAdvancedBilling.Models`). Nullable params: `currencyPrices`, `filterType`, `archived` → pass `null` if not used. Pagination defaults: `page`=1, `perPage`=10. |
| **Request model** | None (query params) |
| **Query params** | `currency_prices` ← `currencyPrices`, `filter[type]` ← `filterType`, `archived` ← `archived`, `page` ← `page`, `per_page` ← `perPage` |
| **Response** | `ListProductPricePointsResponse` (namespace `MaxioAdvancedBilling.Models`) → field `PricePoints (price_points): IReadOnlyList<ProductPricePoint> !req` — unwrap to access array. Fields of `ProductPricePoint`: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents (trial_price_in_cents): long?`, `TrialType (trial_type): TrialType?`, `ExpirationInterval (expiration_interval): int?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `Type (type): PricePointType?` |
| **Error** | `SdkException<RawError>` (Case B) |
| **Pagination** | manual `page`+`perPage` |
| **Notes** | `ProductIdModel` is a simple wrapper or alias; check source. Pass product `Id` (not handle) to this method. Price is in cents; divide by 100 for display. `IntervalUnit` and `TrialType` are enums. |
| **Source** | `map/operations/ProductPricePoints.md`, `map/models/records-1-Ac-Cr.md` |

### Step 4: Create Subscription (with Customer, Product Handle/ID, Price Point)

| Item | Detail |
|---|---|
| **Controller** | `client.Subscriptions` |
| **Method** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` — nullable, no default → **must pass explicitly** |
| **Request model** | `CreateSubscriptionRequest` (namespace `MaxioAdvancedBilling.Models`) — wraps required `Subscription (subscription): CreateSubscription !req` |
| **Request fields** | Inner `CreateSubscription` record (namespace `MaxioAdvancedBilling.Models`) has 40+ optional fields. **Essential ones for hero flow:** `ProductHandle (product_handle): string?` (wire: `product_handle`) OR `ProductId (product_id): int?` — one required; `ProductPricePointHandle (product_price_point_handle): string?` (wire: `product_price_point_handle`) OR `ProductPricePointId (product_price_point_id): int?` — can be omitted (uses product default); `CustomerId (customer_id): int?` OR `CustomerReference (customer_reference): string?` (wire: `customer_reference`) — identify existing customer. **Per the brief, payment method not required.** Omit `PaymentProfileId` and `PaymentProfileAttributes`. Omit `TrialInterval`, setup fees, taxes. |
| **Response** | `SubscriptionResponse` (namespace `MaxioAdvancedBilling.Models`) → field `Subscription (subscription): Subscription !req` — unwrap. Key `Subscription` fields: `Id (id): int?`, `State (state): SubscriptionState?` (enum, wire: `state`), `Customer (customer): Customer?`, `Product (product): Product?`, `ProductHandle (product_handle): string?`, `PricePointHandle (price_point_handle): string?`, `CurrentPeriodStartsAt (current_period_starts_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `TrialStartedAt (trial_started_at): DateTimeOffset?`, `TrialEndsAt (trial_ends_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` |
| **Error** | `SdkException<CreateSubscriptionError>` (Case A) — accessor `TryGetErrorListResponse1(out ErrorListResponse1)` [422] or `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | none |
| **Notes** | Notes state: "Creates a Subscription for a customer and product. Specify the product with `product_id` or `product_handle`. To set a specific product price point, use `product_price_point_handle` or `product_price_point_id`. Identify an existing customer with `customer_id` or `customer_reference`." — use handles for transparency. Response wraps the full `Subscription` object; check `State` and `NextAssessmentAt` to confirm activation. No trial, no setup fee per brief → omit trial fields. **Payment method not required** per brief, so omit payment-related fields. |
| **Source** | `map/operations/Subscriptions.md`, `map/models/records-2-Cr-Ne.md` |

### Step 5: List Subscriptions by Customer (Filter to Active)

| Item | Detail |
|---|---|
| **Controller** | `client.Subscriptions` |
| **Method** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | 14 optional params before pagination; pass `null` to skip. `SubscriptionStateFilter` is an enum; use `SubscriptionStateFilter.Active` (wire: `active`) to filter active subscriptions. No direct "customer ID" query param—filter client-side or use alternative (see Note). Pagination: `page`=1 default, `perPage`=20 default. |
| **Request model** | None (query params) |
| **Query params** | `state` ← `state`, `product` ← `product`, `date_field` ← `dateField`, etc. See operation signature for full mapping. `SubscriptionStateFilter` is enum; wire value `active` for `SubscriptionStateFilter.Active`. |
| **Response** | `IReadOnlyList<SubscriptionResponse>` (namespace `MaxioAdvancedBilling.Models`) — unwrap each response to read `Subscription` record (same fields as Step 4 response). |
| **Error** | `SdkException<RawError>` (Case B) |
| **Pagination** | manual `page`+`perPage` |
| **Notes** | **`ListSubscriptions` does NOT filter by customer ID directly**—it lists all site subscriptions. Either: (A) filter client-side after fetching, or (B) use `ListCustomerSubscriptions(int customerId)` instead (see next operation). For the hero flow, **recommend `ListCustomerSubscriptions`** to avoid fetching all subscriptions. This operation is more suitable for admin dashboards. |
| **Source** | `map/operations/Subscriptions.md`, `map/models/enums.md` |

**Alternative: List Subscriptions by Customer ID**

| Item | Detail |
|---|---|
| **Controller** | `client.Customers` |
| **Method** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Parameters** | `customerId` — customer's Maxio ID (required, no default → pass explicitly) |
| **Request model** | None |
| **Response** | `IReadOnlyList<SubscriptionResponse>` (namespace `MaxioAdvancedBilling.Models`) — same `Subscription` record as above. |
| **Error** | `SdkException<RawError>` (Case B) |
| **Pagination** | none |
| **Notes** | **Preferred method for listing user subscriptions in hero flow.** No pagination, no filters—returns all subscriptions for the customer. Filter client-side by `State == SubscriptionState.Active` if needed. |
| **Source** | `map/operations/Customers.md` |

### Step 6: Read Subscription (Get Detailed State + Next Billing Date)

| Item | Detail |
|---|---|
| **Controller** | `client.Subscriptions` |
| **Method** | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` |
| **Parameters** | `subscriptionId` (required, no default → pass explicitly) — integer Maxio subscription ID. `include` (optional) — can pass `null` or leave unspecified. |
| **Request model** | None |
| **Query params** | `include` ← `include` (enum array; wire values: `coupons`, `self_service_page_token`) |
| **Response** | `SubscriptionResponse` (namespace `MaxioAdvancedBilling.Models`) → field `Subscription (subscription): Subscription !req` — same record as prior steps. Key fields for hero flow: `State (state): SubscriptionState?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CurrentPeriodStartsAt (current_period_starts_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `Product (product): Product?`, `ProductHandle (product_handle): string?` |
| **Error** | `SdkException<RawError>` (Case B) |
| **Pagination** | none |
| **Notes** | Call after creation to confirm state. `NextAssessmentAt` is the next billing date. `State` should be `SubscriptionState.Active` (enum wire: `active`) after successful creation without trial. |
| **Source** | `map/operations/Subscriptions.md` |

---

## Enum Values

### `SubscriptionState` (`MaxioAdvancedBilling.Models.Enums`)
Wire values used in JSON payloads (C# static member names differ):
- `Active` (wire: `active`)
- `Canceled` (wire: `canceled`)
- `Expired` (wire: `expired`)
- `OnHold` (wire: `on_hold`)
- `PastDue` (wire: `past_due`)
- `PendingCancellation` (wire: `pending_cancellation`)
- `Suspended` (wire: `suspended`)
- `TrialEnded` (wire: `trial_ended`)
- `Trialing` (wire: `trialing`)
- `Unpaid` (wire: `unpaid`)

**For hero flow, expect `Active` after creation.**

### `SubscriptionStateFilter` (`MaxioAdvancedBilling.Models.Enums`)
Filter parameter for listing—same values as above, used to filter by current state.

### `SubscriptionListInclude` & `SubscriptionInclude` (`MaxioAdvancedBilling.Models.Enums`)
Optional values for `include` parameter:
- `Coupons` (wire: `coupons`)
- `SelfServicePageToken` (wire: `self_service_page_token`)

### `IntervalUnit` (`MaxioAdvancedBilling.Models.Enums`)
Billing period unit; expected wire values: `month`, `day`, `week`, `year`.

### `PricePointType` (`MaxioAdvancedBilling.Models.Enums`)
Type of price point; wire values: `default`, `custom`.

---

## Client Construction & Auth

### Registration (Dependency Injection)
```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials 
    { 
        Username = configuration["Maxio:ApiKey"],
        Password = "x" 
    };
    o.Environment = ServerEnvironment.Us; // or ServerEnvironment.Eu
});
```

Namespaces required:
- `using MaxioAdvancedBilling;`
- `using MaxioAdvancedBilling.Core.Authentication.Basic;`
- `using MaxioAdvancedBilling.Servers;`
- `using MaxioAdvancedBilling.Api;` (for controller properties on client)
- `using MaxioAdvancedBilling.Models;` (for request/response records)
- `using MaxioAdvancedBilling.Models.Enums;` (for enums like `SubscriptionState`)
- `using MaxioAdvancedBilling.Errors;` (for typed error classes)

### Configuration Binding Keys (from env → user-secrets)
- `Maxio:ApiKey` ← `MAXIO_API_KEY` (the Maxio API key; becomes BasicAuth `Username`)
- `Maxio:Subdomain` ← `MAXIO_SITE_SUBDOMAIN` (used to construct base URL; set via `options.Server.Production.Us.Site`)
- `Maxio:ProductFamilyHandle` ← `MAXIO_DEFAULT_PRODUCT_FAMILY` (e.g., `eshop-subscribe`; used to identify products)
- `Maxio:BaseUrl` (optional) — if set, override `options.Server.Production.Us.BaseUrl` with this literal value

### Auth & Server Wiring
- **Auth scheme:** HTTP Basic — `Username` = API key (from `Maxio:ApiKey`), `Password` = literal string `"x"`
- **Server:** Production (default). If using `Maxio:Subdomain`, set `options.Server.Production.Us.Site = configuration["Maxio:Subdomain"]` to build `https://{site}.chargify.com`. If `Maxio:BaseUrl` is set and non-empty, override with `options.Server.Production.Us.BaseUrl = configuration["Maxio:BaseUrl"]`.

---

## Assumptions & Blockers

### Assumptions
1. **User identity** — eShopOnWeb app has logged-in user with a stable, unique identifier (user ID or reference); this is used as the Maxio customer `reference` field for idempotent lookup.
2. **Product & plan configuration** — Maxio sandbox has the following stable entities (hard-coded handles in the integration, or fetched once and cached):
   - Product Family `eshop-subscribe`
   - Products `eshop-pro` (handle) and `basic-plan` (handle) with default price points at $299/mo and $29/mo respectively
   - Optional metered component `api-call` ($0.01/unit) on both plans (scope: not in hero flow, defer to later)
3. **Payment methods not required** — Per brief, subscriptions are created without payment profiles. This simplifies the flow but requires Maxio configuration to allow non-payment-profile subscriptions.
4. **Environment & credentials** — Maxio credentials (API key, subdomain) are provided at app startup via configuration (env vars → user-secrets or appsettings). No hardcoding of secrets in repo.
5. **Single environment** — Hero flow targets sandbox initially; production URL swap is a configuration change (Maxio:Subdomain or Maxio:BaseUrl).

### Blockers
1. **Product family listing** — SDK `ListProducts` does not filter by product family handle directly. Implementation must either:
   - Hard-code the product IDs/handles (`eshop-pro`, `basic-plan`) in code, or
   - Fetch all products and filter client-side by product-family ID, or
   - Call `ReadProductByHandle` for each known handle.
   
   **No blocker; workaround is in-code filtering or hard-coded list.**

2. **Customer lookup by reference** — `ReadCustomerByReference` throws `SdkException<RawError>` with 404 if not found. Implementation must catch, distinguish 404 (create customer) from other errors (fail). **No blocker; expected behavior.**

3. **Payment method not required** — Maxio behavior when subscription is created without a payment profile is unverified by the SDK map. **Assumption:** Maxio sandbox config allows this. **If Maxio rejects the create call with 422 citing missing payment profile, the integration will fail at subscription creation.** Test early. **Label: UNVERIFIED.**

4. **Metered component usage tracking** — Brief mentions optional `api-call` component ($0.01/unit) but does not scope usage ingestion. Scope unclear. **Defer to later phase; hero flow omits this.**

---

## REQUIRED READING

The companion skills below must be loaded **before implementation starts**. This contract sheet deliberately does not carry their contents; each skill provides patterns, defaults, config keys, and worked examples that are not inferable from the map alone.

| Skill | Governs | Notes |
|---|---|---|
| `dotnet-client-initialization` | Step 1 (client registration & DI) | HttpClient factory reuse, transient vs. singleton, options construction |
| `dotnet-authentication` | Step 1 (auth setup) | Basic auth credentials, loading from config, rotation (not in scope but read for context) |
| `dotnet-calling-endpoints` | Steps 2–6 (calling operations) | Named vs. positional parameters, null handling for optional params, async/await, cancellation |
| `dotnet-models` | Steps 2–4 (request/response models) | Record immutability, `init`-only setters, union factories (`TryGet…`), enum construction (`StringEnum<T>.FromValue(…)` or static members) |
| `dotnet-error-handling` | Steps 2, 4 (error boundaries) | Case A typed errors (`TryGet…` accessors) vs. Case B raw errors, distinguishing 404 from 422, catch ordering; **read before writing the error boundary for customer creation** |
| `dotnet-configuration-resilience` | Step 1 (retry/timeout tuning) | `RetryOptions`, `Timeout` semantics (per-attempt, not total), `HttpMethodsToRetry` + POST idempotency, max-retries floor |
| `dotnet-testing` | All steps (unit/integration test seams) | `HttpClient` test mocking, `SdkException` assertion helpers, worked examples |

**Essential caveat (see `dotnet-error-handling`):**
- A drifted or malformed **2xx body** (missing `required` member) surfaces as `JsonException` from deserialization, **not** an `SdkException` — so an SDK-exception-only catch ladder lets it escape unhandled.
- A **non-2xx body** that does not match the operation's generated error shape throws `JsonException` **while the error object is being constructed**, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed — map every `JsonException` to a 5xx and report as an outage; a caller that retries 5xx retries something that can never succeed.

**Load `dotnet-error-handling` before writing the integration error boundary.**

