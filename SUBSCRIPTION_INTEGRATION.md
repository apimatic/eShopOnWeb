# Maxio Subscription Integration for eShopOnWeb

This document describes the Maxio subscription billing integration added to the eShopOnWeb PublicApi.

## Overview

The integration adds recurring-subscription capability to eShopOnWeb via Maxio Advanced Billing. This is an additive, parallel capability that does not replace the existing cart/checkout flow.

## Architecture

### New Services

1. **MaxioSettings** - Configuration class for Maxio API credentials
2. **MaxioClient** - HTTP client for Maxio API communication (Basic auth)
3. **SubscriptionService** - Business logic for subscription operations
4. **Endpoints** - Three new REST endpoints under `/api/`

### Configuration

Maxio credentials are loaded from environment variables:
- `MAXIO_API_KEY` - Maxio API key
- `MAXIO_SITE_SUBDOMAIN` - Maxio site subdomain
- `MAXIO_DEFAULT_PRODUCT_FAMILY` - Product family handle for plans
- `MAXIO_BASE_URL` (optional) - Override API base URL

The configuration binds to `appsettings.json` under the `Maxio:` section.

## API Endpoints

All endpoints require JWT authentication (except `GET /api/subscription-plans` which allows anonymous access).

### 1. GET /api/subscription-plans
Returns available subscription plans.

**Authentication**: Optional (AllowAnonymous)

**Response**:
```json
{
  "correlationId": "uuid",
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "$299 Pro Plan",
      "description": "Professional plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "success": true
}
```

### 2. POST /api/subscriptions
Creates a subscription for the authenticated user.

**Authentication**: Required (Bearer JWT)

**Request Body**:
```json
{
  "productHandle": "eshop-pro"
}
```

**Response**:
```json
{
  "correlationId": "uuid",
  "success": true,
  "subscriptionId": 12345,
  "state": "active",
  "planName": "$299 Pro Plan",
  "priceInCents": 29900,
  "nextBillingDate": "2026-10-07T00:00:00Z"
}
```

### 3. GET /api/my-subscriptions
Returns the authenticated user's subscriptions.

**Authentication**: Required (Bearer JWT)

**Response**:
```json
{
  "correlationId": "uuid",
  "success": true,
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "planName": "$299 Pro Plan",
      "priceInCents": 29900,
      "activatedAt": "2026-09-07T10:30:00Z",
      "currentPeriodStartedAt": "2026-09-07T00:00:00Z",
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
      "nextBillingDate": "2026-10-07T00:00:00Z",
      "balanceInCents": 0
    }
  ]
}
```

## Maxio Integration Details

### Customer Management
- Customers are identified by a unique reference: `eshop_{userId}`
- The integration ensures idempotent customer creation (no duplicates on retry)
- Customer lookup uses the reference to check if they already exist in Maxio

### Subscription Creation
- Subscriptions are created with `payment_collection_method: "remittance"` (no payment method required)
- The product is identified by handle (not ID) as handles are stable across re-seeds
- Customer data is synced from the eShopOnWeb user profile

### API Contract
All Maxio API interactions follow the OpenAPI specification in `maxio-spec/openapi.yaml`.

## Files Created/Modified

### New Files
- `src/ApplicationCore/Services/MaxioSettings.cs` - Configuration
- `src/ApplicationCore/Services/MaxioClient.cs` - HTTP client
- `src/ApplicationCore/Services/MaxioDtos.cs` - Request/response DTOs
- `src/ApplicationCore/Services/SubscriptionService.cs` - Business logic
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlansEndpoint.cs` - Plans endpoint
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - Create subscription endpoint
- `src/PublicApi/SubscriptionEndpoints/MySubscriptionsEndpoint.cs` - List subscriptions endpoint

### Modified Files
- `src/PublicApi/Program.cs` - Added Maxio service registration and configuration
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `global.json` - Updated rollForward to latestMajor

## Running Tests

### Setup

1. Set environment variables with Maxio sandbox credentials:
```powershell
$env:MAXIO_API_KEY = "your_api_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
```

2. Build the project:
```powershell
dotnet build src/PublicApi/PublicApi.csproj
```

3. Run the PublicApi:
```powershell
dotnet run --project src/PublicApi/PublicApi.csproj
```

### Test Workflow

1. **Authenticate** - Get a JWT token:
```bash
curl -X POST https://localhost:27483/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

2. **List subscription plans** (no auth needed):
```bash
curl https://localhost:27483/api/subscription-plans
```

3. **Create a subscription**:
```bash
curl -X POST https://localhost:27483/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

4. **Get user's subscriptions**:
```bash
curl https://localhost:27483/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN"
```

## Error Handling

- The integration logs errors via ILogger at the service level
- HTTP errors from Maxio are logged with status codes and response bodies
- Graceful degradation: operations return `null` on failure, which endpoints translate to appropriate HTTP status codes
- All endpoint responses include a `success` boolean flag

## Production Considerations

1. **Secrets Management** - Maxio credentials are loaded from environment variables only, never stored in code
2. **Logging** - Errors are logged but sensitive data is excluded
3. **Idempotency** - Customer creation is idempotent (safe to retry)
4. **API Resilience** - The client includes basic error handling and logging
5. **Database Persistence** - User-subscription mappings use the in-memory database in this configuration (use UseOnlyInMemoryDatabase=true)

## Future Enhancements

1. Add subscription cancellation endpoint
2. Add webhook handling for subscription lifecycle events
3. Add subscription modification (plan changes)
4. Add customer portal link generation
5. Persist subscription data in the application database for caching and analytics
