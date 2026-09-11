# Maxio Advanced Billing Integration Plan — eShopOnWeb

## 1. Scope & Sequence

| Step | Description | SDK Operations |
|------|-------------|----------------|
| 1 | Install NuGet package & DI registration | `AddMaxioAdvancedBillingClient` |
| 2 | Configure client with API key + site subdomain | Client options construction |
| 3 | `GET /api/subscription-plans` — list plans from product family | `ListProductsForProductFamily`, `ReadProductByHandle` |
| 4 | `POST /api/subscriptions` — idempotent subscribe | `ReadCustomerByReference`, `CreateCustomer`, `CreateSubscription` |
| 5 | `GET /api/my-subscriptions` — list user's subscriptions | `ListCustomerSubscriptions` |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### Operations

#### Step 3: List Products for a Product Family

| Aspect | Detail |
|--------|--------|
| **Controller** | `client.ProductFamilies` |
| **Method** | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` |
| **Params note** | 8 nullable params (`dateField`…`include`) — no default → **must pass explicitly** (pass `null` to skip) |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.ProductResponse>` |
| **Unwrap** | Each `ProductResponse` has `.Product` → `MaxioAdvancedBilling.Models.Product` |
| **Error** | `SdkException<ListProductsForProductFamilyError>` — **Case A (typed)** |
| **Error accessors** | `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback] |
| **Pagination** | manual `page`+`perPage` |
| **Source** | `operations/ProductFamilies.md` |

**Key `Product` fields to read:**

| C# field (wire name) | Type | Notes |
|---|---|---|
| `Id (id)` | `int?` | Maxio product ID |
| `Name (name)` | `string?` | Display name |
| `Handle (handle)` | `string?` | API handle (e.g. `eshop-pro`) |
| `Description (description)` | `string?` | Plan description |
| `PriceInCents (price_in_cents)` | `long?` | Price in cents |
| `Interval (interval)` | `int?` | Billing interval |
| `IntervalUnit (interval_unit)` | `IntervalUnit?` | `day` or `month` |
| `TrialPriceInCents (trial_price_in_cents)` | `long?` | Trial price |
| `RequestCreditCard (request_credit_card)` | `bool?` | Whether CC required |
| `ProductFamily (product_family)` | `ProductFamily?` | Nested family object |
| `DefaultProductPricePointId (default_product_price_point_id)` | `int?` | Default price point |

#### Step 3 (alt): Read Product by Handle

| Aspect | Detail |
|--------|--------|
| **Controller** | `client.Products` |
| **Method** | `ReadProductByHandle(string apiHandle, CancellationToken ct = default)` |
| **Returns** | `MaxioAdvancedBilling.Models.ProductResponse` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| **Source** | `operations/Products.md` |

#### Step 4: Read Customer by Reference (idempotent lookup)

| Aspect | Detail |
|--------|--------|
| **Controller** | `client.Customers` |
| **Method** | `ReadCustomerByReference(string reference, CancellationToken ct = default)` |
| **Query params** | `reference` ← `reference` |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` |
| **Unwrap** | `CustomerResponse.Customer` → `MaxioAdvancedBilling.Models.Customer` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode` · `ReadAsString()` · etc. |
| **Source** | `operations/Customers.md` |
| **Idempotency note** | Throws `SdkException<RawError>` with 404 when not found — catch and branch to CreateCustomer |

#### Step 4: Create Customer

| Aspect | Detail |
|--------|--------|
| **Controller** | `client.Customers` |
| **Method** | `CreateCustomer(MaxioAdvancedBilling.Models.CreateCustomerRequest? body, CancellationToken ct = default)` |
| **body** | nullable, no default → **must pass explicitly** |
| **Returns** | `MaxioAdvancedBilling.Models.CustomerResponse` |
| **Error** | `SdkException<CreateCustomerError>` — **Case A (typed)** |
| **Error accessors** | `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Source** | `operations/Customers.md` |

**`CreateCustomerRequest` shape (wire names):**

```csharp
new MaxioAdvancedBilling.Models.CreateCustomerRequest
{
    Customer = new MaxioAdvancedBilling.Models.CreateCustomer
    {
        FirstName = "...",    // !req (wire: first_name)
        LastName = "...",     // !req (wire: last_name)
        Email = "...",        // !req (wire: email)
        Reference = "...",    // optional — use eShopOnWeb user ID for idempotent lookup
        Organization = "...", // optional
    }
}
```

**`CreateCustomer` required fields:** `FirstName`, `LastName`, `Email` (all `string !req`).
**Optional fields for idempotency:** `Reference` — set to the eShopOnWeb user's ID to enable `ReadCustomerByReference` lookup.

**`Customer` response fields (key ones):**

| C# field (wire name) | Type |
|---|---|
| `Id (id)` | `int?` |
| `FirstName (first_name)` | `string?` |
| `LastName (last_name)` | `string?` |
| `Email (email)` | `string?` |
| `Reference (reference)` | `string?` |

#### Step 4: Create Subscription

| Aspect | Detail |
|--------|--------|
| **Controller** | `client.Subscriptions` |
| **Method** | `CreateSubscription(MaxioAdvancedBilling.Models.CreateSubscriptionRequest? body, CancellationToken ct = default)` |
| **body** | nullable, no default → **must pass explicitly** |
| **Returns** | `MaxioAdvancedBilling.Models.SubscriptionResponse` |
| **Unwrap** | `SubscriptionResponse.Subscription` → `MaxioAdvancedBilling.Models.Subscription?` |
| **Error** | `SdkException<CreateSubscriptionError>` — **Case A (typed)** |
| **Error accessors** | `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback] |
| **Source** | `operations/Subscriptions.md` |

**`CreateSubscriptionRequest` shape:**

```csharp
new MaxioAdvancedBilling.Models.CreateSubscriptionRequest
{
    Subscription = new MaxioAdvancedBilling.Models.CreateSubscription
    {
        ProductHandle = "eshop-pro",       // or ProductId = <int>
        CustomerId = <maxio_customer_id>,  // or CustomerReference = "eshop-user-123"
        // No payment method needed for these sandbox plans (require_credit_card = false)
    }
}
```

**`CreateSubscription` key fields:**

| C# field (wire name) | Type | Required? | Notes |
|---|---|---|---|
| `ProductHandle (product_handle)` | `string?` | optional | Use this OR `ProductId` |
| `ProductId (product_id)` | `int?` | optional | Use this OR `ProductHandle` |
| `CustomerId (customer_id)` | `int?` | optional | Use this OR `CustomerReference` |
| `CustomerReference (customer_reference)` | `string?` | optional | Use this OR `CustomerId` |
| `ProductPricePointHandle (product_price_point_handle)` | `string?` | optional | Specific price point |
| `ProductPricePointId (product_price_point_id)` | `int?` | optional | Specific price point |
| `CouponCode (coupon_code)` | `string?` | optional | |
| `CouponCodes (coupon_codes)` | `IReadOnlyList<string>?` | optional | |
| `PaymentCollectionMethod (payment_collection_method)` | `CollectionMethod?` | optional | |
| `Reference (reference)` | `string?` | optional | Client-side unique reference |
| `NextBillingAt (next_billing_at)` | `DateTimeOffset?` | optional | |
| `ExpiresAt (expires_at)` | `DateTimeOffset?` | optional | |

**`Subscription` response fields (key ones):**

| C# field (wire name) | Type |
|---|---|
| `Id (id)` | `int?` |
| `State (state)` | `SubscriptionState?` |
| `ProductId (product_id)` | `int?` (on nested `Product`) |
| `CurrentPeriodEndsAt (current_period_ends_at)` | `DateTimeOffset?` |
| `NextAssessmentAt (next_assessment_at)` | `DateTimeOffset?` |
| `ActivatedAt (activated_at)` | `DateTimeOffset?` |
| `CreatedAt (created_at)` | `DateTimeOffset?` |
| `Product (product)` | `Product?` |
| `Customer (customer)` | `Customer?` |

#### Step 5: List Customer Subscriptions

| Aspect | Detail |
|--------|--------|
| **Controller** | `client.Customers` |
| **Method** | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` |
| **Returns** | `IReadOnlyList<MaxioAdvancedBilling.Models.SubscriptionResponse>` |
| **Error** | `SdkException<RawError>` — **Case B** |
| **Error accessors** | `StatusCode` · `ReadAsString()` · etc. |
| **Pagination** | none |
| **Source** | `operations/Customers.md` |

---

### Enum Values Needed

| Enum | Namespace | Members used |
|------|-----------|--------------|
| `IntervalUnit` | `MaxioAdvancedBilling.Models.Enums` | `Month (month)`, `Day (day)` |
| `CollectionMethod` | `MaxioAdvancedBilling.Models.Enums` | `Automatic (automatic)`, `Invoice (invoice)` |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active (active)`, `Trialing (trialing)`, `Canceled (collapsed)`, `PastDue (past_due)`, `Pending (pending)` |
| `ServerEnvironment` | `MaxioAdvancedBilling.Servers` | `Us (US)`, `Eu (EU)` |

---

### Client Construction & Auth

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials
    {
        Username = "<api_key>",  // from configuration, never hardcoded
        Password = "x"           // literal "x"
    },
    Environment = ServerEnvironment.Us,
    // Set site subdomain:
    Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new ServerEntryOptions { Site = "cp-exp-6" }
        }
    },
    Retry = RetryOptions.Default() with
    {
        Timeout = TimeSpan.FromSeconds(15)  // per-attempt; interactive path needs shorter than 100s default
    }
};

var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**DI registration pattern:**

```csharp
using MaxioAdvancedBilling;

builder.Services.AddMaxioAdvancedBillingClient(options =>
{
    options.BasicAuth = new BasicAuthCredentials
    {
        Username = config["Maxio:ApiKey"]!,
        Password = "x"
    };
    options.Environment = ServerEnvironment.Us;
    options.Server = new ServerOptions
    {
        Production = new ProductionOptions
        {
            Us = new ServerEntryOptions { Site = config["Maxio:Site"]! }
        }
    };
});
```

---

### Namespaces Required

```csharp
using MaxioAdvancedBilling;                          // Client, options
using MaxioAdvancedBilling.Core.Authentication.Basic; // BasicAuthCredentials
using MaxioAdvancedBilling.Core.Configuration;        // RetryOptions
using MaxioAdvancedBilling.Core.Exceptions;           // SdkException<T>
using MaxioAdvancedBilling.Core.ErrorResponse;        // RawError, ApiError
using MaxioAdvancedBilling.Errors;                    // CreateCustomerError, CreateSubscriptionError
using MaxioAdvancedBilling.Models;                    // All request/response records
using MaxioAdvancedBilling.Models.Enums;              // IntervalUnit, CollectionMethod, etc.
using MaxioAdvancedBilling.Servers;                   // ServerEnvironment
```

---

## 3. Trap Notes

⚠ Step 2 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. `Timeout` is per-attempt, and transport failures are retried on every verb including POST, so a non-idempotent `CreateSubscription` can execute more than once. **MUST load `dotnet-configuration-resilience`** before wiring the client.

⚠ Step 4 (CreateSubscription) — `CreateSubscription` is a POST, so `HttpMethodsToRetry` does not gate *status* retries on it — but `HttpRequestException` transport failures ARE retried on every verb. A duplicate subscription creation is possible on transport failure. **MUST load `dotnet-configuration-resilience`** and consider idempotency via the `Reference` field.

⚠ Step 4 (idempotent customer/subscription) — `ReadCustomerByReference` throws `SdkException<RawError>` on 404 (not a typed error). Catch `SdkException<RawError>` and check `ex.Error.StatusCode == HttpStatusCode.NotFound` to branch to create. **MUST load `dotnet-error-handling`** before writing this boundary.

⚠ All SDK operations are **throw-only** — there are no `…Result` no-throw variants. Every call must be wrapped in try/catch. **MUST load `dotnet-error-handling`** before writing any error boundary.

⚠ `System.Text.Json.JsonException` can reach the boundary from two directions: (1) a drifted/malformed **2xx** body surfaces as `JsonException` from deserialization, **not** as `SdkException`; (2) a **non-2xx** body that doesn't match its `{Operation}Error` shape throws `JsonException` while the error object is being constructed, destroying the HTTP status. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ The `SubscriptionResponse.Subscription` field is **nullable** (`Subscription?`) — always null-check before accessing nested fields. The `ProductResponse.Product` and `CustomerResponse.Customer` fields are **required** (`!req`) but the outer type can still be null if the API returns an empty envelope.

⚠ `ListProductsForProductFamily` takes `string productFamilyId` (not `int`) — the product family handle `eshop-subscribe` works directly. The `ListProductsForProductFamilyError` is Case A with `TryGetString(out string)` [404] — the 404 body is a plain string, not a JSON object.

---

## 4. REQUIRED READING

These companion skills are to be loaded **before implementation starts**. The sheet deliberately does not carry their contents.

| Skill | Step it governs |
|-------|-----------------|
| `dotnet-client-initialization` | Step 1–2: client construction, DI, HttpClient lifetime |
| `dotnet-authentication` | Step 2: Basic auth credentials wiring |
| `dotnet-calling-endpoints` | Steps 3–5: calling SDK operations, named arguments, response unwrapping |
| `dotnet-models` | Steps 3–5: request body construction, enums, nullable handling |
| `dotnet-error-handling` | All steps: try/catch boundaries, Case A vs Case B, JsonException traps |
| `dotnet-configuration-resilience` | Step 2: retries, timeouts, server config, transport retry gotchas |
| `dotnet-testing` | All steps: stub seam, error path testing |

---

## 5. Assumptions & Blockers

| Item | Status |
|------|--------|
| Sandbox site `cp-exp-6` is accessible and seeded with the product family `eshop-subscribe`, plans `eshop-pro`/`basic-plan`, and metered component `api-call` | Assumed per brief |
| Payment method is NOT required for subscription creation (`require_credit_card = false` on both plans) | Per brief |
| eShopOnWeb users have a stable unique ID that can be used as the Maxio `reference` field for idempotent customer lookup | Assumed — use the user's ASP.NET Identity ID |
| The `AddMaxioAdvancedBillingClient` DI extension exists and works as documented in `dotnet-client-initialization` | Per map + companion skill |
| The `ListProductsForProductFamily` operation accepts the product family handle string (`eshop-subscribe`) as `productFamilyId` | Per map: param is `string` not `int`, and `ReadProductFamily` notes "can be specified with the `handle:my-family` format" |
| No blockers identified — all operations needed for the hero flow are in the map |

---

## 6. Implementation Notes

### Idempotent Subscribe Flow (Step 4)

```
1. ReadCustomerByReference(userId)
   ├── Success → use existing customer.Id
   └── SdkException<RawError> with 404 → CreateCustomer (with Reference = userId)
       └── use new customer.Id

2. CreateSubscription (with CustomerId = customer.Id, ProductHandle = planHandle)
   └── SdkException<CreateSubscriptionError> with 422 → check error body for duplicate/existing
```

### Response Unwrapping Pattern

Every response type wraps its payload in a single property:
- `ProductResponse.Product` → `Product`
- `CustomerResponse.Customer` → `Customer`
- `SubscriptionResponse.Subscription` → `Subscription` (nullable!)
- `IReadOnlyList<ProductResponse>` → iterate, unwrap each `.Product`

### Error Handling Pattern (Case A vs Case B)

| Operation | Case | Catch type |
|-----------|------|------------|
| `ListProductsForProductFamily` | A | `SdkException<ListProductsForProductFamilyError>` |
| `ReadProductByHandle` | B | `SdkException<RawError>` |
| `ReadCustomerByReference` | B | `SdkException<RawError>` |
| `CreateCustomer` | A | `SdkException<CreateCustomerError>` |
| `CreateSubscription` | A | `SdkException<CreateSubscriptionError>` |
| `ListCustomerSubscriptions` | B | `SdkException<RawError>` |
