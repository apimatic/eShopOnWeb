# Maxio Advanced Billing Integration — eShopOnWeb

## 1. Scope & Sequence

| Step | Description | SDK Operations Used |
|------|-------------|---------------------|
| 1 | Register SDK client in DI (singleton, with retry/timeout) | — |
| 2 | `GET /api/subscription-plans` — list products in the `eshop-subscribe` product family | `client.ProductFamilies.ListProductsForProductFamily` |
| 3 | `POST /api/subscriptions` — idempotent customer creation + subscription enrollment | `client.Customers.CreateCustomer` (or `ReadCustomerByReference` + `CreateCustomer`), `client.Subscriptions.CreateSubscription` |
| 4 | `GET /api/my-subscriptions` — list subscriptions for a customer | `client.Customers.ListCustomerSubscriptions` |
| 5 | Error boundary + response mapping | — |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits.

### 2.1 Client Construction & Auth

| Fact | Value | Source |
|------|-------|--------|
| Package id | `AsadAli.AdvancedBilling.Sdk` | `sdk-map.md` |
| Root namespace | `MaxioAdvancedBilling` | `sdk-map.md` |
| Client class | `MaxioAdvancedBillingClient` | `sdk-map.md` |
| Options class | `MaxioAdvancedBillingClientOptions` | `sdk-map.md` |
| Auth property | `BasicAuth` (type: `BasicAuthCredentials`) | `sdk-map.md` |
| Auth pattern | `Username` = API key, `Password` = literal `"x"` | `sdk-map.md` |
| Environment enum | `ServerEnvironment.Us` (default), `ServerEnvironment.Eu` | `sdk-map.md` |
| Site param | `options.Server.Production.Us.Site = "<subdomain>"` | `sdk-map.md` |
| DI extension | `services.AddMaxioAdvancedBillingClient(o => { ... })` | `sdk-map.md` |

**Required usings:**
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Errors;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Core.ErrorResponse;
```

### 2.2 Operation: ListProductsForProductFamily

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.ProductFamilies` | `map/operations/ProductFamilies.md` |
| Method | `ListProductsForProductFamily` | `map/operations/ProductFamilies.md` |
| HTTP | `GET /product_families/{product_family_id}/products.json` | `map/operations/ProductFamilies.md` |

**Signature:**
```csharp
public Task<IReadOnlyList<ProductResponse>> ListProductsForProductFamily(
    string productFamilyId,          // path param — REQUIRED, pass as "3023074"
    BasicDateField? dateField,       // nullable, no default → pass null
    ListProductsFilter? filter,      // nullable, no default → pass null
    DateTimeOffset? startDate,       // nullable, no default → pass null
    DateTimeOffset? endDate,         // nullable, no default → pass null
    DateTimeOffset? startDatetime,   // nullable, no default → pass null
    DateTimeOffset? endDatetime,     // nullable, no default → pass null
    bool? includeArchived,           // nullable, no default → pass null
    ListProductsInclude? include,    // nullable, no default → pass null
    int? page = 1,
    int? perPage = 20,
    CancellationToken ct = default)
```

**Return type:** `IReadOnlyList<ProductResponse>` — each item wraps a `Product`:
- `ProductResponse.Product` → `Product` (required)
  - `Product.Id` (int?) — product ID
  - `Product.Name` (string?) — product name
  - `Product.Handle` (string?) — product handle (e.g. `"eshop-pro"`)
  - `Product.Description` (string?)
  - `Product.PriceInCents` (long?) — price in cents (e.g. `29900` = $299.00)
  - `Product.Interval` (int?) — billing interval (e.g. `1`)
  - `Product.IntervalUnit` (IntervalUnit?) — `IntervalUnit.Month` or `IntervalUnit.Day`
  - `Product.RequireCreditCard` (bool?)
  - `Product.DefaultProductPricePointId` (int?)

**Error case:** `SdkException<ListProductsForProductFamilyError>` — **Case A (typed)**
- `TryGetString(out string)` [404]
- `TryGetRawError(out RawError)` [fallback]

**Notes:** The `productFamilyId` is a `string` path param. Pass the seeded ID `"3023074"`.

### 2.3 Operation: CreateCustomer

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.Customers` | `map/operations/Customers.md` |
| Method | `CreateCustomer` | `map/operations/Customers.md` |
| HTTP | `POST /customers.json` | `map/operations/Customers.md` |

**Signature:**
```csharp
public Task<CustomerResponse> CreateCustomer(
    CreateCustomerRequest? body,     // nullable, no default → MUST pass explicitly
    CancellationToken ct = default)
```

**Request model — `CreateCustomerRequest`:**
```csharp
new CreateCustomerRequest
{
    Customer = new CreateCustomer  // required
    {
        FirstName = "...",         // !req (string)
        LastName = "...",          // !req (string)
        Email = "...",             // !req (string)
        Reference = "...",         // optional — use for idempotency (e.g. user's app ID)
    }
}
```

**`CreateCustomer` fields (all from `records-1-Ac-Cr.md`):**
| Field | Wire name | Type | Required? |
|-------|-----------|------|-----------|
| `FirstName` | `first_name` | `string` | !req |
| `LastName` | `last_name` | `string` | !req |
| `Email` | `email` | `string` | !req |
| `CcEmails` | `cc_emails` | `string?` | optional |
| `Organization` | `organization` | `string?` | optional |
| `Reference` | `reference` | `string?` | optional — **use for idempotent lookup** |
| `Address` | `address` | `string?` | optional |
| `Address2` | `address_2` | `string?` | optional |
| `City` | `city` | `string?` | optional |
| `State` | `state` | `string?` | optional |
| `Zip` | `zip` | `string?` | optional |
| `Country` | `country` | `string?` | optional |
| `Phone` | `phone` | `string?` | optional |
| `Locale` | `locale` | `string?` | optional |
| `VatNumber` | `vat_number` | `string?` | optional |
| `TaxExempt` | `tax_exempt` | `bool?` | optional |
| `TaxExemptReason` | `tax_exempt_reason` | `string?` | optional |
| `ParentId` | `parent_id` | `int?` | optional |
| `SalesforceId` | `salesforce_id` | `string?` | optional |

**Return type:** `CustomerResponse` — wraps a `Customer`:
- `CustomerResponse.Customer` → `Customer` (required)
  - `Customer.Id` (int?) — Maxio customer ID
  - `Customer.FirstName`, `LastName`, `Email`, `Reference` — as provided

**Error case:** `SdkException<CreateCustomerError>` — **Case A (typed)**
- `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422]
- `TryGetRawError(out RawError)` [fallback]

**Idempotency strategy:** Use `ReadCustomerByReference` (below) before `CreateCustomer`. The `Reference` field is unique per site — set it to the eShopOnWeb user's ID. If `ReadCustomerByReference` returns a customer, skip creation.

### 2.4 Operation: ReadCustomerByReference

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.Customers` | `map/operations/Customers.md` |
| Method | `ReadCustomerByReference` | `map/operations/Customers.md` |
| HTTP | `GET /customers/lookup.json` | `map/operations/Customers.md` |

**Signature:**
```csharp
public Task<CustomerResponse> ReadCustomerByReference(
    string reference,                // query param — REQUIRED
    CancellationToken ct = default)
```

**Return type:** `CustomerResponse` — same shape as above.

**Error case:** `SdkException<RawError>` — **Case B**
- On 404: the customer doesn't exist → create one.
- Read `ex.Error.StatusCode` to distinguish not-found from other errors.

### 2.5 Operation: CreateSubscription

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.Subscriptions` | `map/operations/Subscriptions.md` |
| Method | `CreateSubscription` | `map/operations/Subscriptions.md` |
| HTTP | `POST /subscriptions.json` | `map/operations/Subscriptions.md` |

**Signature:**
```csharp
public Task<SubscriptionResponse> CreateSubscription(
    CreateSubscriptionRequest? body, // nullable, no default → MUST pass explicitly
    CancellationToken ct = default)
```

**Request model — `CreateSubscriptionRequest`:**
```csharp
new CreateSubscriptionRequest
{
    Subscription = new CreateSubscription  // required
    {
        ProductId = 7126957,               // or ProductHandle = "eshop-pro"
        CustomerId = <maxio_customer_id>,  // from CreateCustomer or ReadCustomerByReference
    }
}
```

**`CreateSubscription` fields (key ones from `records-2-Cr-Ne.md`):**
| Field | Wire name | Type | Required? | Notes |
|-------|-----------|------|-----------|-------|
| `ProductHandle` | `product_handle` | `string?` | optional | Use `ProductId` OR `ProductHandle` |
| `ProductId` | `product_id` | `int?` | optional | Use `ProductId` OR `ProductHandle` |
| `ProductPricePointHandle` | `product_price_point_handle` | `string?` | optional | To select a specific price point |
| `ProductPricePointId` | `product_price_point_id` | `int?` | optional | To select a specific price point |
| `CustomerId` | `customer_id` | `int?` | optional | Use `CustomerId` OR `CustomerReference` |
| `CustomerReference` | `customer_reference` | `string?` | optional | Use `CustomerId` OR `CustomerReference` |
| `PaymentProfileId` | `payment_profile_id` | `int?` | optional | Existing payment profile |
| `CouponCode` | `coupon_code` | `string?` | optional | Single coupon code |
| `CouponCodes` | `coupon_codes` | `IReadOnlyList<string>?` | optional | Multiple coupon codes |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | optional | `CollectionMethod.Automatic` |
| `CustomerAttributes` | `customer_attributes` | `CustomerAttributes?` | optional | For inline customer creation |
| `PaymentProfileAttributes` | `payment_profile_attributes` | `PaymentProfileAttributes?` | optional | For inline payment profile |
| `Reference` | `reference` | `string?` | optional | Unique reference for the subscription |
| `NextBillingAt` | `next_billing_at` | `DateTimeOffset?` | optional | Override next billing date |
| `AgreementTerms` | `agreement_terms` | `string?` | optional | Required for Maxio Payments |

**Return type:** `SubscriptionResponse` — wraps a `Subscription`:
- `SubscriptionResponse.Subscription` → `Subscription` (nullable)
  - `Subscription.Id` (int?) — subscription ID
  - `Subscription.State` (SubscriptionState?) — e.g. `SubscriptionState.Active`
  - `Subscription.ProductPriceInCents` (long?) — price in cents
  - `Subscription.CurrentPeriodEndsAt` (DateTimeOffset?)
  - `Subscription.NextAssessmentAt` (DateTimeOffset?) — next billing date
  - `Subscription.ActivatedAt` (DateTimeOffset?)
  - `Subscription.CreatedAt` (DateTimeOffset?)
  - `Subscription.Product` (Product?) — nested product details
  - `Subscription.Customer` (Customer?) — nested customer details

**Error case:** `SdkException<CreateSubscriptionError>` — **Case A (typed)**
- `TryGetErrorListResponse1(out ErrorListResponse1)` [422]
- `TryGetRawError(out RawError)` [fallback]

### 2.6 Operation: ListCustomerSubscriptions

| Fact | Value | Source |
|------|-------|--------|
| Controller | `client.Customers` | `map/operations/Customers.md` |
| Method | `ListCustomerSubscriptions` | `map/operations/Customers.md` |
| HTTP | `GET /customers/{customer_id}/subscriptions.json` | `map/operations/Customers.md` |

**Signature:**
```csharp
public Task<IReadOnlyList<SubscriptionResponse>> ListCustomerSubscriptions(
    int customerId,                  // path param — REQUIRED
    CancellationToken ct = default)
```

**Return type:** `IReadOnlyList<SubscriptionResponse>` — each item wraps a `Subscription` (same shape as above).

**Error case:** `SdkException<RawError>` — **Case B**

---

## 3. Trap Notes

⚠ Step 1 (client registration) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-client-initialization`** before wiring the client.

⚠ Step 1 (client registration) — `MaxRetries = 0` is rejected at construction; the floor is 1 (meaning 2 attempts). Transport failures (`HttpRequestException`) are retried on **every verb** including `POST`, so a non-idempotent write can execute more than once. **MUST load `dotnet-configuration-resilience`** before wiring retry options.

⚠ Step 1 (client registration) — the `BasicAuth` property takes `BasicAuthCredentials` with `Username` = API key, `Password` = literal `"x"`. Setting them in the wrong order or using `Password` = API key will produce 401s. **MUST load `dotnet-authentication`** before setting credentials.

⚠ Step 3 (create subscription) — `CreateSubscriptionRequest.Subscription.ProductId` and `ProductHandle` are both optional; at least one must be provided or the call fails. Similarly, `CustomerId` or `CustomerReference` must identify the customer. **MUST load `dotnet-calling-endpoints`** before building the request.

⚠ Steps 2–4 (all SDK calls) — every operation throws `SdkException<TError>` on non-2xx. `TError` varies per operation (see contract sheet above). `System.Text.Json.JsonException` can reach the boundary from two directions: (a) a drifted 2xx body deserializes as `JsonException`, not `SdkException`; (b) a non-2xx body that doesn't match its `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, destroying the HTTP status. **MUST load `dotnet-error-handling`** before writing the error boundary.

⚠ Step 3 (create subscription) — transport retries on `POST` are not gated by `HttpMethodsToRetry` (that only gates status retries). An `HttpRequestException` on a `POST` will be retried, so the subscription creation may reach the provider more than once. For idempotency, use the `Reference` field on both customer and subscription if the provider enforces uniqueness on it. **MUST load `dotnet-configuration-resilience`** before relying on idempotency.

---

## 4. REQUIRED READING

Load these **before implementation starts**. The sheet deliberately does not carry their contents.

| Skill | Governs |
|-------|---------|
| `dotnet-client-initialization` | Client construction, `HttpClient` lifetime, DI registration |
| `dotnet-authentication` | Setting `BasicAuth` credentials correctly |
| `dotnet-calling-endpoints` | Named arguments, request body construction, response unwrapping |
| `dotnet-models` | Record construction, enum usage (`StringEnum<T>`), union types |
| `dotnet-error-handling` | `SdkException<TError>` catch ladders, `TryGet*` accessors, `RawError` fallback — **load before writing ANY try/catch** |
| `dotnet-configuration-resilience` | Retry/timeout tuning, per-attempt vs total timeout, transport retry semantics |
| `dotnet-testing` | Test seam (`HttpClient` + `StubHandler`), asserting request/response |

**`System.Text.Json.JsonException` hazard — load `dotnet-error-handling` and internalize both paths:**
- a drifted or malformed **2xx** body surfaces as `JsonException` from deserialization, **not** as `SdkException` — so an SDK-exception-only catch ladder lets it escape the boundary;
- a **non-2xx** body that doesn't match its `{Operation}Error` shape throws `JsonException` while the error object is being constructed, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a xx then reports a deterministic rejection as an outage, and a caller that retries retries something that can never succeed.

---

## 5. Assumptions & Blockers

| Item | Status |
|------|--------|
| Sandbox site `cp-exp-7` is active and the seeded entities (product family ID 3023074, products 7126957/7126958, component 3057195) exist | Assumed — verify with `ListProductsForProductFamily` on first run |
| No payment gateway is configured on the sandbox — `CreateSubscription` may fail with a payment-related 422 if the product requires a credit card | **Blocker if `Product.RequireCreditCard` is true** — check the `Product` response from step 2; if true, either set `RequireCreditCard = false` on the product or provide `PaymentProfileAttributes` in the subscription request |
| The eShopOnWeb user has a stable integer ID usable as the `Reference` for Maxio customer lookup | Assumed — the app's `ApplicationUser.Id` is a string (GUID); convert or hash to a stable reference value |
| `DOTNET_ROLL_FORWARD=Major` is set in the environment so .NET 10 SDK can build the .NET 8 pinned project | Per the brief |
| In-memory database (`UseOnlyInMemoryDatabase=true`) is already configured | Per the brief |
