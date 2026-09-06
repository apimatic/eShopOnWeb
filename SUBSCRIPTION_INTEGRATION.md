# Maxio Subscription Integration for eShopOnWeb

This document describes the subscription billing integration with Maxio Advanced Billing for the eShopOnWeb reference application.

## Overview

Added a parallel, subscription-based billing capability to the existing one-time commerce flow. The integration allows authenticated users to:
1. Browse available subscription plans
2. Create a subscription
3. View their active subscriptions

## Architecture

### Components

- **MaxioSettings** (`src/PublicApi/MaxioSettings.cs`): Configuration for Maxio API credentials
- **MaxioApiClient** (`src/PublicApi/Services/MaxioApiClient.cs`): HTTP client for Maxio API communication
- **MaxioSubscriptionService** (`src/PublicApi/Services/MaxioSubscriptionService.cs`): Business logic for subscription management (idempotent customer creation, subscription creation/retrieval)
- **Subscription Endpoints** (`src/PublicApi/SubscriptionEndpoints/`): Three HTTP endpoints for managing subscriptions

### Endpoints

All endpoints are under `/api/` and available on the PublicApi service:

1. **GET /api/subscription-plans** - List available plans
   - No authentication required
   - Returns: List of subscription plans with pricing and intervals

2. **POST /api/subscriptions** - Create a subscription
   - Requires: JWT Bearer token authentication
   - Body: `{ "productHandle": "eshop-pro" }`
   - Returns: Subscription details (ID, state, pricing, next billing date)
   - Behavior: Idempotent - creates customer on first call, reuses on subsequent calls

3. **GET /api/my-subscriptions** - List user's subscriptions
   - Requires: JWT Bearer token authentication
   - Returns: List of user's subscriptions with product names and billing info

## Configuration

### Environment Variables (Required)

Set these before running:
```bash
MAXIO_API_KEY=<your-api-key>
MAXIO_SITE_SUBDOMAIN=cp-exp-2
MAXIO_ENVIRONMENT=US
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### User Secrets

The application automatically loads these from .NET user-secrets (configured in the PublicApi project):
- `Maxio:ApiKey`
- `Maxio:Subdomain`
- `Maxio:ProductFamilyHandle`
- `Maxio:BaseUrl` (optional override)

The user-secrets have already been populated with the environment variable values.

### appsettings.json

Configuration section is defined (values come from user-secrets or environment):
```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "",
    "ProductFamilyHandle": "",
    "BaseUrl": null
  }
}
```

## Sandbox Entities

Pre-seeded on `cp-exp-2`:
- Product Family: `eshop-subscribe`
- Plans:
  - `eshop-pro`: $299.00/mo
  - `basic-plan`: $29.00/mo
- Component: `api-call` - Metered, $0.01/unit

All plans are configured without requiring payment method upfront (suitable for testing).

## Running and Testing

### 1. Build

```bash
cd C:\claude-runs\t1h45ali-maxio-docs-mcp-haiku45high-023\repo
dotnet build src/PublicApi/PublicApi.csproj
```

### 2. Run

```bash
cd src/PublicApi
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run
```

The API will start on `https://localhost:27463`

### 3. Test the Integration

#### Step 1: Authenticate

Get a JWT token:
```bash
curl -X POST "https://localhost:27463/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser","password":"Pass@word123"}' \
  --insecure
```

Save the returned `token` value.

#### Step 2: List Available Plans

```bash
curl -X GET "https://localhost:27463/api/subscription-plans" \
  --insecure
```

Response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "priceInDollars": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

#### Step 3: Create a Subscription

```bash
curl -X POST "https://localhost:27463/api/subscriptions" \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

Response:
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "priceInCents": 29900,
  "priceInDollars": 299.00,
  "nextBillingDate": "2026-10-07T00:00:00Z",
  "createdAt": "2026-09-07T12:34:56Z"
}
```

#### Step 4: List User's Subscriptions

```bash
curl -X GET "https://localhost:27463/api/my-subscriptions" \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  --insecure
```

Response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productName": "Pro Plan",
      "priceInCents": 29900,
      "priceInDollars": 299.00,
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:34:56Z",
      "updatedAt": "2026-09-07T12:34:56Z"
    }
  ]
}
```

## Key Features

1. **Idempotent Customer Creation**: Double-clicking subscribe never creates duplicate customers. The service checks if a customer with the user's ID already exists before creating a new one.

2. **JWT Authentication**: All state-changing operations (POST) require authentication via the PublicApi's JWT token system.

3. **Error Handling**: Comprehensive error handling with meaningful error messages in the response payload.

4. **In-Memory Database Support**: Works with `UseOnlyInMemoryDatabase=true` for local development (data persists only during single run).

5. **No External Infrastructure**: Uses only .NET SDK/runtime; no Docker, broker, or PostgreSQL required.

## Files Modified/Created

### New Files
- `src/PublicApi/MaxioSettings.cs`
- `src/PublicApi/Services/MaxioApiClient.cs`
- `src/PublicApi/Services/MaxioSubscriptionService.cs`
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs`

### Modified Files
- `src/PublicApi/Program.cs` - Added Maxio service registration and endpoint mapping
- `src/PublicApi/appsettings.json` - Added Maxio configuration section

## Constraints Honored

✅ Secrets never enter the repository (loaded from environment variables via user-secrets)
✅ Production-grade implementation with proper error handling
✅ JWT authentication for all protected endpoints
✅ Idempotent operations (no duplicate subscriptions)
✅ No external infrastructure beyond .NET SDK
✅ Follows eShopOnWeb conventions (minimal endpoints, dependency injection, configuration)

## Future Enhancements

- Add subscription management operations (update, cancel)
- Add webhook handlers for Maxio events
- Integrate with BlazorAdmin for subscription management UI
- Add metrics and logging for billing operations
- Support for prepaid subscriptions and usage tracking
