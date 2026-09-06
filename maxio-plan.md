# Maxio Subscription Billing Integration Plan — eShopOnWeb

## Scope & Sequence

**1. Client & configuration setup** — Initialize the Maxio SDK client with Basic auth (API key), target server environment (US/EU), and retry/timeout options.

**2. Endpoint `/api/subscription-plans` (GET)** — Query Maxio `Products` list, filter to active plans from the configured product family, transform to JSON response with plan details (handle, name, price).

**3. Endpoint `/api/subscriptions` (POST)** — For the authenticated shopper:
   - Look up existing customer by reference (`eShop user ID`) via `ReadCustomerByReference`
   - If not found, create idempotent customer via `CreateCustomer` using user's email as reference
   - Create subscription via `CreateSubscription` (bind customer ID + selected product handle + optional metered component)
   - Return subscription details to shopper (plan, price, state, next-billing-date)

**4. Endpoint `/api/my-subscriptions` (GET)** — For the authenticated shopper, fetch subscriptions via `ListCustomerSubscriptions` after resolving the customer record.

**5. Error boundary & retry semantics** — All operations throw `SdkException<TError>` (Case A or B per operation); implement defensive error mapping and retry logic per dotnet-error-handling guidance.

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. A members table names the namespace outright; otherwise the row's source path implies it (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root namespace). Enums, unions, auth, server and client-config types are spread across different child namespaces, and two types configured side by side in the same options object routinely live in different ones. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 1. Idempotent Customer Lookup / Creation

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Lookup operation** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Lookup signature** | Returns `CustomerResponse` (envelope wraps `Customer`); `reference` param is the eShop user ID/email; throws `SdkException<RawError>` (Case B) on 404 or other errors |
| **Lookup error** | Case B — `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| **Create operation** | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` — **must pass body explicitly** |
| **Request model** | `CreateCustomerRequest` wraps `CreateCustomer` (required) · fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?` + optional: `CcEmails`, `Organization`, `Address`, `Address2`, `City`, `State`, `Zip`, `Country`, `Phone`, `Locale`, `VatNumber`, `TaxExempt`, `TaxExemptReason`, `ParentId`, `SalesforceId` |
| **Request wire names** | Per model: `first_name`, `last_name`, `email`, `reference` (all string, at wire level) |
| **Response** | `CustomerResponse` envelope (namespace `MaxioAdvancedBilling.Models`) wraps required `Customer (customer): Customer !req` · inner `Customer` fields: `Id (id): int?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Reference (reference): string?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?` + 20+ optional fields |
| **Create error** | Case A — `SdkException<CreateCustomerError>` with `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] + `TryGetRawError(out RawError)` [fallback] |
| **Payload shape (422)** | `CustomerErrorResponse1` → `Errors (errors): Errors?` → `Errors` → `PerPage`, `PricePoint` (both `IReadOnlyList<string>?`) |
| **Pagination** | None |
| **Source** | `operations/Customers.md`, `records-2-Cr-Ne.md` (CreateCustomer, CustomerResponse, Customer), `records-1-Ac-Cr.md` (CustomerErrorResponse1) |

### 2. List Subscription Plans (Products)

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Products` |
| **Operation** | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — all nullable params except `page`/`perPage` defaults must be passed explicitly (pass `null` to skip) |
| **Signature detail** | 8 optional query params (all nullable, no defaults) + 2 pagination defaults (`page`=1, `perPage`=20) |
| **Query params (wire ← C#)** | `page`, `per_page`, `date_field`, `filter`, `start_date`, `start_datetime`, `end_date`, `end_datetime`, `include_archived`, `include` |
| **Returns** | `IReadOnlyList<ProductResponse>` — array envelope, each wraps required `Product (product): Product !req` · inner fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `CreatedAt`, `UpdatedAt`, + 15+ optional |
| **Error** | Case B — `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| **Pagination** | Manual `page`+`perPage` (no cursor); default perPage=20 sufficient for planning |
| **Filtering note** | Scope requires listing plans by product-family handle; no direct family-handle param in ListProducts; recommend query all (or filter in code by product_family_id if returned) |
| **Source** | `operations/Products.md`, `records-3-Of-Su.md` (Product, ProductResponse) |
| **Enum: IntervalUnit** | `Day (day)`, `Month (month)` — namespace `MaxioAdvancedBilling.Models.Enums` |

### 3. Create Subscription

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Subscriptions` |
| **Operation** | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` — **must pass body explicitly** |
| **Request model** | `CreateSubscriptionRequest` wraps `Subscription (subscription): CreateSubscription !req` · request fields: **payload (wire name): type, required?** · `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?` + **40+ optional fields** including: `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`, `Components (components): IReadOnlyList<CreateSubscriptionComponent>?`, `Reference (reference): string?`, `CouponCode (coupon_code): string?` |
| **Request required** | **Neither `ProductHandle` nor `ProductId` is marked `!req`** — the operation Notes state "Specify the product with `product_id` or `product_handle`"; similarly, customer is identified via either `CustomerId` or `CustomerReference` (both nullable) |
| **Component allocation** | Optional metered component: `Components` is `IReadOnlyList<CreateSubscriptionComponent>?` where each has `ComponentId (component_id): ComponentId1?` (union), `Enabled (enabled): bool?`, `Quantity (quantity): int?`, `UnitBalance (unit_balance): int?`, + optional `AllocatedQuantity`, `PricePointId`, `CustomPrice` |
| **Response** | `SubscriptionResponse` wraps required `Subscription (subscription): Subscription !req` · inner fields: `Id (id): int?`, `State (state): SubscriptionState?`, `ProductPriceInCents (product_price_in_cents): long?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `UpdatedAt (updated_at): DateTimeOffset?`, `CancelAtEndOfPeriod (cancel_at_end_of_period): bool?`, `CanceledAt (canceled_at): DateTimeOffset?`, `Product (product): Product?`, `Customer (customer): Customer?` + 35+ optional fields |
| **Error** | Case A — `SdkException<CreateSubscriptionError>` with `TryGetErrorListResponse1(out ErrorListResponse1)` [422] + `TryGetRawError(out RawError)` [fallback] |
| **Payload shape (422)** | `ErrorListResponse1` → `Errors (errors): IReadOnlyList<string> !req` — array of error messages |
| **Pagination** | None |
| **Source** | `operations/Subscriptions.md`, `records-2-Cr-Ne.md` (CreateSubscription, CreateSubscriptionRequest, CreateSubscriptionComponent), `records-3-Of-Su.md` (Subscription, SubscriptionResponse) |
| **Enum: SubscriptionState** | `Pending (pending)`, `FailedToCreate`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)` — namespace `MaxioAdvancedBilling.Models.Enums` |
| **Enum: CollectionMethod** | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` — namespace `MaxioAdvancedBilling.Models.Enums` |

### 4. List Subscriptions for Customer

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Subscriptions` |
| **Operation** | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` — 14 optional query params (all nullable) + 2 pagination defaults |
| **Query params (wire ← C#)** | `state`, `product`, `product_price_point_id`, `coupon`, `coupon_code`, `date_field`, `start_date`, `end_date`, `start_datetime`, `end_datetime`, `metadata`, `direction`, `sort`, `include`, `page`, `per_page` |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` — per row 3 above; field details match Subscription |
| **Error** | Case B — `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| **Pagination** | Manual `page`+`perPage`; default perPage=20 |
| **Filtering note** | To list by customer, use the dedicated `ListCustomerSubscriptions(int customerId, CancellationToken ct)` operation (row below) instead |
| **Source** | `operations/Subscriptions.md` |
| **Enum: SubscriptionStateFilter** | `Active (active)`, `Canceled (canceled)`, `Expired (expired)`, `ExpiredCards (expired_cards)`, `OnHold (on_hold)`, `PastDue (past_due)`, `PendingCancellation (pending_cancellation)`, `PendingRenewal (pending_renewal)`, `Suspended (suspended)`, `TrialEnded (trial_ended)`, `Trialing (trialing)`, `Unpaid (unpaid)` — namespace `MaxioAdvancedBilling.Models.Enums` |
| **Enum: SortingDirection** | `Asc (asc)`, `Desc (desc)` — namespace `MaxioAdvancedBilling.Models.Enums` |
| **Enum: SubscriptionSort** | `SignupDate (signup_date)`, `PeriodStart (period_start)`, `PeriodEnd (period_end)`, `NextAssessment (next_assessment)`, `UpdatedAt (updated_at)`, `CreatedAt (created_at)`, `TotalPayments (total_payments)`, `Id (id)`, `OpenBalance (open_balance)`, `ExpiresAt (expires_at)` — namespace `MaxioAdvancedBilling.Models.Enums` |

### 5. List Subscriptions for a Specific Customer

| Aspect | Details |
|--------|---------|
| **Controller** | `client.Customers` |
| **Operation** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` — `customerId` is the Maxio-generated customer ID (from CreateCustomer or ReadCustomerByReference response) |
| **Returns** | `IReadOnlyList<SubscriptionResponse>` — per rows 3 & 4 above |
| **Error** | Case B — `SdkException<RawError>` with `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` |
| **Pagination** | None (returns full list) |
| **Source** | `operations/Customers.md` |

---

## Enum Value Tables

### CollectionMethod (wire_value)
Source: `map/models/enums.md`

| C# Member | Wire Value | Notes |
|-----------|-----------|-------|
| `CollectionMethod.Automatic` | `"automatic"` | Default for recurring subscriptions |
| `CollectionMethod.Remittance` | `"remittance"` | Invoice-style with net terms |
| `CollectionMethod.Prepaid` | `"prepaid"` | Prepayment required |
| `CollectionMethod.Invoice` | `"invoice"` | Legacy invoice collection |

**Namespace**: `MaxioAdvancedBilling.Models.Enums`

### SubscriptionState (wire_value)
Source: `map/models/enums.md` — select subset for integration

| C# Member | Wire Value | Notes |
|-----------|-----------|-------|
| `SubscriptionState.Pending` | `"pending"` | Awaiting payment or initial charge |
| `SubscriptionState.Trialing` | `"trialing"` | In trial period (if applicable) |
| `SubscriptionState.Active` | `"active"` | Paid, current, in good standing |
| `SubscriptionState.PastDue` | `"past_due"` | Payment failed; dunning in progress |
| `SubscriptionState.Suspended` | `"suspended"` | Paused by customer or system |
| `SubscriptionState.Canceled` | `"canceled"` | Terminated by customer or dunning |
| `SubscriptionState.Expired` | `"expired"` | Reached expiration date |
| `SubscriptionState.OnHold` | `"on_hold"` | Temporarily halted |

**Namespace**: `MaxioAdvancedBilling.Models.Enums`

### IntervalUnit (wire_value)
Source: `map/models/enums.md`

| C# Member | Wire Value | Notes |
|-----------|-----------|-------|
| `IntervalUnit.Day` | `"day"` | Billing every N days |
| `IntervalUnit.Month` | `"month"` | Billing every N months (typical) |

**Namespace**: `MaxioAdvancedBilling.Models.Enums`

---

## Client Construction & Server Configuration

**HTTP Client Setup** — Maxio SDK requires a **long-lived, reusable** `System.Net.Http.HttpClient`. Register via `IHttpClientFactory` (DI) or maintain as a singleton to avoid socket exhaustion and DNS TTL issues.

**Basic Authentication** — Username = your Maxio API key (from config binding `Maxio:ApiKey`), Password = literal string `"x"`. Set in `BasicAuthCredentials` before or during client construction.

**Server Environment** — Select `ServerEnvironment.Us` (default) or `ServerEnvironment.Eu` based on your Maxio account hosting region (config binding `Maxio:Subdomain` supplies the site subdomain; internal `.Site` property defaults to this subdomain, overridable via `options.Server.Production.Us.Site`).

**Base URL Override** — For sandbox/mock servers, set `options.Server.Production.Us.BaseUrl` to the target hostname (e.g., `http://localhost:8080`). Not required for production.

**Retry & Timeout Configuration** — Handled by `MaxioAdvancedBillingClientOptions.Retry` (type `RetryOptions`, namespace `MaxioAdvancedBilling.Core.Configuration`). Members: `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. **See `dotnet-configuration-resilience` for semantics and defaults before setting.**

**Source** — `sdk-map.md` (Getting a client section), `MaxioAdvancedBillingClient.cs`, `MaxioAdvancedBillingClientOptions.cs`, `Core/Configuration/RetryOptions.cs`

---

## Trap Notes (Hazards & Required Companion Skills)

⚠ **Step 1 (client setup)** — The SDK's retry/timeout options do **not** bound a whole operation and are **not** the timeout on the `HttpClient` you register. `Timeout` is per-attempt, not total; `HttpMethodsToRetry` gates only the **status trigger** (so `503` on `POST` is not retried), but **transport failures** on any verb (including `POST`) are retried — meaning a non-idempotent write can execute more than once, and no setting prevents it. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2 (idempotent customer)** — Maxio has no built-in upsert for customers; to implement idempotent customer creation, call `ReadCustomerByReference` first (passing the eShop user ID as the reference), catch the `404` (via `RawError.StatusCode == HttpStatusCode.NotFound`), and only then create the customer if not found. The `Reference (reference)` field is your handle for lookup; **always set it to the eShop user ID on creation**. **MUST load `dotnet-error-handling`** to distinguish 404 from other errors.

⚠ **Step 3 (subscription creation)** — The `CreateSubscription` operation accepts **either** `ProductHandle` **or** `ProductId`, and **either** `CustomerId` **or** `CustomerReference`; the Notes indicate one of each pair is required, but the C# model marks both nullable, so the compiler will not enforce the rule — pass one of each explicitly. Omitting both for a pair causes a provider 422 error ("Subscription product is not specified"). The payload also lists 40+ optional fields; scope requires only the plan and customer bindings. **Never rely on field defaults; consult the Notes for each operation to determine the integration's true contract.**

⚠ **Step 4 (response envelope unwrapping)** — All responses wrap their payload in a single required field (`SubscriptionResponse.Subscription`, `CustomerResponse.Customer`, `ProductResponse.Product`). Reads go one level down. Deserializing the envelope type and then accessing `.Subscription` (not `.subscription` — C# is case-sensitive) is the contract; trying to deserialize directly to the inner type will fail. **MUST load `dotnet-models`** to confirm union/record shapes and when to unwrap.

⚠ **Step 5 (error boundary)** — Both typed (`CreateCustomerError`, `CreateSubscriptionError`) and raw (`RawError`) error types are present in scope. The SDK generates **no** `{Operation}Result` / `ApiResult` no-throw variants — every operation throws. All operations are throw-only; wrap every call in `try/catch` per the error-handling skill. **Additionally**, two `System.Text.Json.JsonException` sources reach the boundary with opposite handling: (a) a drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as an `SdkException`, so an SDK-exception-only catch lets it escape; (b) a **non-2xx** body that does not match the operation's generated error shape throws `JsonException` **while the error object is being constructed**, replacing the `SdkException` and destroying the HTTP status — an error boundary that maps every `JsonException` to 5xx then retries 5xx will retry something that cannot succeed. **MUST load `dotnet-error-handling`** before writing the boundary.

⚠ **Step 6 (calling endpoints with named arguments)** — Many optional parameters have no C# default and mis-bind in positional calls. Always use named arguments for optional params, especially on `ListProducts` and `ListSubscriptions` (14 nullable query params each). **MUST load `dotnet-calling-endpoints`** before the first SDK method call.

⚠ **Step 7 (unit testing)** — The `HttpClient` constructor parameter is the test seam; stub it with a test handler (matching the project's existing framework and assertion style). **MUST load `dotnet-testing`** before writing mocks or integration tests.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The contract sheet does not carry their contents — each skill unpacks the hazards named above and governs the step(s) where the trap bites.

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Step 1 (client & DI setup); HTTP client lifecycle; registration patterns |
| `dotnet-authentication` | Step 1 (credential storage & wiring); Basic auth parameter order and timing |
| `dotnet-calling-endpoints` | Step 2–4 (operation calls); named arguments; response envelope unwrapping; cancellation token passing |
| `dotnet-models` | Step 3–4 (request/response models); record immutability; union construction/reading; enum factories; required fields |
| `dotnet-error-handling` | Step 5 (error boundary); typed vs raw error cases; status mapping; JsonException handling; retry gates |
| `dotnet-configuration-resilience` | Step 1 (retry/timeout setup); `Timeout` semantics; `HttpMethodsToRetry` trigger; transport retry behavior |
| `dotnet-testing` | Step 6 (unit tests); HttpClient mocking; test doubles |

**These are mandatory prerequisite reads; do not skip any. The map and these skills together define the full integration contract.**

---

## Assumptions & Blockers

### Assumptions

1. **Configuration binding is provided** — `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl` are available as bound config values (not environment variables or secrets directly hardcoded). The implementing service loads them via the standard ASP.NET Core configuration pipeline (e.g., user secrets, environment, or appsettings.json).

2. **JWT authentication for the public API** — The eShop application's own `/api/subscription-*` routes are JWT-authenticated (shopper identity extracted from the token). Customer lookup and subscription operations are scoped to the authenticated user.

3. **Product family is stable** — The configured `Maxio:ProductFamilyHandle` (e.g., `eshop-subscribe`) exists and is non-empty in the sandbox site. Plan listing and subscription creation operations reference this family.

4. **Basic plan IDs are stable within the session** — Product/plan IDs (e.g., `7126957` for Pro, `7126958` for Basic) are read once at startup or cached; scope assumes stable IDs for the hero flow. **If IDs change during runtime, the integration must handle graceful 404 responses.**

5. **No payment information is collected on signup** — The sandbox plans specify `payment_method_not_required: true`. The integration does not capture credit card or bank details at subscription creation (Maxio returns a payment-method-not-required subscription state). If production requires payment on signup, that is **not in scope** and must be a separate phase.

6. **Metered component tracking is optional** — The `api-call` metered component (ID `3057195`, $0.01/unit) is present in the sandbox but not required for the hero flow. If the integration tracks per-subscription usage, that is a separate step after subscription creation (via `SubscriptionComponents` controller).

7. **Next-billing-date is derived from response** — The shopper sees the `NextAssessmentAt` field from the subscription response; no separate "next billing date" calculation is needed.

### Blockers

None identified. The map provides signatures, error cases, and model shapes for all in-scope operations. The SDK source is available and the integration path is clear.

