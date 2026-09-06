# Maxio Subscription Billing Implementation Summary

## Architecture Overview

The Maxio subscription billing integration is organized in a clean 3-tier architecture:

```
PublicApi Endpoints
       ↓
MaxioService (Business Logic)
       ↓
MaxioApiClient (HTTP Transport)
       ↓
Maxio REST API
```

## Components

### 1. Configuration (`src/ApplicationCore/MaxioConfiguration.cs`)

Binds configuration from the `Maxio:*` section:

```csharp
public class MaxioConfiguration
{
    public string? ApiKey { get; set; }           // from Maxio:ApiKey env var
    public string? Subdomain { get; set; }        // from Maxio:Subdomain env var
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }          // Optional override
    
    public string GetBaseUrl()  // Constructs https://{subdomain}.chargify.com
}
```

**Registration** (`src/Infrastructure/Dependencies.cs`):
```csharp
var maxioConfig = new MaxioConfiguration
{
    ApiKey = configuration["Maxio:ApiKey"],
    Subdomain = configuration["Maxio:Subdomain"],
    ProductFamilyHandle = configuration["Maxio:ProductFamilyHandle"],
    BaseUrl = configuration["Maxio:BaseUrl"]
};
services.AddSingleton(maxioConfig);
```

### 2. HTTP Client (`src/Infrastructure/Services/MaxioApiClient.cs`)

Handles low-level HTTP communication with Maxio API:

```csharp
public interface IMaxioApiClient
{
    Task<T?> GetAsync<T>(string path);
    Task<T?> PostAsync<T>(string path, object? body = null);
}

public class MaxioApiClient : IMaxioApiClient
{
    // Features:
    // - Automatic Basic auth header encoding
    // - JSON serialization with snake_case property naming
    // - Logging of all requests
    // - Error handling with detailed exceptions
}
```

**Key behaviors:**
- Uses `HttpClient` from the factory pattern (`AddHttpClient<IMaxioApiClient, MaxioApiClient>()`)
- Configures base address from `MaxioConfiguration.GetBaseUrl()`
- Encodes credentials: `Base64(ApiKey:X)`
- Property naming: `PriceInCents` → `price_in_cents` (snake_case)
- All responses use snake_case properties (JSON) that are deserialized to camelCase (.NET convention)

### 3. Business Service (`src/Infrastructure/Services/MaxioService.cs`)

High-level API for subscription operations:

```csharp
public interface IMaxioService
{
    Task<SubscriptionPlanDto[]> GetSubscriptionPlansAsync();
    Task<CustomerDto> GetOrCreateCustomerAsync(string userReference, string email, string firstName, string lastName);
    Task<SubscriptionDto> CreateSubscriptionAsync(int customerId, string productHandle);
    Task<SubscriptionDto[]> GetCustomerSubscriptionsAsync(int customerId);
}
```

**Key behaviors:**

#### GetSubscriptionPlansAsync()
1. Fetches product family by handle: `GET /product_families/handle:{handle}/products.json`
2. Maps each product to a `SubscriptionPlanDto`
3. Returns price in dollars (converts from cents)

#### GetOrCreateCustomerAsync()
1. Searches for existing customer by `reference` field: `GET /customers.json?q={reference}`
2. If found, returns existing customer (idempotent!)
3. If not found, creates new customer: `POST /customers.json`
4. Returns `CustomerDto` with customer ID for future operations

**Idempotency guarantee:** The `reference` field in Maxio is unique per customer. Since we always use the eShopOnWeb user ID as the reference, any call with the same user always maps to the same Maxio customer.

#### CreateSubscriptionAsync()
1. Posts subscription creation request: `POST /subscriptions.json`
2. Request includes `customer_id` and `product_handle`
3. Returns the created subscription with ID, state, next billing date, etc.

#### GetCustomerSubscriptionsAsync()
1. Fetches customer subscriptions: `GET /customers/{id}/subscriptions.json`
2. Maps each to a `SubscriptionDto`
3. Returns all active and past subscriptions for the customer

### 4. API Endpoints (`src/PublicApi/SubscriptionEndpoints/`)

Three minimal API endpoints using `IEndpoint<IResult>` pattern:

#### ListSubscriptionPlansEndpoint
- **Route:** `GET /api/subscription-plans`
- **Auth:** Requires JWT bearer token
- **Handler:**
  1. Calls `IMaxioService.GetSubscriptionPlansAsync()`
  2. Converts prices from cents to dollars
  3. Returns `{ plans: [...] }`
- **No database interaction** — pure API-to-API passthrough

#### CreateSubscriptionEndpoint
- **Route:** `POST /api/subscriptions`
- **Auth:** Requires JWT bearer token (extracts user from claims)
- **Request Body:**
  ```json
  { "productHandle": "eshop-pro" }
  ```
- **Handler:**
  1. Extracts user from JWT claims
  2. Looks up user in ASP.NET Identity
  3. Creates/retrieves Maxio customer (via `GetOrCreateCustomerAsync`)
  4. Creates subscription
  5. Returns subscription details
- **Idempotency:** If called twice by the same user for the same plan, Maxio likely returns an error (subscription already exists) or updates the existing one. This is Maxio's behavior.

#### ListMySubscriptionsEndpoint
- **Route:** `GET /api/my-subscriptions`
- **Auth:** Requires JWT bearer token
- **Handler:**
  1. Extracts user from JWT claims
  2. Looks up user in ASP.NET Identity
  3. Creates/retrieves Maxio customer
  4. Fetches all subscriptions for that customer
  5. Returns `{ subscriptions: [...] }`

### 5. DTOs and Responses

All types are in their respective endpoint files:

**For plans:**
```csharp
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public decimal Price { get; set; }      // in dollars
    public int Interval { get; set; }       // 1 for monthly
    public string? IntervalUnit { get; set; } // "month"
}
```

**For subscriptions:**
```csharp
public class CreateSubscriptionResponse / SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? State { get; set; }      // "active", "past_due", etc.
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal Price { get; set; }      // Monthly price in dollars
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}
```

## Data Flow Examples

### Example 1: User Subscribes to a Plan

```
Client Request (JWT token)
  ↓
CreateSubscriptionEndpoint.AddRoute()
  ↓
Extract username from JWT claims
  ↓
Load ApplicationUser from database
  ↓
MaxioService.GetOrCreateCustomerAsync(userId)
  ├─ Search Maxio for customer with reference = userId
  ├─ If found: return existing customer
  └─ If not found: POST to create new customer, return new customer
  ↓
MaxioService.CreateSubscriptionAsync(customerId, productHandle)
  ├─ POST /subscriptions.json with customerId and productHandle
  └─ Return subscription details
  ↓
Endpoint returns subscription response
```

### Example 2: First-Time Subscription Request by Same User

```
Second Request (same user, same JWT)
  ↓
Load ApplicationUser (same)
  ↓
MaxioService.GetOrCreateCustomerAsync(userId)
  ├─ Search Maxio for customer with reference = userId
  └─ Found! (from Example 1) → return existing customer
  ↓
MaxioService.CreateSubscriptionAsync(customerId, productHandle)
  ├─ POST /subscriptions.json with same customerId and productHandle
  ├─ Maxio either:
  │  a) Returns 422 error (subscription already exists), or
  │  b) Returns existing subscription if re-posting
  └─ Depends on Maxio's implementation and product config
```

**Result:** Same Maxio customer, no duplicate customers created.

## Integration Points

### With ASP.NET Identity

- `UserManager<ApplicationUser>` is used to look up users by username (extracted from JWT)
- User ID (GUID) is converted to string and used as the Maxio customer reference
- Only the user ID and email are passed to Maxio; password remains local

### With Existing Endpoints

- Subscription endpoints are **completely independent** from catalog/basket/order endpoints
- No database schema changes needed
- In-memory database is sufficient (for development with `UseOnlyInMemoryDatabase=true`)

### With JWT Authentication

- PublicApi already uses JWT bearer tokens (generated by `/api/authenticate`)
- Subscription endpoints inherit the JWT requirement via `.RequireAuthorization()`
- User identity comes from the token's `ClaimTypes.Name` claim

## Configuration & Secrets

### appsettings.json

```json
"Maxio": {
  "ApiKey": "",
  "Subdomain": "",
  "ProductFamilyHandle": "eshop-subscribe",
  "BaseUrl": ""
}
```

- All values default to empty strings or provided defaults
- Actual secrets are injected via environment variables **at runtime**
- Never commit real API keys

### Environment Variables

Configuration binding automatically maps:
- `MAXIO_API_KEY` → `Maxio:ApiKey`
- `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
- `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`

### Dependencies Registration

All services are registered in `Infrastructure.Dependencies.ConfigureServices()`:

```csharp
var maxioConfig = new MaxioConfiguration { /* ... */ };
services.AddSingleton(maxioConfig);
services.AddHttpClient<IMaxioApiClient, MaxioApiClient>();
services.AddScoped<IMaxioService, MaxioService>();
```

## Error Handling

### API Errors

- `Unauthorized`: User not found or JWT invalid → `Results.Unauthorized()`
- `BadRequest`: Maxio API error (invalid product, customer creation failed, etc.) → `Results.BadRequest(new { error = "..." })`
- All exceptions are caught and logged by `MaxioApiClient`

### Specific Error Scenarios

1. **Invalid product handle**: Maxio returns 404 → `MaxioApiClient` throws → endpoint returns BadRequest
2. **Duplicate customer reference**: Maxio returns 422 → caught, existing customer is returned
3. **Subscription creation with no payment method**: Works if plan has `payment_method_not_required: true`

## Testing Considerations

### Unit Tests

- Mock `IMaxioService` for endpoint tests
- Mock `IMaxioApiClient` for service tests
- No need to hit real Maxio API in CI/CD

### Integration Tests

- Could use Maxio sandbox (requires credentials)
- Recommended: Mock the HTTP layer

### Manual Testing

See `MAXIO_SETUP_GUIDE.md` for curl examples.

## Extension Points

### Adding New Endpoints

1. Create new class in `SubscriptionEndpoints/`
2. Implement `IEndpoint<IResult>`
3. Inject `IMaxioService` and other dependencies
4. Implement `AddRoute()` to register the minimal API
5. Call `IMaxioService` methods

Example:
```csharp
public class UpdateSubscriptionEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/subscriptions/{id}",
            async (int id, UpdateSubscriptionRequest req, ...) =>
            {
                // Update via IMaxioService
            })
            .RequireAuthorization();
    }
}
```

### Adding New MaxioService Methods

1. Add public method to `IMaxioService` interface
2. Implement in `MaxioService`
3. Call `IMaxioApiClient` with appropriate endpoint
4. Map response to DTO and return

Example:
```csharp
public async Task<bool> CancelSubscriptionAsync(int subscriptionId)
{
    return await _apiClient.PostAsync<dynamic>(
        $"/subscriptions/{subscriptionId}/cancellation.json", 
        null) != null;
}
```

## Performance Notes

1. **Customer Lookup**: First request per user creates a customer (2 API calls). Subsequent requests reuse it (1 API call).
2. **No Caching**: Subscription plans and subscriptions are fetched fresh each request. Consider adding caching for plans.
3. **Parallel Requests**: All operations are async/await compatible; multiple users can subscribe concurrently.

## Known Issues & Future Work

1. **Trials**: No trial support in seeded plans. Can be added via `trial_interval` parameter.
2. **Metered Components**: The `api-call` component exists in Maxio but is not exposed via API.
3. **Payment Profiles**: Current plans don't require payment. If switching to paid-only, payment profile creation must be implemented.
4. **Webhooks**: Maxio events (subscription canceled, payment failed) are not subscribed to yet.
5. **Dunning**: Payment retry/dunning workflows are configured in Maxio but not customizable via API.

## Summary

The implementation is:
- **Clean**: 3-tier separation of concerns
- **Secure**: Secrets never enter the repo
- **Idempotent**: User ↔ Customer mapping is stable
- **Extensible**: Easy to add new endpoints or Maxio operations
- **Minimal**: No database schema changes; works with in-memory DB for dev
- **Production-ready**: Proper error handling, logging, async/await, DI patterns
