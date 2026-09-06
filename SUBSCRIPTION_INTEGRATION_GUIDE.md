# Maxio Subscription Integration - eShopOnWeb

This document describes the recurring subscription billing integration added to eShopOnWeb using Maxio Advanced Billing.

## Overview

The integration adds a parallel subscription capability to eShopOnWeb (existing one-time commerce remains unchanged). Logged-in users can:
- Browse available subscription plans
- Subscribe to a plan
- View their active subscriptions

## Architecture

### Components Added

**Services:**
- `ISubscriptionService` / `SubscriptionService` - Core Maxio integration logic

**HTTP Endpoints (all JWT-authenticated):**
- `GET /api/subscription-plans` - List available plans
- `POST /api/subscriptions` - Create/subscribe to a plan
- `GET /api/my-subscriptions` - View user's active subscriptions

**Configuration:**
- Maxio SDK client registered in DI
- Configuration from environment variables → user-secrets

### Flow

1. **List Plans**: Fetch products from Maxio (hard-coded: `eshop-pro`, `basic-plan`)
2. **Create Subscription**: 
   - Look up or create Maxio customer (idempotent via user ID reference)
   - Create subscription for customer on selected plan
3. **View Subscriptions**: List customer's active subscriptions

## Environment Setup

### Prerequisites

1. .NET 8.0+ runtime (SDK 10 installed, will roll forward)
2. Maxio sandbox credentials:
   - API Key (site credential)
   - Site Subdomain (e.g., `cp-exp-1`)

3. User-secrets configured (done automatically on first setup):
   ```bash
   dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY" --project src/PublicApi/PublicApi.csproj
   dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN" --project src/PublicApi/PublicApi.csproj
   dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe" --project src/PublicApi/PublicApi.csproj
   dotnet user-secrets set "Maxio:BaseUrl" "" --project src/PublicApi/PublicApi.csproj  # leave empty for auto-derived
   ```

### Database

- The in-memory database is used (set `UseOnlyInMemoryDatabase=true` in environment or appsettings)
- Data is lost on service restart
- User → Subscription mapping persists only within a single run

## Running & Testing

### Start the Service

```bash
cd repo
# Set environment variables for credentials
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-1"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Run PublicApi
dotnet run --project src/PublicApi/PublicApi.csproj
```

Service listens on:
- HTTPS: `https://localhost:28003`
- HTTP: `http://localhost:28003` (if enabled)

### Get a JWT Token

First, authenticate to get a JWT token:

```bash
curl -X POST https://localhost:28003/api/authenticate \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }'
```

Response:
```json
{
  "result": true,
  "token": "eyJhbGc...",
  "username": "demouser@microsoft.com"
}
```

### Test the Endpoints

#### 1. List Subscription Plans

```bash
TOKEN="your-jwt-token"

curl -X GET https://localhost:28003/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

Expected response:
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "pricePointHandle": "default",
      "priceInCents": 0,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "pricePointHandle": "default",
      "priceInCents": 0,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

#### 2. Create a Subscription

```bash
curl -X POST https://localhost:28003/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "planHandle": "eshop-pro"
  }'
```

Expected response (201 Created):
```json
{
  "id": 12345,
  "state": "active",
  "productHandle": "eshop-pro",
  "pricePointHandle": "default",
  "nextAssessmentAt": null
}
```

#### 3. View User's Subscriptions

```bash
curl -X GET https://localhost:28003/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "productHandle": "eshop-pro",
      "pricePointHandle": "default",
      "currentPeriodStartsAt": null,
      "nextAssessmentAt": null
    }
  ]
}
```

## Key Implementation Notes

### Idempotent Customer Creation

- Customers are looked up by `reference` (user ID from eShopOnWeb)
- If not found (404), a new customer is created
- Multiple subscriptions calls for the same user reuse the existing Maxio customer

### Plan Selection

- Plans are hard-coded: `eshop-pro` ($299/mo), `basic-plan` ($29/mo)
- No trial period, no setup fees, no payment method required (per sandbox config)
- Plans are marked as "active" after creation

### Error Handling

- **Maxio API errors** (4xx, 5xx) are caught and logged
- **Connection errors** are caught and reported as 5xx
- Errors include the HTTP status and a user-safe message

### SDK Contract

The integration uses Maxio Advanced Billing .NET SDK v1.0.2:
- Auth: HTTP Basic (API key as username, "x" as password)
- Environment: US Production (subdomain-based URL)
- Operations: Customers, Subscriptions, Products

## Troubleshooting

### "Customer creation returned no customer data"

The Maxio API returned a 201 but the response body was empty or missing the customer record. This is a Maxio side issue.

### Subscription creation fails with 422

Either:
1. Product handle is wrong (verify in Maxio sandbox)
2. Payment method required (sandbox config issue)
3. Product/price-point missing (deleted from Maxio)

### 401 Unauthorized on endpoints

Token is missing or invalid. Get a fresh token from `/api/authenticate`.

### Service won't start

Check:
1. User-secrets are set (run `dotnet user-secrets list` from PublicApi directory)
2. Port 28003 is available (or change launchSettings.json)
3. HTTPS dev cert is trusted (run `dotnet dev-certs https --check`)

## Production Considerations

1. **Secrets Management**: Never hardcode API keys. Use Azure Key Vault or similar in production.
2. **Database Persistence**: In-memory DB is for dev only. Use SQL Server or Azure SQL in production.
3. **Retry & Timeout Tuning**: Current SDK defaults are suitable for sandbox. Adjust per SLA in production.
4. **Logging**: Integrate with Application Insights or similar for monitoring.
5. **Metered Components**: The `api-call` metered component is not yet implemented (future phase).

## Files Added/Modified

### New Files
- `src/PublicApi/SubscriptionEndpoints/SubscriptionService.cs` - Core service
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - GET plans endpoint
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - POST subscription endpoint
- `src/PublicApi/SubscriptionEndpoints/GetUserSubscriptionsEndpoint.cs` - GET user subscriptions endpoint

### Modified Files
- `src/PublicApi/Program.cs` - Added Maxio SDK client registration

## Next Steps

- [ ] Test with actual Maxio sandbox
- [ ] Verify payment-method-not-required behavior
- [ ] Implement metered component usage tracking
- [ ] Add UI in the web frontend
- [ ] Set up monitoring and alerting
- [ ] Performance test under load

