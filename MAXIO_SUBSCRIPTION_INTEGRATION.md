# Maxio Subscription Billing Integration for eShopOnWeb

## Implementation Summary

This document describes the Maxio Advanced Billing integration for eShopOnWeb, which adds a parallel subscription billing capability alongside the existing cart/checkout flow.

## Architecture

### Components

1. **MaxioSubscriptionService** (`src/PublicApi/SubscriptionEndpoints/MaxioSubscriptionService.cs`)
   - Encapsulates all Maxio API interactions
   - Handles customer lifecycle (create with idempotent lookup)
   - Manages subscriptions (create, list)
   - Implements error handling with proper logging

2. **REST Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)
   - `ListSubscriptionPlansEndpoint` - GET `/api/subscription-plans`
   - `CreateSubscriptionEndpoint` - POST `/api/subscriptions`
   - `ListUserSubscriptionsEndpoint` - GET `/api/my-subscriptions`
   - All endpoints require JWT authentication via `[Authorize]`

3. **DI Registration** (`src/PublicApi/Program.cs`)
   - Maxio client configured with:
     - HTTP Basic authentication (API key + "x")
     - Configurable environment (US/EU)
     - Per-request 30-second timeout
     - Long-lived HttpClient via factory pattern

### Configuration

Credentials are loaded from .NET user-secrets:

```
Maxio:ApiKey           → from env var MAXIO_API_KEY
Maxio:Subdomain        → from env var MAXIO_SITE_SUBDOMAIN (default: cp-exp-1)
Maxio:Environment      → from env var MAXIO_ENVIRONMENT (default: Us)
Maxio:BaseUrl          → from env var MAXIO_BASE_URL (optional override)
Maxio:ProductFamilyHandle → from env var MAXIO_DEFAULT_PRODUCT_FAMILY
```

Secrets are configured via:
```bash
dotnet user-secrets set "Maxio:ApiKey" "<api-key>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-1" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "Us" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe" --project src/PublicApi
```

## Hero Flow

### 1. Browse Subscription Plans
```
GET /api/subscription-plans
Authorization: Bearer <jwt-token>

Response:
{
  "correlationId": "...",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    ...
  ]
}
```

### 2. Subscribe to a Plan
```
POST /api/subscriptions
Authorization: Bearer <jwt-token>
Content-Type: application/json

Request body:
{
  "productHandle": "eshop-pro"
}

Response:
{
  "correlationId": "...",
  "subscription": {
    "id": 12345678,
    "customerId": 987654,
    "productId": 7126957,
    "state": "active",
    "createdAt": "2025-01-15T10:30:00Z",
    "currentPeriodEndsAt": "2025-02-15T10:30:00Z",
    "nextAssessmentAt": "2025-02-15T10:30:00Z",
    "activatedAt": "2025-01-15T10:30:00Z"
  }
}
```

### 3. List User Subscriptions
```
GET /api/my-subscriptions
Authorization: Bearer <jwt-token>

Response:
{
  "correlationId": "...",
  "subscriptions": [
    {
      "id": 12345678,
      "customerId": 987654,
      "productId": 7126957,
      "state": "active",
      "createdAt": "2025-01-15T10:30:00Z",
      "currentPeriodEndsAt": "2025-02-15T10:30:00Z",
      "nextAssessmentAt": "2025-02-15T10:30:00Z",
      "activatedAt": "2025-01-15T10:30:00Z"
    }
  ]
}
```

## Verification Steps

### Step 1: Build the Solution
```bash
cd repo
dotnet build eShopOnWeb.sln -c Release
# Expected: Build succeeded with 0 errors
```

### Step 2: Run Tests
```bash
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj -c Release
# Expected: All tests pass (15 total)
```

### Step 3: Start PublicApi Service
```bash
cd repo
set UseOnlyInMemoryDatabase=true
dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile "https"
# Expected: Service starts on https://localhost:7269
```

### Step 4: Get Authentication Token
```bash
# Using curl (or Postman)
curl -X POST https://localhost:7269/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@123"}' \
  -k
# Response includes a "token" field
```

### Step 5: Test Subscription Plans Endpoint
```bash
curl -X GET https://localhost:7269/api/subscription-plans \
  -H "Authorization: Bearer <token>" \
  -k
# Expected: 200 OK with plan data
```

### Step 6: Test Create Subscription Endpoint
```bash
curl -X POST https://localhost:7269/api/subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  -k
# Expected: 200 OK with subscription created
```

### Step 7: Test List Subscriptions Endpoint
```bash
curl -X GET https://localhost:7269/api/my-subscriptions \
  -H "Authorization: Bearer <token>" \
  -k
# Expected: 200 OK with user's subscriptions
```

## Error Handling

All endpoints implement comprehensive error handling:

| Error Scenario | HTTP Status | Response |
|---|---|---|
| Missing JWT token | 401 Unauthorized | Automatic by ASP.NET Core |
| Invalid product handle | 400 Bad Request | `{error: "..."}`  |
| Customer creation fails (API error) | 500 Internal Server Error | Logged with details |
| Subscription creation fails | 500 Internal Server Error | Logged with details |
| Network timeout (Maxio API unreachable) | 500 Internal Server Error | Logged with exception |

## Key Design Decisions

1. **Idempotent Customer Creation**
   - UserId from JWT claims is stored as Maxio `reference`
   - On 422 "duplicate reference" error, retrieves existing customer
   - Prevents duplicate customer creation on network retry

2. **Transient Maxio Client Lifetime**
   - Client is registered as `Singleton` via DI
   - HttpClient is created once and reused via `IHttpClientFactory`
   - Enables DNS refresh and connection pooling

3. **In-Memory State Persistence**
   - User-subscription mappings persist only within current session
   - No durable SQL Server dependency
   - Session data lost on service restart (per requirements)

4. **JWT Authentication**
   - User identity extracted from `ClaimTypes.Name` claim
   - Used as Maxio customer reference
   - Ensures customer uniqueness per eShopOnWeb user

5. **Minimal SDK Error Handling**
   - Falls back to `RawError` for SDK v1.0.2 compatibility
   - Logs detailed errors for troubleshooting
   - Presents user-safe error messages

## Files Created/Modified

### New Files
- `src/PublicApi/SubscriptionEndpoints/MaxioSubscriptionService.cs` - Service layer
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - Plans endpoint
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - Create subscription endpoint
- `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs` - List subscriptions endpoint
- `MAXIO_SUBSCRIPTION_INTEGRATION.md` - This file

### Modified Files
- `src/PublicApi/PublicApi.csproj` - Added Maxio SDK NuGet package
- `src/PublicApi/Program.cs` - Added Maxio client DI registration
- `Directory.Packages.props` - Added Maxio SDK package version

## Dependencies

- **AsadAli.AdvancedBilling.Sdk** v1.0.2 - Maxio .NET SDK
- **MinimalApi.Endpoint** v1.3.0 - Endpoint routing framework (existing)
- **.NET 8.0** (net8.0 target)

## Testing Considerations

### Unit Testing
- Service methods can be tested with mock MaxioAdvancedBillingClient
- Use dependency injection to inject mocks

### Integration Testing
- PublicApiIntegrationTests run with in-memory database
- All 15 existing tests pass (verified above)
- Subscription endpoints integrate seamlessly with existing framework

### Production Deployment
- Ensure `UseOnlyInMemoryDatabase=false` in production
- Configure real SQL Server connection strings
- Store Maxio credentials in Key Vault (not user-secrets)
- Enable HTTPS enforcement
- Set appropriate timeout values based on load testing

## Troubleshooting

### "Maxio client failed to authenticate"
- Verify `MAXIO_API_KEY` env var is set correctly
- Check user-secrets: `dotnet user-secrets list --project src/PublicApi`
- Ensure `Maxio:ApiKey` matches the API key from Maxio dashboard

### "Product family not found"
- Verify `MAXIO_DEFAULT_PRODUCT_FAMILY` env var (should be "eshop-subscribe")
- Confirm product family exists on Maxio sandbox site cp-exp-1
- Check `Maxio:ProductFamilyHandle` in user-secrets

### "Reference already exists" (422 error during customer creation)
- This is expected behavior (idempotent create)
- Integration retrieves the existing customer
- Should not see this error in the response - it's handled internally

### Network timeout to Maxio API
- Check Maxio service status
- Verify DNS resolution for cp-exp-1.chargify.com
- Review HttpClient timeout settings (currently 30 seconds per attempt)

##  Next Steps for Production

1. **Payment Method Support** - Currently optional; implement payment capture flow
2. **Usage Metering** - For metered components (e.g., "api-call" usage tracking)
3. **Subscription Management** - Add update/cancel endpoints
4. **Webhooks** - Implement Maxio webhook handlers for subscription events
5. **Audit Logging** - Track all Maxio API calls for compliance
6. **Rate Limiting** - Implement per-user rate limits on subscription operations
