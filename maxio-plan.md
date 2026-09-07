# Maxio Subscription Billing Integration — eShopOnWeb

## Scope & Sequence

1. **Endpoint: GET /api/subscription-plans** — list available subscription plans from Maxio
   - Call: `Products.ListProducts()`
   - Return: `IReadOnlyList<ProductResponse>`

2. **Endpoint: POST /api/subscriptions** — enroll logged-in user in a subscription
   - Sequence:
     a. Look up or create Maxio customer (idempotent by reference)
     b. Create subscription (idempotent by reference key)
     c. Return subscription plan/price/state/next-billing-date to user
   - Calls:
     - `Customers.ReadCustomerByReference(reference)` (read if exists)
     - `Customers.CreateCustomer(body)` (create if not found)
     - `Subscriptions.CreateSubscription(body)` (create subscription)
     - `Subscriptions.ReadSubscription(subscriptionId)` (confirm state)

3. **Endpoint: GET /api/my-subscriptions** — retrieve user's active subscriptions
   - Call: `Subscriptions.ListSubscriptions()` with customer/reference filter
   - Or: `Customers.ListCustomerSubscriptions(customerId)` if customer ID is known

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

| Operation | Signature | Request Model + Fields | Response Envelope + Inner Fields | Error Type & Accessors | Pagination | Source |
|---|---|---|---|---|---|---|
| **List Products** (step 1) | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 8 params nullable, pass `null` to skip; defaults `page=1`, `perPage=20` | None (query-only); optional: `filter` with `Ids` (filter by product IDs to match product family) | `IReadOnlyList<ProductResponse>` · `Product (product)`: `Id`, `Name` (wire: `name`), `Handle` (wire: `handle`), `PriceInCents` (wire: `price_in_cents`), `Interval`, `IntervalUnit` | `SdkException<RawError>` (Case B) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | Manual `page`+`perPage` | `operations/Products.md` |
| **Read Customer by Reference** (step 2a) | `ReadCustomerByReference(string reference, CancellationToken ct = default)` — `reference` must pass explicitly | Query param: `reference` (wire: `reference`) | `CustomerResponse` · `Customer (customer)`: `Id`, `FirstName`, `LastName`, `Email`, `Reference` | `SdkException<RawError>` (Case B) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | None | `operations/Customers.md` |
| **Create Customer** (step 2a) | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — `body` must pass explicitly | `CreateCustomerRequest` · inner: `Customer` (wire: `customer`) !req with fields: `FirstName` (wire: `first_name`) !req, `LastName` (wire: `last_name`) !req, `Email` (wire: `email`) !req, `Reference` (wire: `reference`) optional (app's unique customer ID), `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone` | `CustomerResponse` · `Customer (customer)`: `Id`, `Email`, `Reference`, `FirstName`, `LastName` | `SdkException<CreateCustomerError>` (Case A) · `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | None | `operations/Customers.md` |
| **Create Subscription** (step 2b) | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — `body` must pass explicitly | `CreateSubscriptionRequest` · inner: `Subscription` (wire: `subscription`) !req with fields: `CustomerReference` (wire: `customer_reference`) optional (tie to customer by app reference), `CustomerId` (wire: `customer_id`) optional (Maxio customer ID if known), `ProductId` (wire: `product_id`) optional, `ProductHandle` (wire: `product_handle`) optional (e.g., `"eshop-pro"`), `ProductPricePointId` (wire: `product_price_point_id`) optional, `PaymentCollectionMethod` (wire: `payment_collection_method`) optional (enum), `Reference` (wire: `reference`) optional (idempotent key to prevent double-click) | `SubscriptionResponse` · `Subscription (subscription)`: `Id`, `State` (wire: `state`, enum SubscriptionState), `ProductPriceInCents` (wire: `product_price_in_cents`), `NextAssessmentAt` (wire: `next_assessment_at`, DateTimeOffset — next billing date), `CurrentPeriodEndsAt` (wire: `current_period_ends_at`), `ActivatedAt`, `Product` (nested), `Customer` (nested) | `SdkException<CreateSubscriptionError>` (Case A) · `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | None | `operations/Subscriptions.md` |
| **Read Subscription** (step 2c — confirm state) | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` — `subscriptionId` positional, `include` nullable query param | Query param: `include` (optional, enum list for related data) | `SubscriptionResponse` · `Subscription (subscription)`: `Id`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, `Product.Name`, `Product.PriceInCents` | `SdkException<RawError>` (Case B) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | None | `operations/Subscriptions.md` |
| **List Subscriptions** (step 3 — user's subscriptions) | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 params nullable, pass `null` to skip; defaults `page=1`, `perPage=20` | Query params: `product` (filter by product ID), others optional (no direct customer filter; use customer reference in metadata or by separate read) | `IReadOnlyList<SubscriptionResponse>` · `Subscription (subscription)`: `Id`, `State`, `ProductPriceInCents`, `NextAssessmentAt`, `Reference`, `CustomerId`, `Customer.Reference` | `SdkException<RawError>` (Case B) · `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | Manual `page`+`perPage` | `operations/Subscriptions.md` |

### Enums

| Enum | Namespace | Values (C# member names and wire values) | Source |
|---|---|---|---|
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Trialing`, `Assessing`, `Active`, `SoftFailed`, `PastDue`, `Suspended`, `Canceled`, `Expired` | `map/models/enums.md` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic`, `Invoice`, … | `map/models/enums.md` |
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Day`, `Month`, `Year` | `map/models/enums.md` |
| `BasicDateField` | `MaxioAdvancedBilling.Models.Enums` | `CreatedAt`, `UpdatedAt` | `map/models/enums.md` |
| `SubscriptionDateField` | `MaxioAdvancedBilling.Models.Enums` | `CreatedAt`, `UpdatedAt`, `ActivatedAt`, `CanceledAt`, `CurrentPeriodStartsAt`, `CurrentPeriodEndsAt`, `NextAssessmentAt` | `map/models/enums.md` |

### Request/Response Model Namespaces

All models live in `MaxioAdvancedBilling.Models`.

| Model | Wire Name | Namespace | Source |
|---|---|---|---|
| `CreateCustomerRequest` | `customer` | `MaxioAdvancedBilling.Models` | `records-1-Ac-Cr.md` |
| `CreateCustomer` | (nested in request) | `MaxioAdvancedBilling.Models` | `records-1-Ac-Cr.md` |
| `CustomerResponse` | `customer` | `MaxioAdvancedBilling.Models` | `records-2-Cr-Ne.md` |
| `Customer` | (nested in response) | `MaxioAdvancedBilling.Models` | `records-2-Cr-Ne.md` |
| `CreateSubscriptionRequest` | `subscription` | `MaxioAdvancedBilling.Models` | `records-2-Cr-Ne.md` |
| `CreateSubscription` | (nested in request) | `MaxioAdvancedBilling.Models` | `records-2-Cr-Ne.md` |
| `SubscriptionResponse` | `subscription` | `MaxioAdvancedBilling.Models` | `records-4-Su-We.md` |
| `Subscription` | (nested in response) | `MaxioAdvancedBilling.Models` | `records-3-Of-Su.md` |
| `ProductResponse` | `product` | `MaxioAdvancedBilling.Models` | `records-3-Of-Su.md` |
| `Product` | (nested in response) | `MaxioAdvancedBilling.Models` | `records-3-Of-Su.md` |

### Client Construction & Auth

- **Root namespace**: `MaxioAdvancedBilling`
- **Client class**: `MaxioAdvancedBillingClient`
- **Options class**: `MaxioAdvancedBillingClientOptions`
- **Auth**: HTTP Basic — `Username` = API key (from `MAXIO_API_KEY`), `Password` = literal `"x"`
- **Environment**: `ServerEnvironment.Us` (default, matches `https://{site}.chargify.com`)
- **Server override**: `options.Server.Production.Us.Site = "{subdomain}"` or `options.Server.Production.Us.BaseUrl = "http://localhost:…"`
- **DI registration**: `services.AddMaxioAdvancedBillingClient(o => { o.BasicAuth = ...; o.Environment = ...; })`

Configuration binding (from app config → SDK options):
- `Maxio:ApiKey` → `BasicAuth.Username`
- `Maxio:Subdomain` → `options.Server.Production.Us.Site`
- `Maxio:ProductFamilyHandle` → passed to queries (e.g., filter products)
- `Maxio:BaseUrl` (optional override) → `options.Server.Production.Us.BaseUrl`
- `Maxio:Environment` (US/EU) → `ServerEnvironment` enum

### Idempotency & Duplicate Prevention

**Customer creation is idempotent by reference:**
- Always call `ReadCustomerByReference(reference)` first (using app's user ID or email hash as reference)
- If 404 or not found, call `CreateCustomer(…, Reference = reference, …)`
- Re-attempting create with same reference will result in 422 error → app must handle gracefully or check first

**Subscription creation is idempotent by reference:**
- Pass `Reference` field in `CreateSubscription` request (use a deterministic key, e.g., `{userId}-{productHandle}`)
- On double-click, second attempt will fail with 422 if same reference exists
- App must either check first or handle 422 error and re-read subscription to get state

**Next-billing-date extraction:**
- Read from `Subscription.NextAssessmentAt` (DateTimeOffset, wire: `next_assessment_at`)
- Null if subscription not yet active or cancelled

---

## Trap Notes

⚠ **Step 1 (list plans)** — product listing can return a large set; paginate via `page` and `perPage` params (defaults 1, 20). No direct product-family filter in `ListProducts`; use `ListProductsFilter.Ids` or `filter.prepaid_product_price_point` if needed. **MUST load `dotnet-calling-endpoints`** to confirm pagination and optional param handling.

⚠ **Step 2a (customer lookup/create)** — `ReadCustomerByReference` returns 404 as HTTP status via `SdkException<RawError>`, not a typed accessor. App must catch `RawError.StatusCode == HttpStatusCode.NotFound` to distinguish missing customer from other errors. Creating with a duplicate `reference` results in 422 `CreateCustomerError.TryGetCustomerErrorResponse1` with validation errors (not a typed accessor for 404). **MUST load `dotnet-error-handling`** for Case A vs Case B mechanics and `TryGet…` usage.

⚠ **Step 2b (subscription creation)** — subscription requires either `CustomerId`, `CustomerReference`, or `CustomerAttributes` (nested customer create). Recommend using `CustomerReference` tied to app's user ID for consistency with customer lookup. `Reference` field on subscription (not customer) is the idempotent deduplication key; use a deterministic app-level value like `{userId}-{timestamp}` or `{userId}-{productHandle}`. A second create with the same `reference` will return 422, not a duplicate; app must handle or read-first. **MUST load `dotnet-error-handling`** before writing subscription error boundary.

⚠ **Step 2c (confirm subscription state)** — `NextAssessmentAt` is the next billing date; can be null if subscription is inactive. `State` is an enum (`SubscriptionState`); check for `Active`, `Trialing`, or other states to determine if subscription was successfully enrolled. **MUST load `dotnet-models`** to understand enums (they are `StringEnum<T>`, not C# enums) and union field reads.

⚠ **Step 3 (list user subscriptions)** — `ListSubscriptions` has no direct `customer_id` or `customer_reference` query param. To filter by user: either (a) read customer first via reference, store customer ID, and iterate `IReadOnlyList<SubscriptionResponse>` offline to find matching `subscription.CustomerId`, or (b) use `metadata` dictionary filter if app stored user context as subscription metadata. **MUST load `dotnet-calling-endpoints`** to confirm optional param semantics and how to pass `null` for skipped params in named-argument calls.

⚠ **Configuration & environment** — `ServerEnvironment.Us` defaults to `https://{site}.chargify.com`; override `Site` or `BaseUrl` in options before client construction. `DOTNET_ROLL_FORWARD=Major` is needed in launch settings or build to work around SDK/runtime mismatch (global.json pins 8.0.x, but .NET 10 SDK is installed). **MUST load `dotnet-configuration-resilience`** before tuning timeout, retry, or base-URL settings.

⚠ **Deserialization errors — two directions of `System.Text.Json.JsonException`:**
- **Drifted or malformed 2xx body** (missing `required` member, truncated JSON): surfaces as `JsonException` from deserialization, **NOT** as `SdkException` — a catch ladder that only catches `SdkException<…>` lets it escape.
- **Non-2xx body that does not match its operation's generated `{Operation}Error` shape**: throws `JsonException` **while the error object is being constructed**, so `JsonException` **replaces** the `SdkException` and HTTP status is lost — a boundary that maps every `JsonException` to a 5xx error then tells the caller "retry" will cause infinite loop because the malformed error can never succeed.
  **MUST load `dotnet-error-handling`** and map both `JsonException` sources *before* writing the error boundary; the integration boundary is written early, and a post-hoc caveat is too late.

---

## REQUIRED READING

The following companion skills must be loaded **before implementation starts**. This sheet deliberately does not carry their contents; each skill governs the step it names and carries patterns, defaults, and gotchas a contract sheet cannot.

| Skill | Governs Step | Notes |
|---|---|---|
| `dotnet-client-initialization` | Step 0 (client & DI setup) | HttpClient reuse, transient vs long-lived wrappers, DI registration via `AddMaxioAdvancedBillingClient` |
| `dotnet-authentication` | Step 0 (auth setup) | Basic auth credential binding, whether to set before or after client construction, config vs hardcoding |
| `dotnet-calling-endpoints` | Steps 1–3 (all operations) | Operation signatures, named-arg vs positional, nullable param binding, when to pass `null`, pagination semantics |
| `dotnet-models` | Steps 2–3 (request/response bodies) | Immutable record construction, `required` vs optional fields, enums (`StringEnum<T>`, not C# enums), union read via `TryGet…` |
| `dotnet-error-handling` | Steps 2–3 (error boundary) | Case A (typed `{Operation}Error`) vs Case B (`RawError`), `TryGet…` accessors, when 404/422 surface as what type, how `JsonException` escapes or replaces the exception, pagination/retry implications, the two `JsonException` cases |
| `dotnet-configuration-resilience` | Step 0 & throughout (timeouts, retries, base-URL) | Retry options (`HttpMethodsToRetry` gates status not transport; POST is retried on transport failure), timeout is per-attempt not total, no built-in logging, override base URL or site subdomain |
| `dotnet-testing` | Step 4 (unit tests) | Mock via `HttpClient` test seam, match project's existing test framework |

---

## Assumptions & Blockers

**Assumptions:**
- App's user identity system can supply a deterministic reference (e.g., `userId`, or hash of email) to tie Maxio customer to app user; if multi-tenant, tenant/org scope is handled outside the SDK integration.
- Products in the Maxio Product Family `eshop-subscribe` are pre-configured in sandbox (Pro: ID 7126957, Basic: ID 7126958) and match expected pricing ($299/mo, $29/mo). Integration will call `ListProducts()` to enumerate; no pre-hardcoding of IDs.
- No payment method is required to create subscription (per scope: "no payment method required"). If live payment is needed later, integration must be extended to accept payment profiles.
- Idempotent subscription creation via `Reference` field is acceptable; app supplies reference and handles 422 on re-send (either read-first pattern or graceful error).

**Blockers:**
- None identified. Plan is grounded in available operations and can proceed to implementation.

---

## Implementation Notes

- All enums (`SubscriptionState`, `CollectionMethod`, etc.) are `StringEnum<T>`, not C# enums; construct via static member (e.g., `SubscriptionState.Active`) or factory (e.g., `SubscriptionState.FromValue("active")`).
- Response envelopes wrap payloads: `ProductResponse.Product`, `CustomerResponse.Customer`, `SubscriptionResponse.Subscription`. Always extract inner field.
- `NextAssessmentAt` is nullable; null means subscription is inactive or does not renew (check `State` enum).
- Subscription `Reference` is the idempotent key; use a deterministic app-level value like `{userId}-{productHandle}-{enrollmentTime}` or simpler if re-create is rare.
