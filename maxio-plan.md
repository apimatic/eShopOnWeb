# Maxio Advanced Billing Integration Plan — eShopOnWeb Subscription Capability

## Scope & Sequence

1. **Client Setup** — DI registration, auth configuration, HTTP client factory
2. **Product Catalog** — list product families, list products in family
3. **Customer Identity** — create or retrieve idempotent customer (by reference)
4. **Subscription Enrollment** — create subscription, read subscription state
5. **User Subscription List** — fetch user's active subscriptions
6. **HTTP Endpoints** — three public API routes with JWT auth + error boundary

---

## CONTRACT SHEET

**Signatures are generated code, verbatim — every parameter name is the literal
C# identifier. The cancellation-token parameter really is named `ct`: in named
arguments write `ct:`, never `cancellationToken:`.**

**Every SDK type is written fully-qualified with the namespace the map gives it** — take
each one from that type's own map row, never from where a neighbouring type sits. A members
table names the namespace outright; otherwise the row's source path implies it
(`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
namespace). Enums, unions, auth, server and client-config types are spread across different
child namespaces, and two types configured side by side in the same options object routinely
live in different ones. Dropping a type to the root or to `.Models` makes the implementer
guess the wrong `using`, and the build breaks.

### Operations

| Step | Operation | Signature | Request Model | Response Envelope | Error Case | Notes | Source |
|---|---|---|---|---|---|---|---|
| 1 | `ProductFamilies.ListProductFamilies` | `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, CancellationToken ct = default)` | None (5 optional query params passed explicitly; pass `null` to skip) | `IReadOnlyList<ProductFamilyResponse>` — each element: `ProductFamily (product_family): ProductFamily?` | **Case B** `SdkException<RawError>`: `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | Returns all product families on site. Filter by `dateField` (enum: `UpdatedAt`, `CreatedAt`) and date ranges. No pagination. | `map/operations/ProductFamilies.md` |
| 1b | `Products.ListProductsForProductFamily` | `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | URL param: `productFamilyId` (string); query params: `dateField`, `filter` (object), dates, `includeArchived`, `include` (enum: `PrepaidProductPricePoint`); pagination: `page`, `perPage` | `IReadOnlyList<ProductResponse>` — each element: `Product (product): Product !req` | **Case A** `SdkException<ListProductsForProductFamilyError>`: `.TryGetString(out string)` [404] or `.TryGetRawError(out RawError)` [fallback] | Retrieves products for a specific family. Manual pagination via `page`/`perPage`. | `map/operations/ProductFamilies.md` |
| 2 | `Customers.CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` containing: `Customer (customer): CreateCustomer !req` with fields: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, plus optional address/contact fields | `CustomerResponse` containing: `Customer (customer): Customer !req` with fields: `Id (id): int?`, `Reference (reference): string?`, `Email (email): string?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, etc. | **Case A** `SdkException<CreateCustomerError>`: `.TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422] or `.TryGetRawError(out RawError)` [fallback] | Creates a new Maxio customer. The `Reference` field **MUST** be the user's app ID (e.g. eShopOnWeb `userId`) to enable idempotent lookup. If duplicate reference exists, API returns 422. **Plan must externally deduplicate by reference lookup first.** | `map/operations/Customers.md` |
| 2b | `Customers.ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query param: `reference` (string, app-defined unique key) | `CustomerResponse` containing: `Customer (customer): Customer !req` | **Case B** `SdkException<RawError>`: `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | Looks up existing customer by `reference`. Returns `Customer` with `Id` if found; throws if not found. **Step 2 must call this first; only call CreateCustomer if 404.** | `map/operations/Customers.md` |
| 3 | `Subscriptions.CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` containing: `Subscription (subscription): CreateSubscription !req` with **required** fields: `ProductHandle (product_handle): string?` or `ProductId (product_id): int?` (one required); optional: `CustomerId (customer_id): int?`, `CustomerReference (customer_reference): string?`, `Reference (reference): string?`, `DeferSignup (defer_signup): bool? = false`, plus many optional component/coupon fields. **No payment method required per scope: `PaymentProfileId` and `CreditCardAttributes` are optional.** | `SubscriptionResponse` containing: `Subscription (subscription): Subscription?` with fields: `Id (id): int?`, `State (state): SubscriptionState?`, `CustomerId (customer_id): int?`, `ProductId (product_id): int?`, `CurrentPeriodStartedAt (current_period_started_at): DateTimeOffset?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, etc. | **Case A** `SdkException<CreateSubscriptionError>`: `.TryGetErrorListResponse1(out ErrorListResponse1)` [422] or `.TryGetRawError(out RawError)` [fallback] | Creates subscription. Defaults to product's default price point. No trial, no setup fee, no payment required (per scope). `Reference` field (optional) can store caller's subscription tracking ID for de-duplication. | `map/operations/Subscriptions.md` |
| 4 | `Subscriptions.ReadSubscription` | `ReadSubscription(int subscriptionId, IReadOnlyList<SubscriptionInclude>? include, CancellationToken ct = default)` | URL param: `subscriptionId` (int); query param: `include` (enum list: `Coupons`, `SelfServicePageToken`; pass `null` to skip) | `SubscriptionResponse` containing: `Subscription (subscription): Subscription?` | **Case B** `SdkException<RawError>`: `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | Fetches subscription by ID. Returns full `Subscription` record with state, dates, customer, product, balance. | `map/operations/Subscriptions.md` |
| 5 | `Customers.ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | URL param: `customerId` (int) | `IReadOnlyList<SubscriptionResponse>` — each element: `Subscription (subscription): Subscription?` | **Case B** `SdkException<RawError>`: `.StatusCode`, `.ReadAsString()`, `.ReadAsJson<T>()`, `.ReadAsBytes()` | Lists all subscriptions for a customer. No pagination, no filters. | `map/operations/Customers.md` |

### Enums (values referenced in operations)

| Enum | Namespace | Members | Used in |
|---|---|---|---|
| `BasicDateField` | `MaxioAdvancedBilling.Models.Enums` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | ListProductFamilies |
| `ListProductsInclude` | `MaxioAdvancedBilling.Models.Enums` | `PrepaidProductPricePoint (prepaid_product_price_point)` | ListProductsForProductFamily |
| `SubscriptionInclude` | `MaxioAdvancedBilling.Models.Enums` | `Coupons (coupons)`, `SelfServicePageToken (self_service_page_token)` | ReadSubscription |
| `SubscriptionState` | `MaxioAdvancedBilling.Models.Enums` | `Active (active)`, `Trialing (trialing)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `OnHold (on_hold)`, `Paused (paused)`, `Unpaid (unpaid)`, etc. | Subscription response analysis |

### Client Construction & Authentication

```csharp
// Client registration in DI (Startup.cs or Program.cs)
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new MaxioAdvancedBilling.Core.Authentication.Basic.BasicAuthCredentials
    {
        Username = configuration["Maxio:ApiKey"],  // API key from config
        Password = "x"                              // literal "x"
    };
    o.Environment = ServerEnvironment.Us;           // or .Eu
    o.Server.Production.Us.Site = configuration["Maxio:Subdomain"];  // "cp-exp-4" for sandbox
});

// Inject MaxioAdvancedBillingClient into controllers/services
private readonly MaxioAdvancedBillingClient _maxioClient;
```

**Namespace imports required:**
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Api;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using MaxioAdvancedBilling.Errors;
```

---

## Trap Notes

⚠ **Step 1 (client registration)** — The SDK's retry/timeout options do **not** bound a whole
call and are **not** the timeout on the `HttpClient` you register. **MUST load
`dotnet-configuration-resilience`** before wiring the client.

⚠ **Step 2b + 2 (idempotent customer creation)** — ReadCustomerByReference returns 404 (throws
`SdkException<RawError>` with status 404) when no customer with that reference exists. Must
catch this explicitly, then call CreateCustomer. **MUST load `dotnet-error-handling`** to
distinguish `RawError` 404 from actual network/server errors.

⚠ **Step 3 (subscription enrollment)** — CreateSubscription accepts a body with a nested
`Subscription` record. The operation signature requires this as a `CreateSubscriptionRequest?
body` — the `?` means nullable, but `!req` on the nested `Subscription` field means the
nested record **must** be set. Pass `new CreateSubscriptionRequest { Subscription = new
CreateSubscription { ProductHandle = "...", CustomerId = ... } }`. **MUST load
`dotnet-calling-endpoints`** for named-argument call patterns.

⚠ **Steps 4–5 (reading state)** — Both ReadSubscription and ListCustomerSubscriptions return
`SubscriptionResponse` with a nullable inner `Subscription` field. The response envelope
**always** deserializes; check `response.Subscription != null` before reading state. A 404
on a deleted subscription throws `SdkException<RawError>`, not null envelope.

⚠ **Error boundary (all steps)** — Two directions to a `System.Text.Json.JsonException`:
- A **2xx** body that drops a `required` response field (e.g., Subscription state enum
  unrecognized) surfaces as `JsonException` from deserialization, **not** as an
  `SdkException` — an SDK-exception-only catch ladder lets it escape.
- A **non-2xx** body that doesn't match the operation's generated error shape (e.g., 422 with
  unexpected JSON) throws `JsonException` *while the error object is constructed*, replacing
  the `SdkException` and destroying the HTTP status — a boundary that maps every
  `JsonException` to 5xx reports a deterministic rejection as an outage; a caller that
  retries 5xx retries something that will never succeed.

**MUST load `dotnet-error-handling`** before writing the error boundary.

---

## Integration into `src/PublicApi`

### New Models (Application Layer)

Create these under `src/PublicApi/Models/Subscription/`:

- `SubscriptionPlanDto` — wire model for listing plans
  - `Handle`, `Name`, `Price` (in cents), `Interval`, `IntervalUnit`
- `SubscriptionEnrollmentRequest` — request body for POST /subscriptions
  - `ProductHandle`, optional `Reference` field
- `SubscriptionEnrollmentResponse` — response body
  - `SubscriptionId`, `State`, `NextBillingAt`
- `UserSubscriptionDto` — wire model for listing user subscriptions
  - `SubscriptionId`, `ProductName`, `State`, `CurrentPeriodStartedAt`, `CurrentPeriodEndsAt`

### New Controller

Create `src/PublicApi/Controllers/SubscriptionsController.cs`:

```csharp
[ApiController]
[Route("api/subscriptions")]
[Authorize]  // JWT-authenticated endpoints
public class SubscriptionsController : ControllerBase
{
    private readonly MaxioAdvancedBillingClient _maxio;
    
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans(CancellationToken ct)
    {
        // 1. List product family "eshop-subscribe"
        // 2. List products in that family
        // 3. Map to SubscriptionPlanDto[] and return
    }
    
    [HttpPost]
    public async Task<IActionResult> Subscribe(
        [FromBody] SubscriptionEnrollmentRequest request,
        CancellationToken ct)
    {
        // 1. Extract user ID from JWT claims (HttpContext.User)
        // 2. Call ReadCustomerByReference(userId reference)
        //    - If 404, call CreateCustomer with userId as reference
        // 3. Call CreateSubscription with customerId and productHandle
        // 4. Return SubscriptionEnrollmentResponse with subscription state
    }
    
    [HttpGet("my-subscriptions")]
    public async Task<IActionResult> GetMySubscriptions(CancellationToken ct)
    {
        // 1. Extract user ID from JWT claims
        // 2. Call ReadCustomerByReference(userId reference)
        // 3. Call ListCustomerSubscriptions(customerId)
        // 4. Map to UserSubscriptionDto[] and return
    }
}
```

### New Service Layer

Create `src/PublicApi/Services/MaxioSubscriptionService.cs`:

```csharp
public interface IMaxioSubscriptionService
{
    Task<(Customer customer, bool isNew)> GetOrCreateCustomerAsync(
        string customerReference, CreateCustomer customerData, CancellationToken ct);
    Task<Subscription> CreateSubscriptionAsync(
        int customerId, string productHandle, string? reference, CancellationToken ct);
    Task<IReadOnlyList<Subscription>> ListCustomerSubscriptionsAsync(
        int customerId, CancellationToken ct);
}

public class MaxioSubscriptionService : IMaxioSubscriptionService
{
    private readonly MaxioAdvancedBillingClient _maxio;
    
    // Implement idempotent customer retrieval:
    // 1. Try ReadCustomerByReference
    // 2. If SdkException<RawError> with 404, call CreateCustomer
    // 3. Return (Customer, isNew: bool)
    
    // Delegate CreateSubscription, ListCustomerSubscriptions
    // Handle SdkException<...Error> cases per operation row
}
```

### DI Registration

In `Program.cs` or `Startup.cs`:

```csharp
// 1. Register MaxioAdvancedBillingClient (see Client Construction above)

// 2. Register IMaxioSubscriptionService
services.AddScoped<IMaxioSubscriptionService, MaxioSubscriptionService>();

// 3. Configuration from Maxio:* section
var maxioConfig = configuration.GetSection("Maxio");
services.Configure<MaxioOptions>(maxioConfig);
```

### Configuration Schema

Bind from appsettings.json or environment variables:

```json
{
  "Maxio": {
    "ApiKey": "{{ from MAXIO_API_KEY env var }}",
    "Subdomain": "{{ from MAXIO_SITE_SUBDOMAIN env var, default: cp-exp-4 }}",
    "Environment": "{{ from MAXIO_ENVIRONMENT env var, default: US }}",
    "ProductFamilyHandle": "{{ from config or code: eshop-subscribe }}",
    "BaseUrl": "{{ optional override for mock/dev }}"
  }
}
```

**Keys must come from environment variables, never hardcoded.** Use `IConfiguration["Maxio:ApiKey"]` in startup or User Secrets for local development.

---

## REQUIRED READING

Load these companion skills **before implementation starts**. The sheet deliberately does not carry their contents; these skills hold defaults, worked examples, and binding details you must wire yourself.

| Skill | Step |
|---|---|
| `dotnet-client-initialization` | Client registration, DI, HttpClient factory reuse |
| `dotnet-authentication` | Setting API key credentials, auth scheme manager |
| `dotnet-calling-endpoints` | Named-argument call patterns, request envelope nesting, pagination handling |
| `dotnet-models` | Request/response model construction, enums (`StringEnum<T>`), union factories, field mapping |
| `dotnet-error-handling` | **CRITICAL:** Typed error accessors, `RawError` status codes, `JsonException` two-direction handling, exception boundary design |
| `dotnet-configuration-resilience` | Retry semantics (per-attempt vs. total), timeout bounds, HTTP method retry gates, non-idempotent write re-sending rules |
| `dotnet-testing` | Stubbing `HttpClient` in unit tests, mock setup patterns |

**ADDITIONAL MANDATORY HAZARDS:**

- **`System.Text.Json.JsonException` from drifted 2xx body** (missing `required` response field) surfaces as `JsonException` from deserialization, **not** as an `SdkException` — an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** to build a boundary that maps both sources.

- **`System.Text.Json.JsonException` from non-2xx body mismatch** (e.g., 422 response doesn't match the operation's typed `…Error` shape) throws `JsonException` *while the error object is constructed*, replacing the `SdkException` and destroying the HTTP status with it — a boundary that maps every `JsonException` to 5xx then reports a deterministic rejection as an outage. **MUST load `dotnet-error-handling`** before writing that boundary. These two rows belong in the first contract sheet, not a later revision: the boundary is written early.

---

## Assumptions & Blockers

### Assumptions

1. **User identity** — eShopOnWeb users have a stable `userId` (GUID or numeric) that will serve as the Maxio `customer.reference`. This must be unique per user and persist across the app's lifetime.

2. **Product configuration** — The Maxio sandbox has pre-created:
   - Product Family handle: `eshop-subscribe`
   - Plans: `eshop-pro` ($29900 cents/month), `basic-plan` ($2900 cents/month)
   - Metered component: `api-call` ($1 cent/unit)
   - All with default price points; no trial, no setup fee, no payment method requirement.

3. **No payment gateway required** — The scope specifies plans require no payment method at signup (`DeferSignup` can be true, or payment collection can be deferred). Confirm with Maxio site config that `PaymentProfileId` is optional for these products.

4. **HTTP Basic Auth** — The Maxio API key is treated as the Basic auth username; password is literal `"x"`. This is non-standard but matches the SDK's design. Verify the provided sandbox API key works with this scheme.

5. **Error recovery strategy** — CreateCustomer on duplicate reference returns 422. The service layer must catch this and fall back to ReadCustomerByReference to retrieve the existing customer, then continue. No automatic retry is needed; the error is deterministic.

6. **Subscription state machine** — Maxio subscription states include `Active`, `Canceled`, `Expired`, etc. The app must display these states to the user and refresh them via ReadSubscription on demand (no webhook/event intake in scope).

### Blockers

**None identified.** All required operations are present in the SDK map; all sandbox entities are documented as pre-configured on site `cp-exp-4`; auth scheme matches SDK baseline; configuration is expressible via environment variables and DI.

---

## Additional Notes

- **Sandbox site:** `cp-exp-4` (US environment, default)
- **API Key source:** Environment variable `MAXIO_API_KEY`
- **No PCI scope:** The scope does not accept payment method details at signup (no credit card input, no bank account data). Subscription remains in "awaiting payment" state or uses a pre-stored profile. Confirm Maxio product config allows this.
- **Idempotency:** Always call ReadCustomerByReference before CreateCustomer. Always check subscription state after CreateSubscription to confirm `Active` or expected initial state. Store returned subscription IDs in the app's subscription record for future reference.
- **Refresh token:** JWT claims extraction assumes the controller has `HttpContext.User.FindFirst("sub")` or similar ID claim. Verify eShopOnWeb's JWT issuer and claim names.
