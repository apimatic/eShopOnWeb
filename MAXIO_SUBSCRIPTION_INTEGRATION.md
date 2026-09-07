# Maxio Subscription Billing Integration - eShopOnWeb

## Overview

This document describes the completed Maxio Advanced Billing integration for eShopOnWeb. The integration adds a parallel subscription capability alongside the existing one-time cart/checkout flow.

## Architecture

### New Components

**Endpoints (PublicApi project):**
- `GET /api/subscription-plans` - Lists available subscription plans
- `POST /api/subscriptions` - Enrolls logged-in user in a subscription  
- `GET /api/my-subscriptions` - Retrieves user's active subscriptions

**Implementation Files:**
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - Lists plans from Maxio
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - Creates customer & subscription
- `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs` - Lists user's subscriptions
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlan.cs` - DTO for plan details

### Configuration

**Appsettings:**
```json
{
  "Maxio": {
    "ApiKey": "from MAXIO_API_KEY env var (set via user-secrets)",
    "Subdomain": "from MAXIO_SITE_SUBDOMAIN env var (set via user-secrets)",
    "Environment": "US",
    "ProductFamilyHandle": "from MAXIO_DEFAULT_PRODUCT_FAMILY env var (set via user-secrets)",
    "BaseUrl": null
  }
}
```

**DI Registration (Program.cs):**
- `AddMaxioAdvancedBillingClient()` - Registers Maxio SDK client
- `AddHttpContextAccessor()` - Provides access to HTTP context for user identity
- HTTP Basic auth configured with API key

### Data Flow

**GET /api/subscription-plans:**
1. Client calls endpoint with JWT token
2. Endpoint calls `Products.ListProducts()` on Maxio SDK
3. Returns array of available plans (handle, name, price, interval)

**POST /api/subscriptions:**
1. Client sends JWT token + desired product handle
2. Endpoint extracts user ID from JWT claims
3. Looks up or creates Maxio customer (idempotent by user reference)
4. Creates subscription (idempotent by reference key: `{userId}-{productHandle}-{timestamp}`)
5. Returns subscription details (ID, state, price, next billing date)

**GET /api/my-subscriptions:**
1. Client calls endpoint with JWT token
2. Endpoint lists all subscriptions, filters by user's customer reference
3. Returns array of user's subscriptions

## Key Implementation Details

### Idempotency

**Customer Creation:**
- First call: `ReadCustomerByReference(userID)` → returns 404 (RawError)
- Then: `CreateCustomer()` with `Reference = userID`
- Duplicate calls: 422 error caught gracefully

**Subscription Creation:**
- Each subscription includes a unique reference: `{userId}-{productHandle}-{timestamp}`
- Prevents duplicate subscriptions from multiple clicks
- On re-send: 422 error indicates already created

### Error Handling

**SDK Exceptions (from maxio-sdk agent guidance):**

- **Case A (Typed Errors):** Operations with `{Operation}Error` model
  - `CreateSubscription` throws `SdkException<CreateSubscriptionError>`
  - `CreateCustomer` throws `SdkException<CreateCustomerError>`
  - Use `TryGet{ErrorType}()` accessors to inspect error details

- **Case B (RawError):** Operations without typed error model
  - `ListProducts`, `ListSubscriptions` throw `SdkException<RawError>`
  - Access `ex.Error.StatusCode` and `ex.Error.ReadAsString()`

- **JsonException:** Thrown when response body cannot deserialize
  - Occurs on drifted 2xx bodies (missing required fields)
  - Also thrown while constructing error objects on malformed error responses
  - Caught separately and returned as 500 error to client

### Authentication

**JWT-based authentication:**
- All endpoints require `[Authorize]` attribute
- User identity extracted from JWT `ClaimTypes.Name` claim
- User ID used as deterministic Maxio customer reference
- Prevents duplicate customers/subscriptions across multiple app instances

## Building & Running

### Prerequisites

1. .NET SDK/Runtime:
   - Solution uses .NET 8.0 (net8.0 target framework)
   - `global.json` pins SDK to 8.0.x; works with .NET 10 SDK via `rollForward: latestMajor`

2. Environment Variables (set via user-secrets or system):
   ```bash
   MAXIO_API_KEY=<sandbox API key>
   MAXIO_SITE_SUBDOMAIN=<sandbox site subdomain, e.g., cp-exp-4>
   MAXIO_ENVIRONMENT=US
   MAXIO_DEFAULT_PRODUCT_FAMILY=<product family handle, e.g., eshop-subscribe>
   ```

3. Database:
   - Default uses LocalDB (`(localdb)\mssqllocaldb`)
   - For environments without LocalDB, set `UseOnlyInMemoryDatabase=true`
   - In-memory database loses data on restart (test only)

### Build

```bash
cd C:\claude-runs\t1h45ali-maxio-sdk-haiku45high-059\repo
$env:DOTNET_ROLL_FORWARD="Major"
dotnet build eShopOnWeb.sln --configuration Release
```

Expected output: `Build succeeded.` with warnings only (no errors).

### Run PublicApi Server

```bash
cd src/PublicApi
$env:DOTNET_ROLL_FORWARD="Major"
$env:UseOnlyInMemoryDatabase="true"  # if no SQL Server LocalDB
dotnet run --configuration Release
```

Server listens on:
- HTTPS: https://localhost:28563
- HTTP: http://localhost:28564

Swagger UI: https://localhost:28563/swagger

## Testing

### 1. List Subscription Plans

**cURL:**
```bash
# Authenticate first
TOKEN=$(curl -X POST https://localhost:28563/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"P@ssw0rd"}' \
  --insecure | jq -r '.token')

# List plans
curl -H "Authorization: Bearer $TOKEN" \
  https://localhost:28563/api/subscription-plans \
  --insecure
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "Month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "Month"
    }
  ]
}
```

### 2. Create Subscription

**cURL:**
```bash
# Use token from above
curl -X POST https://localhost:28563/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

**Expected Response:**
```json
{
  "id": 123456789,
  "customerId": 987654,
  "state": "Active",
  "productPriceInCents": 29900,
  "nextBillingDate": "2025-09-30T00:00:00Z",
  "currentPeriodEndsAt": "2025-10-30T00:00:00Z"
}
```

### 3. Get User's Subscriptions

**cURL:**
```bash
curl -H "Authorization: Bearer $TOKEN" \
  https://localhost:28563/api/my-subscriptions \
  --insecure
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 123456789,
      "state": "Active",
      "productName": "Pro Plan",
      "productPriceInCents": 29900,
      "nextBillingDate": "2025-09-30T00:00:00Z",
      "activatedAt": "2025-09-07T12:34:56Z",
      "currentPeriodEndsAt": "2025-10-30T00:00:00Z"
    }
  ]
}
```

## Maxio Sandbox Details

**Site:** cp-exp-4
**Environment:** US (https://cp-exp-4.chargify.com)

**Pre-seeded Product Family:** eshop-subscribe
- **Pro Plan** (handle: `eshop-pro`): $299.00/month
- **Basic Plan** (handle: `basic-plan`): $29.00/month

No payment method required for subscription creation (payments optional for test plans).

## Files Changed

**Created:**
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlan.cs`
- `maxio-plan.md` (SDK contract sheet from planning agent)

**Modified:**
- `src/PublicApi/Program.cs` - Added Maxio client registration, HttpContextAccessor
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/PublicApi/PublicApi.csproj` - Added Maxio SDK package reference
- `Directory.Packages.props` - Added Maxio SDK version management

## Troubleshooting

**Build errors with SDK types:**
- Ensure `using MaxioAdvancedBilling;` and related imports are present
- Check operation signatures match the contract sheet (maxio-plan.md)
- SDK method signatures have leading non-defaulted nullable params; pass null explicitly

**Runtime: "Could not load assembly Microsoft.Bcl.AsyncInterfaces":**
- Already fixed in this build (package added to Directory.Packages.props)
- Ensure clean rebuild: `dotnet clean && dotnet build`

**404 on subscription endpoints:**
- Verify JWT token is valid
- Check `[Authorize]` attribute is present on endpoint class
- Verify endpoint is registered: GET https://localhost:28563/swagger/v1/swagger.json

**Maxio API errors (422, 400):**
- Check error response for validation details
- Ensure ProductHandle matches a valid product in the family
- Verify customer reference format (should be user ID or email)

## Production Considerations

1. **Persistence:** Replace in-memory database with SQL Server for production
2. **Security:** Use strong API key, rotate regularly, store in Azure Key Vault
3. **Resilience:** Adjust retry and timeout settings in `dotnet-configuration-resilience`
4. **Logging:** Add request/response logging via custom `DelegatingHandler`
5. **Monitoring:** Track Maxio API errors and response times
6. **Webhooks:** Implement Maxio webhooks for subscription lifecycle events (renewal, cancellation)

## References

- **Maxio SDK:** AsadAli.AdvancedBilling.Sdk v1.0.2
- **APIMatic:** Generated .NET SDK from Maxio OpenAPI spec
- **Authentication:** HTTP Basic (API key as username, "x" as password)
- **API Docs:** https://maxio-api.readme.io/
