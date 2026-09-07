# Maxio Subscription Integration for eShopOnWeb

This document describes the Maxio subscription billing integration added to eShopOnWeb.

## Overview

eShopOnWeb now supports recurring subscription billing powered by Maxio Advanced Billing. The subscription feature is implemented as a parallel capability alongside existing one-time commerce (cart/checkout flow).

## Architecture

### Components

1. **Maxio Service Layer** (`src/Infrastructure/Services/Maxio/`)
   - `MaxioSettings.cs` - Configuration for Maxio API access
   - `MaxioApiClient.cs` - HTTP client for Maxio API communication
   - `MaxioDtos.cs` - Data transfer objects for API requests/responses
   - `MaxioSubscriptionService.cs` - Business logic orchestration for subscriptions

2. **Public API Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)
   - `GetSubscriptionPlansEndpoint.cs` - List available plans
   - `CreateSubscriptionEndpoint.cs` - Create subscription for user
   - `GetMySubscriptionsEndpoint.cs` - List user's subscriptions

3. **Configuration**
   - User Secrets store Maxio credentials (ApiKey, Subdomain)
   - `appsettings.json` contains ProductFamilyHandle and optional BaseUrl override

## Endpoints

All endpoints require JWT authentication (Bearer token from `/api/authenticate`).

### GET `/api/subscription-plans`
Lists available subscription plans from Maxio's product family.

**Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional plan",
      "priceInCents": 29900,
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "success": true
}
```

### POST `/api/subscriptions`
Creates a subscription for the authenticated user.

**Request:**
```json
{
  "planHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "success": true,
  "subscriptionId": 12345,
  "customerId": 67890,
  "state": "active",
  "createdAt": "2026-09-07T10:30:00Z",
  "nextBillingAt": "2026-10-07T10:30:00Z",
  "planName": "Pro Plan",
  "priceInCents": 29900,
  "price": 299.00
}
```

### GET `/api/my-subscriptions`
Lists all subscriptions for the authenticated user.

**Response:**
```json
{
  "success": true,
  "subscriptions": [
    {
      "subscriptionId": 12345,
      "customerId": 67890,
      "planName": "Pro Plan",
      "planHandle": "eshop-pro",
      "state": "active",
      "priceInCents": 29900,
      "price": 299.00,
      "createdAt": "2026-09-07T10:30:00Z",
      "updatedAt": "2026-09-07T10:30:00Z",
      "nextBillingAt": "2026-10-07T10:30:00Z"
    }
  ]
}
```

## Configuration

### Environment Variables
The integration reads from these environment variables (used by user-secrets):

- `MAXIO_API_KEY` - Maxio API key for authentication
- `MAXIO_SITE_SUBDOMAIN` - Maxio sandbox subdomain (e.g., "cp-exp-2")
- `MAXIO_ENVIRONMENT` - Environment identifier ("sandbox")
- `MAXIO_DEFAULT_PRODUCT_FAMILY` - Product family handle (default: "eshop-subscribe")

### Settings
Configure in `appsettings.json`:

```json
{
  "Maxio": {
    "ApiKey": "from-secrets",
    "Subdomain": "from-secrets",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": null
  }
}
```

- `BaseUrl` (optional): Override the default API base URL. If not set, uses `https://{Subdomain}.chargify.com`

## Setup and Verification

### 1. Configure User Secrets

```powershell
cd src/PublicApi

dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-sandbox-subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### 2. Run the Application

```powershell
dotnet run --project src/PublicApi/PublicApi.csproj
```

The PublicApi runs on `https://localhost:28983` (HTTPS) and `http://localhost:28984` (HTTP).

### 3. Test Authentication

Get a JWT token from the authenticate endpoint:

```bash
curl -X POST https://localhost:28984/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  -k
```

Extract the `token` field from the response.

### 4. Test Subscription Plans Endpoint

```bash
curl -X GET https://localhost:28983/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

### 5. Test Create Subscription

```bash
curl -X POST https://localhost:28983/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k
```

### 6. Test Get My Subscriptions

```bash
curl -X GET https://localhost:28983/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

## Implementation Details

### Customer Management
- Customers are identified in Maxio using a reference field: `eshop-{userId}`
- When creating a subscription, the system automatically:
  1. Checks if a customer with that reference exists
  2. Creates a new customer if needed (using user's name and email from JWT claims)
  3. Associates the subscription with the customer

### Subscription State
Maxio tracks subscription state with these common values:
- `active` - Active subscription, paid and up to date
- `trialing` - In trial period (if configured)
- `past_due` - Payment failed, past due
- `canceled` - Subscription canceled
- `expired` - Subscription expired
- `pending` - Being created

## Production Considerations

### Security
- Credentials are stored in .NET user-secrets, never in code or config files
- JWT authentication required for all subscription endpoints
- Basic Auth (API Key + 'X') used for Maxio API calls
- All connections to Maxio use HTTPS

### Reliability
- The MaxioApiClient includes error logging
- Subscription creation is idempotent (same user + plan = no duplicate customers)
- Configuration is validated on startup (settings injected as singletons)

### Data Persistence
- User subscriptions are retrieved real-time from Maxio
- No local subscription database required (Maxio is system of record)
- User-to-Maxio-customer mapping is via reference field (no local storage needed)

## Troubleshooting

### Missing Maxio Credentials
Error: "Maxio API Key or Subdomain not configured"
- Verify user-secrets are set correctly
- Check `dotnet user-secrets list --project src/PublicApi/PublicApi.csproj`

### API Connection Failures
- Verify the Maxio sandbox subdomain is correct
- Ensure the API key has permissions to the subscription-related endpoints
- Check network connectivity to `https://{subdomain}.chargify.com`

### Customer/Subscription Creation Failures
- Ensure the product family handle matches a real family in Maxio
- Verify product handles (e.g., "eshop-pro") exist in the product family
- Check Maxio sandbox site for the configured product family

## Files Added/Modified

### New Files
- `src/Infrastructure/Services/Maxio/MaxioSettings.cs`
- `src/Infrastructure/Services/Maxio/MaxioApiClient.cs`
- `src/Infrastructure/Services/Maxio/MaxioDtos.cs`
- `src/Infrastructure/Services/Maxio/MaxioSubscriptionService.cs`
- `src/PublicApi/SubscriptionEndpoints/GetSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs`

### Modified Files
- `src/Infrastructure/Dependencies.cs` - Added Maxio service registration
- `src/PublicApi/Program.cs` - Added HttpClient factory and Maxio configuration
- `src/PublicApi/appsettings.json` - Added Maxio configuration section

## Next Steps

To extend the integration:

1. **Subscription Management** - Add endpoints for updating/canceling subscriptions
2. **Webhooks** - Implement Maxio webhooks for subscription state changes
3. **Local Caching** - Add optional local database to cache subscription state
4. **Metered Components** - Support usage-based billing for metered components
5. **UI Integration** - Add subscription management UI to the web storefront

