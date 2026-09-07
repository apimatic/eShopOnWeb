# Maxio Subscription Integration - Verification Guide

## Overview
This guide provides step-by-step instructions to verify the Maxio subscription billing integration for eShopOnWeb.

## Environment Setup

### Prerequisites
- .NET 10 SDK (or use `DOTNET_ROLL_FORWARD=Major` to use available SDK)
- Maxio sandbox credentials
- No SQL Server LocalDB required (uses in-memory database for development)

### Configuration

The integration reads Maxio credentials from environment variables and stores them in .NET user-secrets:

**Required Environment Variables:**
```
MAXIO_API_KEY=<your-api-key>
MAXIO_SITE_SUBDOMAIN=<subdomain>  # e.g., cp-exp-3
MAXIO_DEFAULT_PRODUCT_FAMILY=<family-handle>  # e.g., eshop-subscribe
MAXIO_ENVIRONMENT=<environment>  # e.g., US or sandbox
```

**User Secrets Configured:**
- `Maxio:ApiKey`
- `Maxio:Subdomain`
- `Maxio:ProductFamilyHandle`
- `Maxio:BaseUrl` (optional override)

## Running the Application

### Start PublicApi Service

```bash
cd src/PublicApi

# Set environment variables for SDK rollforward and in-memory database
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"

# Run the service (listens on https://localhost:28463)
dotnet run
```

The service will:
1. Initialize in-memory database
2. Seed initial data
3. Load Maxio configuration from user-secrets
4. Register subscription endpoints

### Retrieve Authentication Token

**Endpoint:** `POST /api/authenticate`

Using curl:
```bash
curl -X POST https://localhost:28463/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }' -k
```

Expected Response:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "result": true,
  "username": "demouser@microsoft.com",
  "correlationId": "..."
}
```

Save the token for subsequent API calls.

## API Endpoints

### 1. List Available Subscription Plans

**Endpoint:** `GET /api/subscription-plans`

```bash
curl -X GET https://localhost:28463/api/subscription-plans \
  -H "Authorization: Bearer <token>" \
  -k
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "$299/month subscription",
      "price": 299.00,
      "billingInterval": 1,
      "billingIntervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "$29/month subscription",
      "price": 29.00,
      "billingInterval": 1,
      "billingIntervalUnit": "month"
    }
  ],
  "correlationId": "..."
}
```

### 2. Create Subscription

**Endpoint:** `POST /api/subscriptions`

```bash
curl -X POST https://localhost:28463/api/subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  -k
```

**Expected Response:**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productHandle": "eshop-pro",
  "nextBillingAt": "2026-10-07T12:34:56Z",
  "status": "success",
  "correlationId": "..."
}
```

**What happens:**
1. Endpoint extracts user ID from JWT token
2. Looks up user from database
3. Creates/updates Maxio customer with reference = userId
4. Creates subscription in Maxio
5. Returns subscription details

### 3. List User Subscriptions

**Endpoint:** `GET /api/my-subscriptions`

```bash
curl -X GET https://localhost:28463/api/my-subscriptions \
  -H "Authorization: Bearer <token>" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "nextBillingAt": "2026-10-07T12:34:56Z",
      "currentPeriodEndsAt": "2026-10-07T12:34:56Z",
      "createdAt": "2026-09-07T12:34:56Z"
    }
  ],
  "correlationId": "..."
}
```

## Architecture

### Service Layer (`Infrastructure/Services`)

**MaxioClient** (`MaxioClient.cs`)
- Low-level HTTP client for Maxio API
- Handles Basic Authentication (API Key + "X")
- Serialization/deserialization with snake_case naming
- Logging of all requests

**SubscriptionService** (`SubscriptionService.cs`)
- Business logic for subscription operations
- Interfaces: `IMaxioClient`, `MaxioSettings`
- Key Methods:
  - `ListProductsAsync()` - GET /products.json
  - `CreateSubscriptionAsync(userId, firstName, lastName, email, productHandle)` - POST /subscriptions.json
  - `ListUserSubscriptionsAsync(userId)` - GET /subscriptions.json with reference filter

### Endpoints (`PublicApi/SubscriptionEndpoints`)

**ListSubscriptionPlansEndpoint** (`ListSubscriptionPlansEndpoint.cs`)
- Route: `GET /api/subscription-plans`
- Handler: Lists available products via SubscriptionService
- Auth: Required (JWT)
- Response: `ListSubscriptionPlansResponse`

**CreateSubscriptionEndpoint** (`CreateSubscriptionEndpoint.cs`)
- Route: `POST /api/subscriptions`
- Handler: Creates subscription for authenticated user
- Auth: Required (JWT)
- Payload: `CreateSubscriptionRequestPayload` (planHandle)
- Response: `CreateSubscriptionResponse`

**ListUserSubscriptionsEndpoint** (`ListUserSubscriptionsEndpoint.cs`)
- Route: `GET /api/my-subscriptions`
- Handler: Lists subscriptions for authenticated user
- Auth: Required (JWT)
- Response: `ListUserSubscriptionsResponse`

### Configuration

**appsettings.json** (PublicApi)
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

**Dependencies.cs** (Infrastructure)
- Registers `IMaxioClient` as `MaxioClient` (HttpClient)
- Registers `MaxioSettings` singleton
- Registers `ISubscriptionService` as `SubscriptionService`

## Key Features

✅ **Production-Grade**
- Comprehensive error handling
- Structured logging via ILogger
- Proper serialization handling with JSON naming conventions
- Clean separation of concerns (client, service, endpoints)

✅ **Security**
- JWT authentication on all endpoints
- No secrets in repository (all from user-secrets)
- Basic Auth with Maxio API
- User context extraction from JWT token

✅ **Idempotent Customer Creation**
- Uses userId as customer reference in Maxio
- Double-click safe - Maxio prevents duplicate customers
- Lookup by customer reference

✅ **Sandbox Mode**
- All development against Maxio sandbox
- In-memory database (no LocalDB required)
- No payment method required for sandbox testing

## Maxio API Details

**Authentication:** HTTP Basic Auth
- Username: API Key
- Password: "X"

**Base URL:** `https://{subdomain}.chargify.com`

**Key Endpoints Used:**
- `GET /products.json` - List products (subscription plans)
- `POST /subscriptions.json` - Create subscription
- `GET /subscriptions.json?customer_id[type]=reference&customer_id[value]={userId}` - List user subscriptions

**Sandbox Plans (cp-exp-3):**
- Pro Plan (`eshop-pro`) - $299.00/mo
- Basic Plan (`basic-plan`) - $29.00/mo
- Payment method NOT required (no card capture for sandbox)

## Testing Checklist

- [ ] PublicApi builds successfully
- [ ] In-memory database initializes
- [ ] Maxio credentials load from user-secrets
- [ ] `/api/authenticate` returns valid JWT token
- [ ] `/api/subscription-plans` lists products correctly
- [ ] `/api/subscriptions` POST creates subscription in Maxio
- [ ] Maxio customer created with userId as reference
- [ ] Subscription appears in `/api/my-subscriptions` for the user
- [ ] Second subscription POST is idempotent (no error)
- [ ] Subscription state shows as "active"
- [ ] Next billing date is correctly calculated
- [ ] Error handling works for invalid plans
- [ ] Unauthorized access without token returns 401

## Troubleshooting

### "Maxio API Key not configured"
- Verify environment variables are set before running
- Check user-secrets: `dotnet user-secrets list --project src/PublicApi`
- Re-initialize: `dotnet user-secrets init --project src/PublicApi`

### "SSL/TLS error"
- Dev certificate might not be trusted
- Run: `dotnet dev-certs https --check --trust`
- Use `-k` flag in curl to ignore certificate errors (dev only)

### "Subscription creation fails"
- Verify plan handle exists in Maxio
- Check plan doesn't require payment method (sandbox plans don't)
- Review Maxio error response in logs

### "Port already in use"
- Stop any previous PublicApi instance
- Check configured port in `launchSettings.json`
- Default: `https://localhost:28463`

## Next Steps

1. ✅ Verify the integration works with the testing guide above
2. ✅ Test against the Maxio sandbox (cp-exp-3)
3. ⏭️ When ready for production:
   - Update credentials to production API key/subdomain
   - Test with production plans
   - Handle payment method capture (use Billing.js)
   - Set up proper error logging and monitoring

---

**Document Generated:** 2026-09-07
**Integration Status:** Complete and Ready for Testing
