# Maxio Advanced Billing Integration Plan — eShopOnWeb

## Scope & Sequence

1. **List subscription plans** — GET /api/subscription-plans
   - Operation: `Products.ListProducts` — fetch plans from eshop-subscribe product family
   - Filter by product family handle or list all products

2. **Create or retrieve customer (idempotent)** — internal operation before subscription creation
   - Operation: `Customers.ReadCustomerByReference` — lookup by logged-in user's unique reference (e.g., user ID or email)
   - Operation: `Customers.CreateCustomer` — if not found, create new customer with user details
   - Ensures single customer per user; prevents duplicate customer records

3. **Create subscription for user** — POST /api/subscriptions
   - Operation: `Subscriptions.CreateSubscription` — create subscription binding customer to a plan
   - Pass existing customer ID, product handle/ID, and optional components (metered component for api-call tracking)

4. **List user's subscriptions** — GET /api/my-subscriptions
   - Operation: `Customers.ListCustomerSubscriptions` — fetch subscriptions for the logged-in customer
   - Filters subscriptions to the authenticated user only

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operation: ListProducts

| Aspect | Value |
|---|---|
| **Controller & method** | `client.Products.ListProducts(…)` |
| **Signature** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters (in order)** | All 8 optional params before pagination: `dateField`, `filter`, `endDate`, `endDatetime`, `startDate`, `startDatetime`, `includeArchived`, `include` (all nullable, pass `null` to skip). Pagination: `page` (default 1), `perPage` (default 20). |
| **Request body** | None |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` — list of product envelopes; unwrap each with `.Product` to access the `Product` record |
| **Response envelope & fields** | Each element is `ProductResponse` with one field: `Product (product): Product !req`. Unwrap via `respItem.Product` to access: `Id`, `Name`, `Handle`, `Description`, `PriceInCents`, `Interval`, `IntervalUnit`, `RequireCreditCard`, `CreatedAt`, `UpdatedAt`, etc. |
| **Error — Case B** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — no typed accessors; use `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()` |
| **Pagination** | Manual: pass `page` and `perPage`. Default perPage is 20; request all with a loop incrementing `page`. |
| **Source** | `map/operations/Products.md` |

### Operation: ReadCustomerByReference

| Aspect | Value |
|---|---|
| **Controller & method** | `client.Customers.ReadCustomerByReference(…)` |
| **Signature** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Parameters** | `reference` (required, non-nullable string) — the app's own unique identifier for the user (e.g., user ID or email) |
| **Request body** | None |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` — envelope wrapping a single customer |
| **Response envelope & fields** | `CustomerResponse` has one field: `Customer (customer): Customer !req`. Unwrap via `.Customer` to access: `Id`, `FirstName`, `LastName`, `Email`, `Reference`, `CreatedAt`, `UpdatedAt`, etc. |
| **Error — Case B** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — 404 if reference not found; check `ex.Error.StatusCode` == `HttpStatusCode.NotFound` to detect "customer not found" and proceed with creation |
| **Pagination** | None |
| **Source** | `map/operations/Customers.md` |

### Operation: CreateCustomer

| Aspect | Value |
|---|---|
| **Controller & method** | `client.Customers.CreateCustomer(…)` |
| **Signature** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` (required, non-nullable) — pass explicitly |
| **Request body** | `CreateCustomerRequest` wraps `CreateCustomer` record |
| **Request model fields** | Inside `CreateCustomerRequest.Customer` (`CreateCustomer` record): **required:** `FirstName` (wire: first_name), `LastName` (wire: last_name), `Email` (wire: email). **Optional (idempotency):** `Reference` (wire: reference) — set to logged-in user's unique ID (e.g., user ID string); ensures only one customer per user. Other optional fields: `Organization`, `Address`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId`, `CcEmails`, `Address2`. |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` — envelope with `Customer` field |
| **Response envelope & fields** | Same as ReadCustomerByReference: `CustomerResponse.Customer` (`MaxioAdvancedBilling.Models.Customer`) provides `Id` (Maxio-assigned), `Reference`, `Email`, etc. |
| **Error — Case A** | `SdkException<MaxioAdvancedBilling.Errors.CreateCustomerError>` — typed error with: `TryGetCustomerErrorResponse1(out MaxioAdvancedBilling.Models.CustomerErrorResponse1)` [422], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback]. Typical 422: validation failures (missing email, invalid country code, duplicate reference). |
| **Pagination** | None |
| **Source** | `map/operations/Customers.md` |

### Operation: CreateSubscription

| Aspect | Value |
|---|---|
| **Controller & method** | `client.Subscriptions.CreateSubscription(…)` |
| **Signature** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **Parameters** | `body` (required, non-nullable) — pass explicitly |
| **Request body** | `CreateSubscriptionRequest` wraps `CreateSubscription` record |
| **Request model fields** | Inside `CreateSubscriptionRequest.Subscription` (`CreateSubscription` record): **At least one product specifier required:** `ProductHandle` (wire: product_handle) or `ProductId` (wire: product_id) — use `ProductHandle` (e.g., "eshop-pro", "basic-plan"). **At least one customer specifier required:** `CustomerId` (wire: customer_id, int) or `CustomerReference` (wire: customer_reference, string). **Optional:** `ProductPricePointHandle` (wire: product_price_point_handle), `ProductPricePointId` (wire: product_price_point_id) — if omitted, default price point is used. `PaymentCollectionMethod` (wire: payment_collection_method, `CollectionMethod` enum: "automatic", "remittance", "prepaid", "invoice"). `Reference` (wire: reference) — subscription's own unique reference for idempotent retries. `Components` (wire: components, `IReadOnlyList<CreateSubscriptionComponent>`) — for metered component (api-call): pass component ID/handle and initial allocation. `CustomerAttributes` (wire: customer_attributes, `CustomerAttributes` record) — alternative to `CustomerId`, for inline customer creation (but we use separate create flow for idempotency). `PaymentProfileId` (wire: payment_profile_id) — not required (noted as "payment method not required" in brief). Other optional fields: `CouponCode`, `CouponCodes`, `DeferSignup`, `NextBillingAt`, `InitialBillingAt`, `NetTerms`, `Currency`, etc. |
| **Returns** | `MaxioAdvancedBilling.Models.SubscriptionResponse` — envelope wrapping subscription state |
| **Response envelope & fields** | `SubscriptionResponse` has one field: `Subscription (subscription): Subscription`. Unwrap via `.Subscription` to access: `Id`, `State` (`MaxioAdvancedBilling.Models.Enums.SubscriptionState` enum: "active", "canceled", "pending", "trialing", etc.), `ProductId`, `ProductHandle`, `CustomerId`, `BalanceInCents`, `CurrentPeriodStartsAt`, `CurrentPeriodEndsAt`, `NextAssessmentAt`, `ActivatedAt`, `CreatedAt`, `UpdatedAt`, `CouponCode`, `Reference`, etc. |
| **Error — Case A** | `SdkException<MaxioAdvancedBilling.Errors.CreateSubscriptionError>` — typed error with: `TryGetErrorListResponse1(out MaxioAdvancedBilling.Models.ErrorListResponse1)` [422], `TryGetRawError(out MaxioAdvancedBilling.Core.ErrorResponse.RawError)` [fallback]. Typical 422: invalid product, customer not found, payment method required (if gateway config demands it), invalid coupon, invalid component allocation. |
| **Pagination** | None |
| **Source** | `map/operations/Subscriptions.md` |

### Operation: ListSubscriptions (filtered by customer)

| Aspect | Value |
|---|---|
| **Controller & method** | `client.Subscriptions.ListSubscriptions(…)` |
| **Signature** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Parameters** | 14 optional filters (all nullable, pass `null` to skip): `state`, `product` (product ID), `productPricePointId`, `coupon` (coupon ID), `couponCode`, `dateField`, `startDate`, `endDate`, `startDatetime`, `endDatetime`, `metadata`, `direction`, `sort`, `include`. Pagination: `page` (default 1), `perPage` (default 20). **Note:** No built-in "customer filter" parameter. To list subscriptions for a specific customer, use `Customers.ListCustomerSubscriptions(customerId)` instead (see next operation). |
| **Request body** | None |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` — list of subscription envelopes |
| **Response envelope & fields** | Each element is `SubscriptionResponse` with field `Subscription (subscription): Subscription`. Unwrap via `.Subscription` to access subscription details (see CreateSubscription response fields). |
| **Error — Case B** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — no typed accessors. Use for site-wide subscription lists or filtered lists; for user's own subscriptions, prefer `ListCustomerSubscriptions(customerId)`. |
| **Pagination** | Manual: pass `page` and `perPage`. Default perPage is 20. |
| **Source** | `map/operations/Subscriptions.md` |

### Operation: ListCustomerSubscriptions

| Aspect | Value |
|---|---|
| **Controller & method** | `client.Customers.ListCustomerSubscriptions(…)` |
| **Signature** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Parameters** | `customerId` (required, int) — Maxio-assigned customer ID (from CreateCustomer or ReadCustomerByReference response) |
| **Request body** | None |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` — list of subscriptions for that customer only |
| **Response envelope & fields** | Each element is `SubscriptionResponse` (see CreateSubscription response fields above). |
| **Error — Case B** | `SdkException<MaxioAdvancedBilling.Core.ErrorResponse.RawError>` — 404 if customer not found; check `ex.Error.StatusCode` == `HttpStatusCode.NotFound`. |
| **Pagination** | None (returns all subscriptions for customer) |
| **Source** | `map/operations/Customers.md` |

---

## Enum Values (from SDK map)

### SubscriptionState
Wire values: `pending`, `failed_to_create`, `trialing`, `assessing`, `active`, `soft_failure`, `past_due`, `suspended`, `canceled`, `expired`, `paused`, `unpaid`, `trial_ended`, `on_hold`, `awaiting_signup`.

**C# member names** (use these in code):
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.Active`
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.Canceled`
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.Pending`
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.Trialing`
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.PastDue`
- `MaxioAdvancedBilling.Models.Enums.SubscriptionState.Suspended`
- (and others from wire list above)

### IntervalUnit
**Wire values:** `day`, `month`.

**C# member names:**
- `MaxioAdvancedBilling.Models.Enums.IntervalUnit.Day`
- `MaxioAdvancedBilling.Models.Enums.IntervalUnit.Month`

### CollectionMethod
**Wire values:** `automatic`, `remittance`, `prepaid`, `invoice`.

**C# member names:**
- `MaxioAdvancedBilling.Models.Enums.CollectionMethod.Automatic`
- `MaxioAdvancedBilling.Models.Enums.CollectionMethod.Remittance`
- `MaxioAdvancedBilling.Models.Enums.CollectionMethod.Prepaid`
- `MaxioAdvancedBilling.Models.Enums.CollectionMethod.Invoice`

---

## Idempotency Strategy

### Customer Creation (Idempotent)
1. Before `CreateCustomer`, call `ReadCustomerByReference(userReference)` (e.g., user ID or email as string).
2. If 404 (customer not found), proceed with `CreateCustomer`.
3. Always pass `Reference` in CreateCustomer body, set to the logged-in user's unique identifier.
4. **Consequence:** Each user has exactly one Maxio customer record, keyed by reference. Retrying subscription creation uses the same customer (found by reference) rather than creating duplicates.

### Subscription Creation (Idempotent)
1. Before `CreateSubscription`, check existing subscriptions via `ListCustomerSubscriptions(customerId)`.
2. Filter the returned list by `ProductHandle` and `State` to detect active/pending subscription for the same plan.
3. If found, return the existing subscription (avoid duplicate creation).
4. Otherwise, proceed with `CreateSubscription`.
5. **Optional:** Pass `Reference` in CreateSubscription body (subscription's own unique reference) for further idempotency; Maxio allows reference-based lookup (see `FindSubscription` operation).

---

## Client Setup & Configuration

### DI Registration
```csharp
// In Startup or Program.cs:
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

services.AddMaxioAdvancedBillingClient(options =>
{
    var apiKey = configuration["Maxio:ApiKey"];     // from config/env
    var subdomain = configuration["Maxio:Subdomain"];
    var environment = configuration["Maxio:Environment"] == "eu" 
        ? ServerEnvironment.Eu 
        : ServerEnvironment.Us;

    options.BasicAuth = new BasicAuthCredentials 
    { 
        Username = apiKey, 
        Password = "x" 
    };
    options.Environment = environment;
    options.Server.Production.Us.Site = subdomain;  // or .Eu if EU
});
```

### Configuration Binding
Expected configuration keys (from brief):
- `Maxio:ApiKey` → `BasicAuthCredentials.Username`
- `Maxio:Subdomain` → `Server.Production.Us.Site` (or `.Eu.Site`)
- `Maxio:Environment` → `ServerEnvironment` (map "eu" → `Eu`, default → `Us`)
- `Maxio:DefaultProductFamilyHandle` → `"eshop-subscribe"` (used in plan listing to filter by family)

---

## Wire Names & C# Property Names

| Use case | C# property | Wire name (JSON) |
|---|---|---|
| Customer first name | `FirstName` | `first_name` |
| Customer last name | `LastName` | `last_name` |
| Customer email | `Email` | `email` |
| Customer reference (your ID) | `Reference` | `reference` |
| Product handle | `ProductHandle` | `product_handle` |
| Product ID | `ProductId` | `product_id` |
| Subscription state | `State` | `state` |
| Subscription customer ID | `CustomerId` | `customer_id` |
| Subscription customer reference | `CustomerReference` | `customer_reference` |
| Subscription reference (your ID) | `Reference` | `reference` |
| Payment collection method | `PaymentCollectionMethod` | `payment_collection_method` |
| Interval | `Interval` | `interval` |
| Interval unit | `IntervalUnit` | `interval_unit` |

**Wire names are JSON keys on the wire; C# property names are what you write in code.** Always use C# names when building request objects.

---

## Error Handling Summary

| Operation | Error type | How to read |
|---|---|---|
| ListProducts | `SdkException<RawError>` (Case B) | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |
| ReadCustomerByReference | `SdkException<RawError>` (Case B) | 404 means customer not found; check `StatusCode == HttpStatusCode.NotFound` |
| CreateCustomer | `SdkException<CreateCustomerError>` (Case A) | `ex.Error.TryGetCustomerErrorResponse1(out var err422)` [422], `ex.Error.TryGetRawError(out var raw)` [fallback] |
| CreateSubscription | `SdkException<CreateSubscriptionError>` (Case A) | `ex.Error.TryGetErrorListResponse1(out var err422)` [422], `ex.Error.TryGetRawError(out var raw)` [fallback] |
| ListSubscriptions | `SdkException<RawError>` (Case B) | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |
| ListCustomerSubscriptions | `SdkException<RawError>` (Case B) | `ex.Error.StatusCode` |

---

## REQUIRED READING

Before implementation starts, load these companion skills (in order):

| Skill | Step it governs |
|---|---|
| `dotnet-client-initialization` | Client & DI setup (step 1: create `MaxioAdvancedBillingClient` and register via DI) |
| `dotnet-authentication` | Authentication (set `BasicAuthCredentials` before client construction) |
| `dotnet-calling-endpoints` | Calling operations (make requests via `client.{Controller}.{Operation}`) |
| `dotnet-models` | Request/response models (build `CreateCustomerRequest`, `CreateSubscriptionRequest` records; read `SubscriptionResponse`, `ProductResponse`) |
| `dotnet-error-handling` | **Error handling & exception boundaries** — CRITICAL: Read before writing any `try/catch`. This SDK is **throw-only** (no Result variants). Typed errors (Case A) have `TryGet…` accessors; raw errors (Case B) use `StatusCode`/`ReadAsString()`. **TWO UNVERIFIED HAZARDS** to handle: (1) **Drifted/malformed 2xx body** (missing `required` field on deserialized model) surfaces as `JsonException` from deserialization, NOT `SdkException` — catch ladder for SDK exceptions only lets it escape, breaking the boundary; (2) **Non-2xx body that doesn't match the operation's generated `{Operation}Error` shape** throws `JsonException` *while constructing the error object*, replacing the `SdkException` and destroying the HTTP status — a boundary mapping all `JsonException` to 5xx misreports deterministic rejections as outages, and callers retrying 5xx retry something that can never succeed. **MUST load `dotnet-error-handling`** before wiring your boundary. |
| `dotnet-configuration-resilience` | Configuration & resilience (retry/timeout settings, base URL override, pagination loops) |
| `dotnet-testing` | Testing (stub the `HttpClient` constructor; use project's existing framework) |

These skills contain the "how" for each step; this sheet contains the "what" (signatures, models, enums). Load each skill before its step; the sheet is your reference during implementation.

---

## Assumptions & Blockers

- **Assumption:** Logged-in user (JWT auth) provides a stable, unique identifier (user ID or email) for `Reference` field — avoids customer duplication.
- **Assumption:** Maxio sandbox catalog contains products with handles `eshop-pro` and `basic-plan` in product family `eshop-subscribe` (per brief).
- **Blocker:** None identified — all required operations are available in the SDK map.

