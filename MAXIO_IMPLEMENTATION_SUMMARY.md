# Maxio Subscription Billing Integration — Implementation Summary

## Overview

The eShopOnWeb reference application has been extended with recurring subscription billing powered by Maxio Advanced Billing. This integration is **additive and parallel** to the existing one-time cart/checkout flow — subscriptions are a separate capability accessed via new endpoints.

## Implementation Status

✅ **Complete** — All components build successfully and are ready for testing.

### Build Status
- **Solution**: eShopOnWeb.sln
- **Project**: src/PublicApi/PublicApi.csproj
- **Build Result**: ✅ Succeeded (0 errors, 4 warnings about System.Text.Json CVE)

### Files Added/Modified

**New Files (Service Layer & Endpoints)**
- `src/PublicApi/MaxioSettings.cs` — Configuration model
- `src/PublicApi/Services/MaxioSubscriptionService.cs` — Maxio SDK wrapper service
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` — GET /api/subscription-plans
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` — POST /api/subscriptions
- `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs` — GET /api/my-subscriptions

**Modified Files**
- `Directory.Packages.props` — Added AsadAli.AdvancedBilling.Sdk v1.0.2
- `src/PublicApi/PublicApi.csproj` — Added Maxio SDK package reference
- `src/PublicApi/appsettings.json` — Added Maxio configuration section
- `src/PublicApi/Program.cs` — Added Maxio client registration and DI setup
- `src/Infrastructure/Identity/ApplicationUser.cs` — Added FirstName, LastName, MaxioCustomerId properties

**Database Migration**
- `src/Infrastructure/Identity/Migrations/20260906221636_AddMaxioCustomerAndUserFields.cs` — Adds three nullable columns to AspNetUsers table

## Architecture

### Layers

```
API Endpoints (SubscriptionEndpoints/)
        ↓
MaxioSubscriptionService (IMaxioSubscriptionService)
        ↓
MaxioAdvancedBillingClient (Maxio SDK)
        ↓
Maxio Sandbox API
```

### Dependency Injection (Program.cs)

1. Load Maxio credentials from environment variables: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`
2. Bind to configuration section `Maxio:*` with defaults from env vars
3. Register Maxio client as a singleton with:
   - HttpClient via IHttpClientFactory (reusable, pooled connections)
   - Basic auth (username = API key, password = literal "x")
   - Sandbox environment (ServerEnvironment.Us or ServerEnvironment.Eu)
   - Optional BaseUrl override for custom endpoints
4. Register MaxioSubscriptionService as scoped (per-request lifetime)

### API Endpoints

All endpoints require JWT bearer authentication. No payment method is required for sandbox subscriptions.

#### GET /api/subscription-plans
Lists available subscription plans from Maxio (filtered by product family handle).

**Response (200 OK)**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

#### POST /api/subscriptions
Subscribes the authenticated user to a plan. Idempotently creates a Maxio customer if one does not exist (keyed by user ID).

**Request**
```json
{
  "productHandle": "eshop-pro"
}
```

**Response (200 OK)**
```json
{
  "subscriptionId": 123456,
  "customerId": 789,
  "state": "active",
  "productName": "Pro Plan",
  "productHandle": "eshop-pro",
  "priceInCents": 29900,
  "nextBillingDate": "2025-10-07T00:00:00+00:00"
}
```

**Response (422 Unprocessable Entity)** — Validation error (e.g., duplicate subscription on same plan)
```json
{
  "error": "Validation failed"
}
```

#### GET /api/my-subscriptions
Lists all subscriptions for the authenticated user.

**Response (200 OK)**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 123456,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "priceInCents": 29900,
      "nextBillingDate": "2025-10-07T00:00:00+00:00",
      "createdAt": "2025-09-07T12:34:56+00:00"
    }
  ]
}
```

### Error Handling

The integration uses structured exception handling per the Maxio SDK contract:

- **SdkException\<RawError\>** (Case B) — For operations without a typed error model (ListProducts, ReadProductByHandle, ListCustomerSubscriptions)
  - Access status: `ex.Error.StatusCode`
  - Read body: `ex.Error.ReadAsString()` or `ex.Error.ReadAsJson<T>()`
  
- **SdkException\<TError\>** (Case A) — For operations with typed error models (CreateCustomer, CreateSubscription)
  - Use `TryGet*` accessors for per-status error shapes
  - Fall back to `TryGetRawError(out RawError)` for untyped statuses

- **JsonException** — Unreadable response body (malformed JSON on 2xx or mismatch with error model on non-2xx)
  - Treated as API error; user receives HTTP 400 with safe error message
  - Never surfaces serialization details to the client

### Idempotency

Customer creation is idempotent: the eShopOnWeb user ID is passed as the Maxio `Reference` field. On repeated calls:
1. `ReadCustomerByReference(userId)` returns the existing customer
2. Repeated subscriptions to the same plan either succeed (Maxio deduplicates) or fail with 422 (if Maxio enforces uniqueness)

The customer ID is stored in the user's `MaxioCustomerId` field for future operations.

### Configuration

**Configuration Keys** (from `appsettings.json`, overridden by environment variables)
```json
{
  "Maxio": {
    "ApiKey": "",                      // from MAXIO_API_KEY
    "Subdomain": "",                   // from MAXIO_SITE_SUBDOMAIN
    "Environment": "US",               // from MAXIO_ENVIRONMENT (US or EU)
    "ProductFamilyHandle": "",         // from MAXIO_DEFAULT_PRODUCT_FAMILY
    "BaseUrl": null                    // optional override for custom API host
  }
}
```

No secrets are stored in the repository. Credentials come from environment variables (set at deployment time or in a secrets manager).

## Data Model

### ApplicationUser (Addition)

```csharp
public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }         // Used when creating Maxio customer
    public string? LastName { get; set; }          // Used when creating Maxio customer
    public int? MaxioCustomerId { get; set; }      // Maxio's internal customer ID
}
```

### MaxioSettings

```csharp
public class MaxioSettings
{
    public string ApiKey { get; set; }
    public string Subdomain { get; set; }
    public string Environment { get; set; }
    public string ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }
}
```

### MaxioSubscriptionService (DTOs)

```csharp
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Handle { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; }              // e.g., "active", "past_due"
    public long ProductPriceInCents { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string ProductName { get; set; }
    public string ProductHandle { get; set; }
}
```

## Maxio SDK Integration

### Operations Used

1. **ListProducts** — Fetch all products (paginated, queried by date/filter)
   - Query params: dateField, filter, endDate, startDate, page, perPage
   - Returns: `IReadOnlyList<ProductResponse>`
   - Error: `SdkException<RawError>` (Case B)

2. **ReadProductByHandle** — Fetch a specific product by handle
   - Path param: productHandle
   - Returns: `ProductResponse`
   - Error: `SdkException<RawError>` (Case B)

3. **CreateCustomer** — Create a customer (idempotent by reference)
   - Body: `CreateCustomerRequest` with firstName, lastName, email, reference
   - Returns: `CustomerResponse` with Id
   - Error: `SdkException<CreateCustomerError>` (Case A)

4. **ReadCustomerByReference** — Fetch customer by reference key
   - Query param: reference (the user ID)
   - Returns: `CustomerResponse` if found
   - Error: `SdkException<RawError>` (404 if not found)

5. **CreateSubscription** — Create a subscription
   - Body: `CreateSubscriptionRequest` with customerId, productHandle, paymentCollectionMethod
   - Returns: `SubscriptionResponse` with subscription details
   - Error: `SdkException<CreateSubscriptionError>` (Case A)

6. **ListCustomerSubscriptions** — Fetch all subscriptions for a customer
   - Path param: customerId
   - Returns: `IReadOnlyList<SubscriptionResponse>`
   - Error: `SdkException<RawError>` (Case B)

7. **ReadSubscription** — Fetch a specific subscription
   - Path param: subscriptionId
   - Returns: `SubscriptionResponse`
   - Error: `SdkException<RawError>` (Case B)

### Authentication

```csharp
options.BasicAuth = new BasicAuthCredentials
{
    Username = apiKey,       // MAXIO_API_KEY
    Password = "x"           // literal string "x"
};
```

### Environment Selection

```csharp
options.Environment = serverEnvironment switch
{
    "EU" => ServerEnvironment.Eu,      // https://{subdomain}.ebilling.maxio.com
    _ => ServerEnvironment.Us           // https://{subdomain}.chargify.com
};
```

## Testing

See `MAXIO_INTEGRATION_VERIFICATION.md` for step-by-step manual testing instructions.

### Quick Test

```powershell
# Start the app
cd src/PublicApi
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --configuration Release

# In another terminal, authenticate and list plans
$token = "..."  # JWT token from authenticate endpoint
$headers = @{Authorization = "Bearer $token"}
Invoke-WebRequest https://localhost:27863/api/subscription-plans -Headers $headers -SkipCertificateCheck
```

## Compliance & Production Readiness

### ✅ Implemented

- **JWT Authentication** — All endpoints require bearer token
- **Error Boundary** — SDK exceptions caught; safe error messages returned (no serialization details leaked)
- **Idempotency** — Customer creation keyed by user ID (no duplicates)
- **Dependency Injection** — Maxio client registered as singleton; service as scoped
- **Configuration Management** — Credentials from environment variables, not hardcoded
- **Logging** — Errors logged via ILogger; debug information available
- **Database Schema** — Migration adds required columns; schema valid for both in-memory and SQL databases

### ⚠️ Recommended for Production

- **Monitoring** — Add APM (Application Insights, DataDog) to track Maxio API performance
- **Rate Limiting** — Implement per-user or per-IP rate limits on subscription endpoints
- **Audit Trail** — Log subscription events (creation, state changes, cancellations)
- **Timeout Configuration** — Set explicit per-attempt and per-call timeouts (see `dotnet-configuration-resilience` skill)
- **Retry Policy** — Customize retry counts/backoff if needed (current: 3 retries, 1s + exponential backoff)
- **Payment Verification** — Verify payment method is attached before allowing invoice-based plans
- **Webhook Handling** — Add endpoint to receive Maxio events (subscription state, billing events)

## Files Structure

```
repo/
├── src/PublicApi/
│   ├── Program.cs                              (✓ modified: Maxio DI)
│   ├── appsettings.json                        (✓ modified: Maxio config)
│   ├── PublicApi.csproj                        (✓ modified: SDK package)
│   ├── MaxioSettings.cs                        (✓ new: config model)
│   ├── Services/
│   │   └── MaxioSubscriptionService.cs         (✓ new: SDK wrapper)
│   └── SubscriptionEndpoints/
│       ├── ListSubscriptionPlansEndpoint.cs    (✓ new)
│       ├── CreateSubscriptionEndpoint.cs       (✓ new)
│       └── ListUserSubscriptionsEndpoint.cs    (✓ new)
├── src/Infrastructure/
│   ├── Identity/
│   │   ├── ApplicationUser.cs                  (✓ modified: new fields)
│   │   └── Migrations/
│   │       └── 20260906221636_AddMaxioCustomerAndUserFields.cs  (✓ new)
├── Directory.Packages.props                    (✓ modified: SDK version)
├── maxio-plan.md                               (✓ new: SDK contract sheet)
├── MAXIO_IMPLEMENTATION_SUMMARY.md             (✓ new: this file)
└── MAXIO_INTEGRATION_VERIFICATION.md           (✓ new: test guide)
```

## Next Steps

1. **Run verification tests** — Follow `MAXIO_INTEGRATION_VERIFICATION.md` to validate the integration
2. **Deploy to staging** — Test against a Maxio staging environment
3. **Add webhook support** — Receive subscription state changes from Maxio
4. **Extend UI** — Add subscription management pages to the web storefront
5. **Performance tuning** — Monitor API latency, adjust timeouts, add caching if needed
6. **Production secrets** — Move credentials to a secrets manager (Azure Key Vault, AWS Secrets Manager, etc.)

## Contact & Support

For questions about the Maxio SDK:
- See embedded `maxio-plan.md` for the SDK contract sheet (exact signatures, error types, enum values)
- Refer to `dotnet-*` companion skills for usage patterns:
  - `dotnet-client-initialization` — HttpClient & SDK client setup
  - `dotnet-authentication` — Auth schemes & credentials
  - `dotnet-calling-endpoints` — Operation signatures & calling patterns
  - `dotnet-models` — Request/response models, enums, unions
  - `dotnet-error-handling` — Exception types & handling strategies
  - `dotnet-configuration-resilience` — Retries, timeouts, pagination

For Maxio API documentation:
- Sandbox: https://cp-exp-2.chargify.com (replace subdomain)
- API Docs: Maxio Advanced Billing API (v3) documentation
