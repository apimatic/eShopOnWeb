# Maxio Subscription Billing Integration Setup & Verification Guide

## Overview
This guide walks through setting up and verifying the new Maxio subscription billing endpoints in eShopOnWeb's PublicApi.

## Prerequisites
- .NET 8.0 SDK (configured with `rollForward: latestMajor` if only .NET 10 SDK is installed)
- Maxio sandbox account with credentials for site `cp-exp-3`
- The following environment variables will be loaded from user secrets or environment:
  - `MAXIO_API_KEY` → `Maxio:ApiKey`
  - `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
  - `MAXIO_ENVIRONMENT` → (informational)
  - `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`

## Database Setup
The integration uses in-memory database by default (no SQL Server LocalDB required).

To run with in-memory database:
```bash
export UseOnlyInMemoryDatabase=true
# or on Windows CMD:
set UseOnlyInMemoryDatabase=true
```

## Step 1: Configure Maxio Credentials

### Option A: Using .NET User Secrets (Recommended)
```bash
cd src/PublicApi

# Set the Maxio credentials
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key-from-maxio>"
dotnet user-secrets set "Maxio:Subdomain" "<your-sandbox-subdomain>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:BaseUrl" ""  # Optional, leave empty for auto-derived URL
```

### Option B: Environment Variables
```bash
export MAXIO_API_KEY="<your-api-key>"
export MAXIO_SITE_SUBDOMAIN="<your-subdomain>"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

## Step 2: Start the PublicApi Service

```bash
cd src/PublicApi
dotnet run
```

The service will start on `https://localhost:24383` (or check `appsettings.json` for the configured port).

## Step 3: Verify Endpoints Are Registered

Visit the Swagger UI at:
```
https://localhost:24383/swagger/index.html
```

You should see three new endpoints under **SubscriptionEndpoints**:
- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

## Step 4: Test the Integration

### 4a. Get Authentication Token

```bash
curl -X POST https://localhost:24383/api/authenticate \
  -H "Content-Type: application/json" \
  -k \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

**Response:**
```json
{
  "result": true,
  "token": "eyJ0eXAi...",
  "username": "demouser@microsoft.com",
  "correlationId": "..."
}
```

**Note:** Save the `token` value for subsequent requests.

### 4b. List Available Subscription Plans

```bash
curl -X GET https://localhost:24383/api/subscription-plans \
  -H "Accept: application/json" \
  -k
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": "7126957",
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription plan",
      "price": 299.00,
      "currencyCode": "USD",
      "billingCycle": "monthly",
      "trialDays": null
    },
    {
      "id": "7126958",
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription plan",
      "price": 29.00,
      "currencyCode": "USD",
      "billingCycle": "monthly",
      "trialDays": null
    }
  ],
  "correlationId": "..."
}
```

### 4c. Create a Subscription

```bash
curl -X POST https://localhost:24383/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -k \
  -d '{
    "planHandle": "eshop-pro"
  }'
```

**Expected Response (201 Created):**
```json
{
  "subscription": {
    "id": "12345678",
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "price": 299.00,
    "status": "active",
    "nextBillingDate": "2024-10-06T00:00:00Z"
  },
  "correlationId": "..."
}
```

### 4d. Retrieve User's Subscriptions

```bash
curl -X GET https://localhost:24383/api/my-subscriptions \
  -H "Accept: application/json" \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": "12345678",
      "planHandle": "eshop-pro",
      "planName": "Pro Plan",
      "price": 299.00,
      "status": "active",
      "nextBillingDate": "2024-10-06T00:00:00Z"
    }
  ],
  "correlationId": "..."
}
```

## Step 5: Verify Idempotency

Call the subscribe endpoint **twice with the same user and plan handle**:

```bash
# First call
curl -X POST https://localhost:24383/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -k \
  -d '{"planHandle": "eshop-pro"}'

# Second call (same user, same plan)
curl -X POST https://localhost:24383/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -k \
  -d '{"planHandle": "eshop-pro"}'
```

**Expected:** Both calls succeed. The second call reuses the existing Maxio customer (idempotent via the `reference` field) and either reuses the existing subscription or creates a new one, depending on Maxio's duplicate handling.

## Architecture Overview

### Key Files
- `MaxioConfiguration.cs` — Configuration schema for Maxio settings
- `Services/MaxioService.cs` — Core Maxio API integration
  - `GetSubscriptionPlansAsync()` — Lists plans via `/products.json`
  - `CreateSubscriptionAsync(userId, planHandle)` — Creates subscription with idempotent customer creation
  - `GetUserSubscriptionsAsync(userId)` — Retrieves user's subscriptions
  - Automatic customer creation with `reference={userId}` for idempotency
  - Customer lookup via `GET /customers/lookup.json?reference={ref}`

- `SubscriptionEndpoints/` — Three minimal API endpoints
  - `SubscriptionPlansListEndpoint` — GET /api/subscription-plans (public)
  - `SubscribeEndpoint` — POST /api/subscriptions (JWT-required)
  - `MySubscriptionsEndpoint` — GET /api/my-subscriptions (JWT-required)

### Integration Flow
1. User calls `POST /api/subscriptions` with plan handle
2. Endpoint extracts userId from JWT claims
3. MaxioService looks up Maxio customer by `reference={userId}`
4. If customer doesn't exist, creates one (idempotent via reference field)
5. Creates subscription using customer ID and plan handle
6. Returns subscription details to caller

### Maxio API Mapping
| Operation | Maxio Endpoint | Auth |
|-----------|---|---|
| List Plans | `GET /products.json` | Basic |
| Create Customer | `POST /customers.json` | Basic |
| Lookup Customer | `GET /customers/lookup.json?reference={ref}` | Basic |
| Create Subscription | `POST /subscriptions.json` | Basic |
| List Customer Subscriptions | `GET /customers/{id}/subscriptions.json` | Basic |

Authentication: HTTP Basic with `Authorization: Basic base64(apikey:x)`

## Troubleshooting

### "Maxio configuration not found"
Ensure user secrets or environment variables are set correctly:
```bash
cd src/PublicApi
dotnet user-secrets list
```

### "Failed to create customer: 422"
The reference field must be unique per Maxio site. If you're retesting, either:
- Use a different user ID (e.g., add a timestamp suffix)
- Clear the reference by using the Maxio admin panel
- The service automatically retries customer lookup on 422, so the second call should work

### Authorization errors on POST/GET my-subscriptions
Ensure you're passing the bearer token:
```bash
-H "Authorization: Bearer <token>"
```

The token comes from the `/api/authenticate` endpoint.

### Port conflicts
If port 24383 is already in use, check `src/PublicApi/Properties/launchSettings.json` and adjust the port, or stop any existing PublicApi process:
```bash
# Kill existing .NET processes
pkill -f dotnet
```

## Notes for Production Deployment

1. **Secrets Management**: Load Maxio credentials from secure vaults (Azure Key Vault, AWS Secrets Manager, etc.), not from appsettings.json
2. **Error Handling**: Log subscription failures for monitoring; consider implementing retry logic with exponential backoff
3. **Webhooks**: Implement Maxio webhooks to sync subscription state changes (not yet implemented in this version)
4. **Caching**: Consider caching subscription plans (they change infrequently) to reduce API calls
5. **Rate Limiting**: Maxio has rate limits; consider implementing circuit breakers
6. **Audit Trail**: Track subscription lifecycle events (created, updated, canceled) for compliance

---

For official Maxio API documentation, see: https://developers.maxio.com/
