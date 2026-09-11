# Maxio Advanced Billing Integration Plan — eShopOnWeb

## 1. Scope & Sequence

This integration adds recurring-subscription billing as an additive, parallel capability to the existing one-time commerce flow. Three new API endpoints are added to the Web project, backed by a new service layer in Infrastructure that wraps the Maxio SDK.

| Step | Operations Used | Purpose |
|------|----------------|---------|
| 1 | (none) | Add NuGet package + config binding |
| 2 | (none) | Create service interface + implementation in Infrastructure |
| 3 | `Products.ListProducts` | GET /api/subscription-plans — list plans from Maxio |
| 4 | `Customers.CreateCustomer`, `Customers.ReadCustomerByReference`, `Subscriptions.CreateSubscription` | POST /api/subscriptions — subscribe user to a plan (idempotent) |
| 5 | `Customers.ReadCustomerByReference`, `Customers.ListCustomerSubscriptions` | GET /api/my-subscriptions — list current user's subscriptions |
| 6 | (none) | Wire DI registrations + add MinimalApi.Endpoint implementations |
| 7 | (none) | Add configuration model + appsettings binding |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal C# identifier. The cancellation-token parameter really is named `ct`: in named arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take each one from that type's own map row, never from where a neighbouring type sits. Dropping a type to the root or to `.Models` makes the implementer guess the wrong `using`, and the build breaks.

### 2.1 SDK Identity

| Fact | Value | Source |
|------|-------|--------|
| NuGet package | `AsadAli.AdvancedBilling.Sdk` | sdk-map.md |
| Root namespace | `MaxioAdvancedBilling` | sdk-map.md |
| Client class | `MaxioAdvancedBillingClient` | sdk-map.md |
| Options class | `MaxioAdvancedBillingClientOptions` | sdk-map.md |
| Auth scheme | HTTP Basic — `Username` = API key, `Password` = literal `"x"` | sdk-map.md |
| Environments | `ServerEnvironment.Us` (default) → `https://{site}.chargify.com`; `ServerEnvironment.Eu` → `https://{site}.ebilling.maxio.com` | sdk-map.md |

### 2.2 Client Construction

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" },
    Environment = ServerEnvironment.Us,
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

**DI alternative** (`ServiceCollectionExtensions.cs`):
```csharp
services.AddMaxioAdvancedBillingClient(o =>
{
    o.BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" };
});
```

> Source: `sdk-map.md` (Getting a client section)

### 2.3 Operation Contract Table

| # | Controller Property | Method Signature | Request Model + Fields | Response Envelope + Inner Fields | Error Case + Accessors | Pagination | Source |
|---|---------------------|------------------|------------------------|----------------------------------|------------------------|------------|--------|
| 1 | `client.Products` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | (all query params; no body) — 8 nullable params `dateField`…`include` must be passed explicitly (pass `null` to skip); defaults: `page = 1`, `perPage = 20` | `IReadOnlyList<ProductResponse>` — each item has `Product (product): Product` with fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `ProductPricePointHandle (product_price_point_handle): string?`, `ProductPricePointId (product_price_point_id): int?`, `DefaultProductPricePointId (default_product_price_point_id): int?` | **Case B** — `SdkException<RawError>`: `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()` | Manual `page`+`perPage` | operations/Products.md, records-3-Of-Su.md (`ProductResponse`, `Product`) |
| 2 | `client.Customers` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | Query: `reference` (string, required) | `CustomerResponse` — `Customer (customer): Customer` with fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?` | **Case B** — `SdkException<RawError>` | none | operations/Customers.md, records-1-Ac-Cr.md (`CustomerResponse`, `Customer`) |
| 3 | `client.Customers` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CreateCustomerRequest` → `Customer (customer): CreateCustomer !req` → `CreateCustomer`: `FirstName (first_name): string !req`, `LastName (last_name): string !req`, `Email (email): string !req`, `Reference (reference): string?`, `Organization (organization): string?` | `CustomerResponse` — `Customer (customer): Customer` with fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?` | **Case A** — `SdkException<CreateCustomerError>`: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | none | operations/Customers.md, records-1-Ac-Cr.md (`CreateCustomerRequest`, `CreateCustomer`, `CustomerResponse`) |
| 4 | `client.Subscriptions` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `CreateSubscriptionRequest` → `Subscription (subscription): CreateSubscription !req` → `CreateSubscription`: `ProductHandle (product_handle): string?`, `ProductId (product_id): int?`, `CustomerReference (customer_reference): string?`, `CustomerId (customer_id): int?`, `Reference (reference): string?`, `ProductPricePointHandle (product_price_point_handle): string?` | `SubscriptionResponse` — `Subscription (subscription): Subscription?` with fields: `Id (id): int?`, `State (state): SubscriptionState?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `Product (product): Product?`, `Customer (customer): Customer?` | **Case A** — `SdkException<CreateSubscriptionError>`: `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] | none | operations/Subscriptions.md, records-2-Cr-Ne.md (`CreateSubscriptionRequest`, `CreateSubscription`), records-4-Su-We.md (`SubscriptionResponse`, `Subscription`) |
| 5 | `client.Customers` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | Path: `customerId` (int, required) | `IReadOnlyList<SubscriptionResponse>` — each item: `Subscription (subscription): Subscription?` | **Case B** — `SdkException<RawError>` | none | operations/Customers.md, records-4-Su-We.md (`SubscriptionResponse`) |

### 2.4 Key Enums Needed

| Enum | Members (wire values) | Used In | Source |
|------|----------------------|---------|--------|
| `IntervalUnit` | `Day (day)`, `Month (month)` | `Product.IntervalUnit` | enums.md |
| `SubscriptionState` | `Active (active)`, `Canceled (canceled)`, `Trialing (trialing)`, `PastDue (past_due)`, `Pending (pending)`, + 10 more | `Subscription.State` | enums.md |
| `CollectionMethod` | `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)` | `CreateSubscription.PaymentCollectionMethod` | enums.md |
| `BasicDateField` | `UpdatedAt (updated_at)`, `CreatedAt (created_at)` | `ListProducts.dateField` | enums.md |
| `SortingDirection` | `Asc (asc)`, `Desc (desc)` | (optional sort) | enums.md |

### 2.5 Error Handling Summary

**No-throw (`…Result`) variants are absent across this SDK** — every operation is throw-only. Wrap every call in `try/catch`.

| Operation | Catch Type | Accessors |
|-----------|-----------|-----------|
| `ListProducts` | `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |
| `ReadCustomerByReference` | `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |
| `CreateCustomer` | `SdkException<CreateCustomerError>` | `ex.Error.TryGetCustomerErrorResponse1(out var e422)`, `ex.Error.TryGetRawError(out var raw)` |
| `CreateSubscription` | `SdkException<CreateSubscriptionError>` | `ex.Error.TryGetErrorListResponse1(out var e422)`, `ex.Error.TryGetRawError(out var raw)` |
| `ListCustomerSubscriptions` | `SdkException<RawError>` | `ex.Error.StatusCode`, `ex.Error.ReadAsString()` |

---

## 3. Trap Notes

- ⚠ **Step 1 (client registration)** — The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. `Timeout` is per-attempt, not total; `MaxRetries = 0` is rejected at construction (floor is 1). Transport failures (`HttpRequestException`) retry on **every** verb including `POST`, so a non-idempotent write can execute more than once. **MUST load `dotnet-configuration-resilience`** before wiring the client.

- ⚠ **Step 1 (client registration)** — The SDK does **not** own the `HttpClient` — you provide it. Reuse one instance for the app's lifetime (or use `IHttpClientFactory`); do not create one per request. The SDK client wrapper may be transient. **MUST load `dotnet-client-initialization`** before wiring the client.

- ⚠ **Step 3 (list plans)** — `ListProducts` returns `IReadOnlyList<ProductResponse>` directly (not a wrapper). Each item must be unwrapped: `item.Product.Id`, `item.Product.Handle`. Pass all 8 nullable query params explicitly (as `null`) when calling positionally, or use named arguments. **MUST load `dotnet-calling-endpoints`** before the first call.

- ⚠ **Step 4 (subscribe)** — `CreateSubscriptionRequest` nests a required `CreateSubscription` under the `Subscription` property. `ProductHandle` and `CustomerReference` are nullable strings — use them to link by handle/reference rather than numeric IDs. The `Reference` field on `CreateSubscription` is the idempotency key. **MUST load `dotnet-models`** before building the request.

- ⚠ **Step 4 (subscribe)** — `CreateCustomer` requires `FirstName`, `LastName`, and `Email` (all `string !req`). If any are missing from the eShopOnWeb user profile, the call fails with 422. **MUST load `dotnet-error-handling`** to handle the typed `CustomerErrorResponse1` accessor.

- ⚠ **All steps** — `System.Text.Json.JsonException` reaches the boundary from two directions: (a) a drifted or malformed **2xx** body surfaces as `JsonException` from deserialization, **not** as `SdkException` — so an SDK-exception-only catch ladder lets it escape; (b) a **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, replacing the `SdkException` and destroying the HTTP status. **MUST load `dotnet-error-handling`** before writing the error boundary.

---

## 4. Idempotency Strategy

**Problem:** Double-click on POST /api/subscriptions could create duplicate customers and subscriptions.

**Solution — two-layer idempotency:**

1. **Customer idempotency via `reference`:** Before creating a customer, call `ReadCustomerByReference(userId.ToString())`. If found, reuse the `Customer.Id`. If not found, call `CreateCustomer` with `Reference = userId.ToString()`. The Maxio API enforces uniqueness on `reference` — a duplicate create returns 422, which we handle by re-reading.

2. **Subscription idempotency via `reference`:** The `CreateSubscription` request model has a `Reference (reference): string?` field. Set it to a deterministic value like `"{userId}-{productHandle}"`. Maxio uses this for deduplication on the subscription lookup endpoint (`FindSubscription`). If the same reference is submitted twice, the second call returns the existing subscription.

**Implementation sequence in the service:**
```
1. ReadCustomerByReference(userId) → if found, use existing customerId
2. If not found, CreateCustomer(reference: userId) → use new customerId
   - On 422 (duplicate reference), ReadCustomerByReference(userId) → use existing customerId
3. CreateSubscription(customerId: customerId, productHandle: handle, reference: "{userId}-{handle}")
   - On success, return subscription
   - On 422, FindSubscription(reference: "{userId}-{handle}") → return existing subscription
```

> **UNVERIFIED:** Whether Maxio actually enforces uniqueness on the `CreateSubscription.Reference` field — the `FindSubscription` endpoint exists and accepts a `reference` query param, suggesting it does, but live traffic confirmation is needed. The plan defends against this with a re-read fallback.

---

## 5. Customer-to-User Mapping

**Approach:** Use the eShopOnWeb user's Identity `Id` (string) as the Maxio customer `Reference`.

- **Mapping:** `MaxioCustomer.Reference = eShopOnWebUser.Id` (the ASP.NET Identity user ID)
- **Lookup:** `ReadCustomerByReference(userId)` returns the `CustomerResponse` with `Customer.Id` (the Maxio numeric ID)
- **Storage:** The Maxio `Customer.Id` is **not** stored locally — it is looked up fresh on each subscription operation via `ReadCustomerByReference`. This avoids sync issues and keeps the local database schema unchanged.
- **Alternative considered:** Storing `MaxioCustomerId` in a local table — rejected for simplicity; the reference lookup is O(1) and avoids schema migration.

> **YOUR CALL — not in the map:** The decision to look up fresh vs. cache the Maxio customer ID locally is an application architecture choice. The SDK map provides `ReadCustomerByReference` and `CreateCustomer` — the linking strategy is the implementer's.

---

## 6. Full Implementation Sequence

### Step 1: Add NuGet Package

**File:** `src/Infrastructure/Infrastructure.csproj`
```xml
<PackageReference Include="AsadAli.AdvancedBilling.Sdk" Version="1.0.2" />
```

### Step 2: Configuration Model + Binding

**New file:** `src/Infrastructure/Maxio/MaxioOptions.cs`
```csharp
namespace eShopOnWeb.Infrastructure.Maxio;

public class MaxioOptions
{
    public const string SectionName = "Maxio";
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
}
```

**Modify:** `src/Web/appsettings.json` (and `appsettings.Development.json`)
```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": ""
  }
}
```

### Step 3: Service Interface

**New file:** `src/ApplicationCore/Interfaces/ISubscriptionPlanService.cs`
```csharp
using eShopOnWeb.ApplicationCore.Entities;

namespace eShopOnWeb.ApplicationCore.Interfaces;

public interface ISubscriptionPlanService
{
    Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionResultDto> SubscribeUserAsync(string userId, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<UserSubscriptionDto>> GetUserSubscriptionsAsync(string userId, CancellationToken ct = default);
}
```

**New DTOs** in `src/ApplicationCore/Entities/` (or a new `MaxioDtos.cs` file):
```csharp
namespace eShopOnWeb.ApplicationCore.Entities;

public record SubscriptionPlanDto(
    int Id,
    string Name,
    string Handle,
    string Description,
    decimal Price,
    string IntervalUnit,
    int Interval);

public record SubscriptionResultDto(
    int SubscriptionId,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAssessmentAt,
    string ProductName);

public record UserSubscriptionDto(
    int SubscriptionId,
    string State,
    string ProductName,
    decimal Price,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CanceledAt);
```

### Step 4: Service Implementation

**New file:** `src/Infrastructure/Maxio/MaxioSubscriptionPlanService.cs`

Key implementation notes:
- Constructor takes `MaxioAdvancedBillingClient` (injected) + `MaxioOptions` (injected)
- `GetPlansAsync`: calls `client.Products.ListProducts(dateField: null, filter: null, ...)` with all nullable params as `null`, extracts `Product` from each `ProductResponse`
- `SubscribeUserAsync`: follows the idempotency sequence (Section 4 above)
- `GetUserSubscriptionsAsync`: calls `ReadCustomerByReference(userId)` then `ListCustomerSubscriptions(customerId)`
- Error handling: wrap every SDK call in `try/catch(SdkException<T>)` per the accessors in Section 2.5

### Step 5: DI Registration

**Modify:** `src/Infrastructure/DependencyInjection.cs`
```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using eShopOnWeb.Infrastructure.Maxio;

// Inside AddInfrastructure method:
builder.Services.Configure<MaxioOptions>(
    builder.Configuration.GetSection(MaxioOptions.SectionName));

builder.Services.AddMaxioAdvancedBillingClient(options =>
{
    var maxioConfig = builder.Configuration
        .GetSection(MaxioOptions.SectionName)
        .Get<MaxioOptions>() ?? new MaxioOptions();

    options.BasicAuth = new BasicAuthCredentials
    {
        Username = maxioConfig.ApiKey,
        Password = "x"
    };
    options.Environment = ServerEnvironment.Us;

    if (!string.IsNullOrEmpty(maxioConfig.Subdomain))
    {
        options.Server.Production.Us.Site = maxioConfig.Subdomain;
    }
});

builder.Services.AddScoped<ISubscriptionPlanService, MaxioSubscriptionPlanService>();
```

### Step 6: API Endpoints

**New file:** `src/PublicApi/SubscriptionPlans/GetSubscriptionPlans.cs`

Implements `IEndpoint` pattern:
- Route: `GET /api/subscription-plans`
- Requires authentication (`[Authorize]`)
- Calls `ISubscriptionPlanService.GetPlansAsync()`
- Returns `Ok(plans)`

**New file:** `src/PublicApi/Subscriptions/PostSubscription.cs`

Implements `IEndpoint`:
- Route: `POST /api/subscriptions`
- Requires authentication
- Accepts `{ productHandle: "..." }` in body
- Gets current user ID from `User.FindFirstValue(ClaimTypes.NameIdentifier)`
- Calls `ISubscriptionPlanService.SubscribeUserAsync(userId, productHandle)`
- Returns `Ok(result)` or `Conflict(...)` on duplicate

**New file:** `src/PublicApi/Subscriptions/GetMySubscriptions.cs`

Implements `IEndpoint`:
- Route: `GET /api/my-subscriptions`
- Requires authentication
- Gets current user ID from claims
- Calls `ISubscriptionPlanService.GetUserSubscriptionsAsync(userId)`
- Returns `Ok(subscriptions)`

### Step 7: Register Endpoints

**Modify:** `src/PublicApi/Program.cs` (or the endpoint registration file)

Add the three new endpoints to the MinimalApi endpoint registration.

---

## 7. Assumptions & Blockers

| # | Type | Statement |
|---|------|-----------|
| 1 | Assumption | The eShopOnWeb user's Identity `Id` (string) is stable and unique — used as the Maxio customer `Reference`. |
| 2 | Assumption | Maxio sandbox site `cp-exp-8` has the seeded entities (product family `eshop-subscribe`, plans `eshop-pro` and `basic-plan`) and the API key has access to them. |
| 3 | Assumption | The `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, and `MAXIO_DEFAULT_PRODUCT_FAMILY` environment variables are set at deployment time and bound to the `Maxio:` config section. |
| 4 | Assumption | Both plans have `payment_method_not_required = true` (no credit card needed) — the sandbox entities are described this way. If false, `CreateSubscription` will fail with 422. |
| 5 | Assumption | The `CreateSubscription.Reference` field is enforced as unique by Maxio — used for subscription idempotency. **UNVERIFIED** — plan includes a re-read fallback. |
| 6 | Blocker | If `CreateSubscription` requires a payment profile (despite the "payment method not required" claim), the subscription creation will fail. This must be verified against the live sandbox before implementation is considered complete. |
| 7 | Assumption | The `ListProducts` endpoint returns only non-archived products by default. If archived products appear, filter client-side by checking `Product.ArchivedAt` is null. |

---

## 8. REQUIRED READING

These `dotnet-*` companion skills must be loaded **before implementation starts**. The sheet deliberately does not carry their contents — each carries usage traps a one-line note cannot convey.

| Skill | Governs Step(s) |
|-------|-----------------|
| `dotnet-client-initialization` | Step 1 (client registration, HttpClient lifetime, DI wiring) |
| `dotnet-authentication` | Step 1 (Basic auth credential setup) |
| `dotnet-configuration-resilience` | Step 1 (retry/timeout tuning, per-attempt vs total bounds) |
| `dotnet-calling-endpoints` | Steps 3–5 (calling SDK operations, named arguments, response unwrapping) |
| `dotnet-models` | Steps 3–5 (building request records, enum usage, collection handling) |
| `dotnet-error-handling` | Steps 3–5 (catch ladder for both Case A and Case B operations, `JsonException` dual-path) |

**Hazards requiring these skills:**

- `dotnet-error-handling`: **Both** of these `System.Text.Json.JsonException` paths must be handled:
  - A drifted or malformed **2xx** body (missing `required` member) surfaces as `JsonException` from deserialization, **not** as `SdkException` — so an SDK-exception-only catch ladder lets it escape the boundary;
  - A **non-2xx** body that does not match its operation's generated error shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a xx then reports a deterministic rejection as an outage.

- `dotnet-configuration-resilience`: **Transport failures** (`HttpRequestException`) retry on **every** verb including `POST`, so the `CreateSubscription` call may execute more than once — the idempotency strategy (Section 4) addresses this, but the retry behavior must be understood before implementing.

- `dotnet-client-initialization`: The SDK's `AddMaxioAdvancedBillingClient` extension registers a client — check whether it registers as singleton or transient, as this affects `HttpClient` handler rotation.
