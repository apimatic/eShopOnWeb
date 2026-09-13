# Maxio Advanced Billing Integration Plan — eShopOnWeb

## 1. Scope & Sequence

| Step | Description | Operations Used |
|------|-------------|-----------------|
| 1 | Add `AsadAli.AdvancedBilling.Sdk` NuGet package to `src/Infrastructure` | — |
| 2 | Add Maxio configuration section to `appsettings.json` and bind in `Program.cs` | — |
| 3 | Register `MaxioAdvancedBillingClient` via DI in `src/Infrastructure/Dependencies.cs` | — |
| 4 | Create `ISubscriptionService` interface in `src/ApplicationCore` | — |
| 5 | Implement `MaxioSubscriptionService` in `src/Infrastructure` | `Products.ListProducts`, `Customers.ReadCustomerByReference`, `Customers.CreateCustomer`, `Customers.ListCustomerSubscriptions`, `Subscriptions.CreateSubscription` |
| 6 | Add `SubscriptionPlanViewModel`, `CreateSubscriptionRequest`, `SubscriptionViewModel` to `src/PublicApi/Models` | — |
| 7 | Add `SubscriptionEndpoint` class to `src/PublicApi/Api/Endpoints` (Pattern B) | — |
| 8 | Register `MaxioSubscriptionService` as `ISubscriptionService` in DI | — |

---

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### Operations

#### Products — `client.Products` (source: `map/operations/Products.md`)

| Method | Signature | Returns | Error Case | Pagination |
|--------|-----------|---------|------------|------------|
| `ListProducts` | `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<ProductResponse>` | Case B: `SdkException<RawError>` | manual `page`+`perPage` |

**ListProducts notes**: 8 nullable params (`dateField`…`include`) must be passed explicitly — pass `null` to skip.

#### Customers — `client.Customers` (source: `map/operations/Customers.md`)

| Method | Signature | Returns | Error Case |
|--------|-----------|---------|------------|
| `ReadCustomerByReference` | `ReadCustomerByReference(string reference, CancellationToken ct = default)` | `CustomerResponse` | Case B: `SdkException<RawError>` |
| `CreateCustomer` | `CreateCustomer(CreateCustomerRequest? body, CancellationToken ct = default)` | `CustomerResponse` | Case A: `SdkException<CreateCustomerError>` — accessors: `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` [422], `TryGetRawError(out RawError)` [fallback] |
| `ListCustomerSubscriptions` | `ListCustomerSubscriptions(int customerId, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` | Case B: `SdkException<RawError>` |

#### Subscriptions — `client.Subscriptions` (source: `map/operations/Subscriptions.md`)

| Method | Signature | Returns | Error Case |
|--------|-----------|---------|------------|
| `CreateSubscription` | `CreateSubscription(CreateSubscriptionRequest? body, CancellationToken ct = default)` | `SubscriptionResponse` | Case A: `SdkException<CreateSubscriptionError>` — accessors: `TryGetErrorListResponse1(out ErrorListResponse1)` [422], `TryGetRawError(out RawError)` [fallback] |
| `ListSubscriptions` | `ListSubscriptions(SubscriptionStateFilter? state, int? product, int? productPricePointId, int? coupon, string? couponCode, SubscriptionDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, IReadOnlyDictionary<string, string>? metadata, SortingDirection? direction, SubscriptionSort? sort, IReadOnlyList<SubscriptionListInclude>? include, int? page = 1, int? perPage = 20, CancellationToken ct = default)` | `IReadOnlyList<SubscriptionResponse>` | Case B: `SdkException<RawError>` |

### Request Models

#### `CreateCustomerRequest` (source: `map/models/records-1-Ac-Cr.md`)

```csharp
// Namespace: MaxioAdvancedBilling.Models
public record CreateCustomerRequest {
    public required CreateCustomer Customer { get; init; }
}
```

#### `CreateCustomer` (source: `map/models/records-1-Ac-Cr.md`)

| Field | Wire Name | Type | Required? |
|-------|-----------|------|-----------|
| `FirstName` | `first_name` | `string` | **!req** |
| `LastName` | `last_name` | `string` | **!req** |
| `Email` | `email` | `string` | **!req** |
| `Reference` | `reference` | `string?` | optional |
| `Organization` | `organization` | `string?` | optional |
| `Address` | `address` | `string?` | optional |
| `City` | `city` | `string?` | optional |
| `State` | `state` | `string?` | optional |
| `Zip` | `zip` | `string?` | optional |
| `Country` | `country` | `string?` | optional |
| `Phone` | `phone` | `string?` | optional |
| `Locale` | `locale` | `string?` | optional |
| `VatNumber` | `vat_number` | `string?` | optional |
| `TaxExempt` | `tax_exempt` | `bool?` | optional |

#### `CreateSubscriptionRequest` (source: `map/models/records-2-Cr-Ne.md`)

```csharp
// Namespace: MaxioAdvancedBilling.Models
public record CreateSubscriptionRequest {
    public required CreateSubscription Subscription { get; init; }
}
```

#### `CreateSubscription` (source: `map/models/records-2-Cr-Ne.md`)

| Field | Wire Name | Type | Required? |
|-------|-----------|------|-----------|
| `ProductHandle` | `product_handle` | `string?` | optional |
| `ProductId` | `product_id` | `int?` | optional |
| `ProductPricePointHandle` | `product_price_point_handle` | `string?` | optional |
| `ProductPricePointId` | `product_price_point_id` | `int?` | optional |
| `CustomerId` | `customer_id` | `int?` | optional |
| `CustomerReference` | `customer_reference` | `string?` | optional |
| `Reference` | `reference` | `string?` | optional |
| `PaymentCollectionMethod` | `payment_collection_method` | `CollectionMethod?` | optional |
| `ReceivesInvoiceEmails` | `receives_invoice_emails` | `string?` | optional |
| `NetTerms` | `net_terms` | `string?` | optional |
| `NextBillingAt` | `next_billing_at` | `DateTimeOffset?` | optional |
| `InitialBillingAt` | `initial_billing_at` | `DateTimeOffset?` | optional |
| `DeferSignup` | `defer_signup` | `bool?` | optional, default `false` |
| `PaymentProfileId` | `payment_profile_id` | `int?` | optional |
| `CouponCode` | `coupon_code` | `string?` | optional |
| `CouponCodes` | `coupon_codes` | `IReadOnlyList<string>?` | optional |
| `Components` | `components` | `IReadOnlyList<CreateSubscriptionComponent>?` | optional |
| `CalendarBilling` | `calendar_billing` | `CalendarBilling?` | optional |
| `Metafields` | `metafields` | `IReadOnlyDictionary<string, string>?` | optional |
| `Currency` | `currency` | `string?` | optional |
| `ExpiresAt` | `expires_at` | `DateTimeOffset?` | optional |
| `CustomerAttributes` | `customer_attributes` | `CustomerAttributes?` | optional |
| `PaymentProfileAttributes` | `payment_profile_attributes` | `PaymentProfileAttributes?` | optional |

### Response Envelope

#### `ProductResponse` (source: `map/models/records-3-Of-Su.md`)

```csharp
public record ProductResponse {
    public required Product Product { get; init; }
}
```

Key `Product` fields: `Id (id): int?`, `Name (name): string?`, `Handle (handle): string?`, `Description (description): string?`, `PriceInCents (price_in_cents): long?`, `Interval (interval): int?`, `IntervalUnit (interval_unit): IntervalUnit?`, `TrialPriceInCents (trial_price_in_cents): long?`, `RequireCreditCard (require_credit_card): bool?`, `ProductFamilyId (product_family_id): int?`, `ProductFamily (product_family): ProductFamily?`.

#### `CustomerResponse` (source: `map/models/records-2-Cr-Ne.md`)

```csharp
public record CustomerResponse {
    public required Customer Customer { get; init; }
}
```

Key `Customer` fields: `Id (id): int?`, `FirstName (first_name): string?`, `LastName (last_name): string?`, `Email (email): string?`, `Reference (reference): string?`, `Organization (organization): string?`.

#### `SubscriptionResponse` (source: `map/models/records-4-Su-We.md`)

```csharp
public record SubscriptionResponse {
    public Subscription? Subscription { get; init; }
}
```

Key `Subscription` fields: `Id (id): int?`, `State (state): SubscriptionState?`, `CurrentPeriodEndsAt (current_period_ends_at): DateTimeOffset?`, `NextAssessmentAt (next_assessment_at): DateTimeOffset?`, `ActivatedAt (activated_at): DateTimeOffset?`, `CreatedAt (created_at): DateTimeOffastAt (created_at): DateTimeOffset?`, `ProductId (product_id): int?`, `ProductName (product_name): string?`, `CustomerId (customer_id): int?`, `ProductPriceInCents (product_price_in_cents): long?`, `BalanceInCents (balance_in_cents): long?`, `TotalRevenueInCents (total_revenue_in_cents): long?`, `PaymentCollectionMethod (payment_collection_method): CollectionMethod?`.

### Enum Values Needed

#### `CollectionMethod` (source: `map/models/enums.md`)

Members: `Automatic (automatic)`, `Remittance (remittance)`, `Prepaid (prepaid)`, `Invoice (invoice)`

#### `SubscriptionState` (source: `map/models/enums.md`)

Members: `Pending (pending)`, `FailedToCreate (failed_to_create)`, `Trialing (trialing)`, `Assessing (assessing)`, `Active (active)`, `SoftFailure (soft_failure)`, `PastDue (past_due)`, `Suspended (suspended)`, `Canceled (canceled)`, `Expired (expired)`, `Paused (paused)`, `Unpaid (unpaid)`, `TrialEnded (trial_ended)`, `OnHold (on_hold)`, `AwaitingSignup (awaiting_signup)`

#### `IntervalUnit` (source: `map/models/enums.md`)

Members: `Day (day)`, `Month (month)`

### Client Construction

```csharp
// Namespace: MaxioAdvancedBilling
var options = new MaxioAdvancedBillingClientOptions {
    BasicAuth = new BasicAuthCredentials { Username = "<api_key>", Password = "x" },
    Environment = ServerEnvironment.Us,
    // Override base URL if needed:
    // Server = new ServerOptions { Production = new ProductionOptions { Us = new ProductionUrlOptions { BaseUrl = "https://custom-host.com" } } }
};
var client = new MaxioAdvancedBillingClient(httpClient, options);
```

### Error Handling Pattern

All operations are **throw-based** (no `…Result` variants). Two error cases:

**Case A (typed errors):** `CreateCustomer`, `CreateSubscription` throw `SdkException<{Op}Error>`.
- `SdkException<CreateCustomerError>`: catch, call `TryGetCustomerErrorResponse1(out CustomerErrorResponse1)` for 422, or `TryGetRawError(out RawError)` for other statuses.
- `SdkException<CreateSubscriptionError>`: catch, call `TryGetErrorListResponse1(out ErrorListResponse1)` for 422, or `TryGetRawError(out RawError)` for other statuses.

**Case B (raw errors):** `ListProducts`, `ReadCustomerByReference`, `ListCustomerSubscriptions`, `ListSubscriptions` throw `SdkException<RawError>`.
- Access: `ex.Error.StatusCode`, `ex.Error.ReadAsString()`, `ex.Error.ReadAsJson<T>()`.

---

## 3. Implementation Steps

### Step 1 — Add NuGet Package

Add `AsadAli.AdvancedBilling.Sdk` to `src/Infrastructure/Infrastructure.csproj`:

```xml
<PackageReference Include="AsadAli.AdvancedBilling.Sdk" Version="1.0.2" />
```

### Step 2 — Configuration

Add to `appsettings.json` (PublicApi project) under a `Maxio` section:

```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "",
    "ProductFamilyHandle": "",
    "BaseUrl": ""
  }
}
```

Bind in `Program.cs` (or wherever configuration is wired):

```csharp
builder.Services.Configure<MaxioSettings>(builder.Configuration.GetSection("Maxio"));
```

Create `src/ApplicationCore/Configuration/MaxioSettings.cs`:

```csharp
public class MaxioSettings {
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string? BaseUrl { get; set; }
}
```

### Step 3 — DI Registration

In `src/Infrastructure/Dependencies.cs`, register the `MaxioAdvancedBillingClient`:

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;
using Microsoft.Extensions.Options;

services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));

services.AddSingleton(sp => {
    var settings = sp.GetRequiredService<IOptions<MaxioSettings>>().Value;
    var httpClient = new HttpClient(); // or use IHttpClientFactory
    var options = new MaxioAdvancedBillingClientOptions {
        BasicAuth = new BasicAuthCredentials { Username = settings.ApiKey, Password = "x" },
        Environment = ServerEnvironment.Us,
    };
    if (!string.IsNullOrEmpty(settings.BaseUrl)) {
        options.Server = new ServerOptions {
            Production = new ProductionOptions {
                Us = new ProductionUrlOptions { BaseUrl = settings.BaseUrl }
            }
        };
    }
    return new MaxioAdvancedBillingClient(httpClient, options);
});

services.AddScoped<ISubscriptionService, MaxioSubscriptionService>();
```

⚠ **Trap note**: The SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register. **MUST load `dotnet-configuration-resilience`** before wiring the client.

### Step 4 — ApplicationCore Interface

Create `src/ApplicationCore/Services/ISubscriptionService.cs`:

```csharp
public interface ISubscriptionService {
    Task<IReadOnlyList<PlanDto>> GetPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> CreateSubscriptionAsync(string userId, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default);
    Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default);
}
```

Create `src/ApplicationCore/Models/PlanDto.cs`:

```csharp
public record PlanDto {
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Handle { get; init; } = "";
    public string Description { get; init; } = "";
    public decimal PriceInDollars { get; init; }
    public string IntervalUnit { get; init; } = "";
    public int Interval { get; init; }
}
```

Create `src/ApplicationCore/Models/SubscriptionDto.cs`:

```csharp
public record SubscriptionDto {
    public int Id { get; init; }
    public string State { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string ProductHandle { get; init; } = "";
    public decimal PriceInDollars { get; init; }
    public DateTimeOffset? NextBillingDate { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
```

### Step 5 — MaxioSubscriptionService Implementation

Create `src/Infrastructure/Services/MaxioSubscriptionService.cs`:

```csharp
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;

public class MaxioSubscriptionService : ISubscriptionService {
    private readonly MaxioAdvancedBillingClient _client;
    private readonly MaxioSettings _settings;

    public MaxioSubscriptionService(MaxioAdvancedBillingClient client, IOptions<MaxioSettings> settings) {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<IReadOnlyList<PlanDto>> GetPlansAsync(CancellationToken ct = default) {
        var response = await _client.Products.ListProducts(
            dateField: null,
            filter: null,
            endDate: null,
            endDatetime: null,
            startDate: null,
            startDatetime: null,
            includeArchived: null,
            include: null,
            page: 1,
            perPage: 50,
            ct: ct);

        return response.Select(pr => new PlanDto {
            Id = pr.Product!.Id ?? 0,
            Name = pr.Product.Name ?? "",
            Handle = pr.Product.Handle ?? "",
            Description = pr.Product.Description ?? "",
            PriceInDollars = (pr.Product.PriceInCents ?? 0) / 100.0m,
            IntervalUnit = pr.Product.IntervalUnit?.Value ?? "",
            Interval = pr.Product.Interval ?? 0
        }).ToList();
    }

    public async Task<SubscriptionDto> CreateSubscriptionAsync(
        string userId, string email, string firstName, string lastName, string productHandle, CancellationToken ct = default) {

        // 1. Idempotent customer lookup — use reference = userId
        int customerId;
        try {
            var custResp = await _client.Customers.ReadCustomerByReference(userId, ct);
            customerId = custResp.Customer!.Id!.Value;
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound) {
            // Customer doesn't exist — create
            var newCust = await _client.Customers.CreateCustomer(
                new CreateCustomerRequest {
                    Customer = new CreateCustomer {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        Reference = userId
                    }
                }, ct);
            customerId = newCust.Customer!.Id!.Value;
        }

        // 2. Create subscription
        var subResp = await _client.Subscriptions.CreateSubscription(
            new CreateSubscriptionRequest {
                Subscription = new CreateSubscription {
                    ProductHandle = productHandle,
                    CustomerId = customerId
                }
            }, ct);

        var s = subResp.Subscription;
        return new SubscriptionDto {
            Id = s?.Id ?? 0,
            State = s?.State?.Value ?? "",
            ProductName = s?.Product?.Name ?? "",
            ProductHandle = productHandle,
            PriceInDollars = (s?.ProductPriceInCents ?? 0) / 100.0m,
            NextBillingDate = s?.NextAssessmentAt,
            ActivatedAt = s?.ActivatedAt,
            CreatedAt = s?.CreatedAt ?? DateTimeOffset.UtcNow
        };
    }

    public async Task<IReadOnlyList<SubscriptionDto>> GetMySubscriptionsAsync(string userReference, CancellationToken ct = default) {
        // Find customer by reference
        var custResp = await _client.Customers.ReadCustomerByReference(userReference, ct);
        var customerId = custResp.Customer!.Id!.Value;

        // List subscriptions for this customer
        var subs = await _client.Customers.ListCustomerSubscriptions(customerId, ct);

        return subs.Select(sr => new SubscriptionDto {
            Id = sr.Subscription?.Id ?? 0,
            State = sr.Subscription?.State?.Value ?? "",
            ProductName = sr.Subscription?.Product?.Name ?? "",
            ProductHandle = sr.Subscription?.Product?.Handle ?? "",
            PriceInDollars = (sr.Subscription?.ProductPriceInCents ?? 0) / 100.0m,
            NextBillingDate = sr.Subscription?.NextAssessmentAt,
            ActivatedAt = sr.Subscription?.ActivatedAt,
            CreatedAt = sr.Subscription?.CreatedAt ?? DateTimeOffset.UtcNow
        }).ToList();
    }
}
```

⚠ **Trap notes**:
- All SDK operations are **throw-only** — no `…Result` variant exists. Every call must be in a try/catch. **MUST load `dotnet-error-handling`** before writing the error boundary.
- `SdkException<RawError>` (Case B) on `ReadCustomerByReference` returns 404 when not found — the status code must be checked, not the body.
- `SdkException<CreateCustomerError>` (Case A) on `CreateCustomer` — if the customer already exists with the same reference, Maxio returns 422. The idempotent path above catches this via `ReadCustomerByReference` first; a race between two requests could still trigger 422, so the create call should also be wrapped and the error checked via `TryGetCustomerErrorResponse1`. **MUST load `dotnet-error-handling`**.

### Step 6 — PublicApi Request/Response Models

Create `src/PublicApi/Models/CreateSubscriptionRequest.cs`:

```csharp
public class CreateSubscriptionRequest {
    public string ProductHandle { get; set; } = "";
}
```

Create `src/PublicApi/Models/SubscriptionPlanResponse.cs`:

```csharp
public class SubscriptionPlanResponse {
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Handle { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string IntervalUnit { get; set; } = "";
    public int Interval { get; set; }
}
```

Create `src/PublicApi/Models/SubscriptionResponse.cs`:

```csharp
public class SubscriptionResponseModel {
    public int Id { get; set; }
    public string State { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

### Step 7 — Endpoint (Pattern B — MinimalApi.Endpoint)

Create `src/PublicApi/Api/Endpoints/SubscriptionEndpoint.cs`:

Follow the `MinimalApi.Endpoint` pattern used by `CatalogItemEndpoints`. The endpoint class implements `IEndpoint` and has an `AddRoute` method.

Three routes on one endpoint class:
- `GET /api/subscription-plans` — calls `ISubscriptionService.GetPlansAsync()`
- `POST /api/subscriptions` — calls `ISubscriptionService.CreateSubscriptionAsync()` with user info from JWT claims
- `GET /api/my-subscriptions` — calls `ISubscriptionService.GetMySubscriptionsAsync()` with user reference from JWT claims

⚠ **Trap note**: JWT auth uses `[Authorize]` attribute. The user's identity (sub, email, name) comes from `User.FindFirstValue(ClaimTypes.NameIdentifier)` etc. **MUST load `dotnet-calling-endpoints`** before wiring the call.

### Step 8 — Register Endpoint

In the PublicApi `Program.cs` or wherever endpoints are registered, ensure `SubscriptionEndpoint` is added.

---

## 4. Architecture Decisions

| Layer | What Goes Here | Why |
|-------|---------------|-----|
| **ApplicationCore** | `ISubscriptionService`, `PlanDto`, `SubscriptionDto` | Domain abstraction — no SDK dependency leaks here |
| **Infrastructure** | `MaxioSubscriptionService`, `MaxioSettings`, SDK client registration | SDK implementation detail hidden behind interface |
| **PublicApi** | Request/response models, `SubscriptionEndpoint` | HTTP layer — thin, delegates to `ISubscriptionService` |

### DI Registration Order

1. `MaxioSettings` configuration binding (in `Dependencies.cs` or `Program.cs`)
2. `MaxioAdvancedBillingClient` singleton registration
3. `ISubscriptionService` → `MaxioSubscriptionService` as scoped

---

## 5. Assumptions & Blockers

| # | Item | Status |
|---|------|--------|
| 1 | The app uses `dotnet-user-id` as the Maxio customer `reference` field | Assumption — if the app uses a different unique identifier, the `reference` field in `CreateCustomer` must be updated accordingly |
| 2 | Maxio sandbox site `cp-exp-5` allows creating customers without payment profiles | Assumption — the requirement states "payment method not required" for both plans |
| 3 | No existing Maxio customers with duplicate references for the same eShopOnWeb user | Assumption — the idempotent lookup-then-create pattern handles this |
| 4 | Product IDs (7126957, 7126958) match the sandbox on `cp-exp-5` | Assumption — use product handles (`eshop-pro`, `basic-plan`) instead of IDs for portability |
| 5 | JWT claims include `ClaimTypes.NameIdentifier` for user ID and standard name/email claims | Assumption — if the JWT uses different claim types, the endpoint must be adjusted |
| 6 | `IConfiguration` pattern already exists in the PublicApi project for settings binding | Assumption — matches the requirement that settings are bound from `Maxio:` config section |

**No blocking items identified.** The plan is implementable as-is given the seeded sandbox entities.
