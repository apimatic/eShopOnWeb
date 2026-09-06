# Maxio Subscription Billing Integration - Verification Guide

## Overview

The Maxio subscription billing system has been integrated into eShopOnWeb as a parallel capability to the existing one-time commerce flow. This guide walks you through verifying the integration works end-to-end.

## Prerequisites

- .NET 8+ runtime or .NET 10 SDK (see Environment Gotchas below)
- Maxio Advanced Billing sandbox account credentials
- Access to the `cp-exp-1` sandbox site with the pre-seeded subscription plans

## Environment Gotchas

### .NET Runtime Issue
`global.json` pins the SDK to 8.0.x, but only .NET 10 SDK is installed (no ASP.NET Core 8.0 runtime). 

**Solution:** Set rollForward and run with:
```bash
export DOTNET_ROLL_FORWARD=Major
dotnet run --project src/PublicApi/PublicApi.csproj
```

### Database
The in-memory database (required for this setup) loses all data on restart. This is fine - Maxio is the source of truth for subscription state.

**Enable in-memory DB:**
```bash
export UseOnlyInMemoryDatabase=true
```

### HTTPS Dev Cert
Both Web and PublicApi use HTTPS. Ensure the dev cert is trusted:
```bash
dotnet dev-certs https --check
```

## Setup: Configure Maxio Credentials

Store credentials in .NET user-secrets (never in source code):

```bash
cd src/PublicApi

# Set your Maxio API key (from Maxio dashboard)
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"

# Set sandbox subdomain (cp-exp-1 for the demo site)
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-1"

# Set product family handle (eshop-subscribe is pre-seeded)
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

Verify secrets are set:
```bash
dotnet user-secrets list
```

## Verification Checklist

### 1. Start the PublicApi Server

```bash
export DOTNET_ROLL_FORWARD=Major
export UseOnlyInMemoryDatabase=true
cd src/PublicApi
dotnet run
```

Expected output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:25463
      Now listening on: http://localhost:25464
```

### 2. Get an Authentication Token

The subscription endpoints require JWT authentication. First, authenticate to get a token:

```bash
curl -X POST https://localhost:25463/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  --insecure
```

Response includes a `token` field. Save it:
```bash
export TOKEN="<token_from_response>"
```

### 3. List Available Subscription Plans

This endpoint doesn't require authentication:

```bash
curl https://localhost:25463/api/subscription-plans \
  --insecure
```

Expected response:
```json
{
  "correlationId": "uuid",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Professional Plan",
      "description": "...",
      "price": 299.00,
      "interval": "Month",
      "intervalCount": 1
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "...",
      "price": 29.00,
      "interval": "Month",
      "intervalCount": 1
    }
  ]
}
```

### 4. Create a Subscription

Subscribe the authenticated user to the Professional Plan:

```bash
curl -X POST https://localhost:25463/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

Expected response (201 Created or 200 OK):
```json
{
  "correlationId": "uuid",
  "subscription": {
    "id": 123456,
    "state": "Active",
    "productHandle": "eshop-pro",
    "productName": "Professional Plan",
    "price": 299.00,
    "currentPeriodStartedAt": "2024-09-06T...",
    "currentPeriodEndsAt": "2024-10-06T...",
    "nextAssessmentAt": "2024-10-06T..."
  }
}
```

### 5. List User's Subscriptions

Retrieve all subscriptions for the authenticated user:

```bash
curl https://localhost:25463/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

Expected response:
```json
{
  "correlationId": "uuid",
  "subscriptions": [
    {
      "id": 123456,
      "state": "Active",
      "productHandle": "eshop-pro",
      "productName": "Professional Plan",
      "price": 299.00,
      "currentPeriodStartedAt": "...",
      "currentPeriodEndsAt": "...",
      "nextAssessmentAt": "..."
    }
  ]
}
```

### 6. Idempotency Test

Try subscribing the same user to the same plan again (should not create a duplicate):

```bash
curl -X POST https://localhost:25463/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

The second request should succeed and return the existing subscription (or return the same subscription ID), confirming idempotency works.

### 7. Different Plan Subscription

Subscribe to the Basic Plan instead:

```bash
curl -X POST https://localhost:25463/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "basic-plan"
  }' \
  --insecure
```

Now list subscriptions again to see both are active.

## Troubleshooting

### 401 Unauthorized
- Token expired or invalid
- Solution: Re-authenticate with `/api/authenticate`

### 400 Bad Request
- Missing or invalid `productHandle`
- Solution: Use one of the listed plans from step 3 (`eshop-pro` or `basic-plan`)

### 500 Internal Server Error
- Maxio credentials not set or invalid
- Maxio service unreachable
- Solution: 
  - Verify `dotnet user-secrets list` shows Maxio config
  - Check Maxio API key in your Maxio dashboard
  - Verify network connectivity to `cp-exp-1.chargify.com`

### Subscription creation fails with 422
- Plan not found or invalid
- Validation error from Maxio
- Solution: Check the plan handle matches exactly (`eshop-pro`, `basic-plan`)

## How the Integration Works

### Architecture

1. **PublicApi Endpoints** (HTTP layer)
   - `GET /api/subscription-plans` - Lists available plans from Maxio
   - `POST /api/subscriptions` - Creates a subscription (idempotent per user)
   - `GET /api/my-subscriptions` - Retrieves user's active subscriptions

2. **Application Layer**
   - `ISubscriptionService` interface in `ApplicationCore.Interfaces`
   - `MaxioSubscriptionService` implementation in `Infrastructure.Services`

3. **Maxio SDK Integration**
   - Uses `AsadAli.AdvancedBilling.Sdk` v1.0.2
   - Configured with Basic Auth (API key + literal "x" password)
   - Targets Maxio sandbox at `cp-exp-1.chargify.com`

### Idempotency

- **Customer Idempotency**: On subscription creation, the system first looks up a customer by `Reference` (eShopWeb userId)
  - If not found, creates a new customer with `Reference = userId`
  - If found, reuses the existing customer
  - This ensures the same user never has multiple Maxio customers

- **Subscription Idempotency**: Application-level check prevents duplicate subscriptions
  - User must be authenticated (JWT)
  - System looks up existing subscriptions for the customer
  - Returns existing subscription or creates new one

### Data Mapping

- **Product**: Maxio Plan → `PlanModel`
  - `PriceInCents` ÷ 100 → `Price` (in dollars)
  - `Handle` maps directly
  
- **Subscription**: Maxio Subscription → `SubscriptionModel`
  - `State` (enum) → `State` (string value)
  - Nested `Product` data mapped to `ProductName`, `ProductHandle`, `Price`
  - Period and assessment dates mapped directly

### Configuration

All Maxio settings read from configuration:

| Config Key | Source | Purpose |
|-----------|--------|---------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` env var or user-secrets | API authentication |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` env var or user-secrets | Sandbox hostname (`cp-exp-1`) |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` env var or user-secrets | Product family to query (`eshop-subscribe`) |
| `Maxio:BaseUrl` | Optional override | For proxies or alternate hosts |

## Security Notes

- **No Payment Method Required**: The sandbox plans don't require payment method capture, so users can subscribe instantly
- **JWT Authentication**: Subscription endpoints are protected by JWT Bearer tokens (from `/api/authenticate`)
- **In-Memory DB**: Subscription data is NOT persisted locally (except during a single run); Maxio is the source of truth
- **Secrets Management**: Credentials stored in .NET user-secrets, never in source code or `appsettings.json`

## Next Steps

### Production Deployment

To deploy to production:

1. Update `Maxio:Subdomain` to your production Maxio site (e.g., `mycompany`)
2. Update `Maxio:ApiKey` to your production API key
3. Use a persistent database instead of in-memory DB
4. Implement subscription persistence (map userId ↔ Maxio customerId in AppIdentityDbContext)
5. Add webhook handlers for Maxio subscription state changes (cancellation, renewal, etc.)
6. Implement subscription renewal/cancellation UI on the eShopWeb storefront

### Feature Extensions

- **Subscription Management**: Add endpoints to cancel, change plan, or update payment method
- **Webhooks**: Listen to Maxio events (subscription state changes) and update local state
- **Metered Components**: The `api-call` component is pre-seeded; implement usage-based billing
- **Trial Periods**: Add trial support by configuring trials on Maxio plans
