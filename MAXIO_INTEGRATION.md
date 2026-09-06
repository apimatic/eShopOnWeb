# Maxio Subscription Billing Integration

This document provides setup and verification instructions for the Maxio subscription billing integration in eShopOnWeb.

## Overview

The Maxio integration adds subscription/recurring billing capabilities to eShopOnWeb as a parallel feature alongside the existing one-time cart/checkout flow. Subscriptions are managed through:

- **Maxio Sandbox**: The subscription and customer data source (https://[subdomain].chargify.com)
- **eShopOnWeb Database**: Local storage of subscription metadata and user ↔ Maxio customer mappings
- **PublicApi Endpoints**: JWT-authenticated HTTP endpoints for subscription operations

## Architecture

### Data Flow

1. **Authenticated User** → POST `/api/subscriptions` with `productHandle`
2. **CreateSubscriptionEndpoint** → Creates/retrieves Maxio customer (idempotent via reference)
3. **Maxio API** → Creates subscription, returns subscription ID
4. **CatalogContext** → Stores subscription metadata locally (user ID, Maxio IDs, plan details)
5. **Response** → Returns subscription confirmation with next billing date

### Entities

- **Subscription** (ApplicationCore.Entities.SubscriptionAggregate)
  - UserId: eShopOnWeb user ID
  - MaxioCustomerId: Maxio customer ID (stable across operations)
  - MaxioSubscriptionId: Maxio subscription ID (stable)
  - ProductHandle, PlanName, PriceInDollars
  - BillingState, CurrentPeriodEndsAt, NextBillingAt
  - CreatedAt, UpdatedAt

## Setup Instructions

### 1. Maxio Sandbox Account

You need a Maxio sandbox account. If you don't have one:

1. Visit https://app.chargify.com/signup/maxio-billing-sandbox (US) or https://app.ebilling.maxio.com/signup/maxio-billing-sandbox (EU)
2. Sign up and create a sandbox site
3. Navigate to **Settings → Developer API** to get your credentials:
   - **API Key**: Copy the "API Password" (used as username in Basic Auth)
   - **Subdomain**: Your site's subdomain (e.g., `cp-exp-4`)

### 2. Maxio Configuration

The integration reads credentials from environment variables:

```
MAXIO_API_KEY               → Maxio:ApiKey
MAXIO_SITE_SUBDOMAIN        → Maxio:Subdomain
MAXIO_ENVIRONMENT           → Maxio:Environment (default: "US")
MAXIO_DEFAULT_PRODUCT_FAMILY → Maxio:ProductFamilyHandle (must exist in Maxio)
```

**Option A: launchSettings.json** (Development Only)

Edit `src/PublicApi/Properties/launchSettings.json`:

```json
"PublicApi": {
  "environmentVariables": {
    "MAXIO_API_KEY": "<your-api-key>",
    "MAXIO_SITE_SUBDOMAIN": "<your-subdomain>",
    "MAXIO_ENVIRONMENT": "US",
    "MAXIO_DEFAULT_PRODUCT_FAMILY": "<product-family-handle>"
  }
}
```

**Option B: User Secrets** (Recommended for Development)

```bash
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "<your-subdomain>"
dotnet user-secrets set "Maxio:Environment" "US"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<product-family-handle>"
```

**Option C: Environment Variables** (Production)

Set via your deployment platform's environment configuration.

### 3. Database Migration

The integration uses the in-memory database by default for development:

```bash
# This is already set in launchSettings.json
UseOnlyInMemoryDatabase=true
```

For SQL Server with LocalDB, the migration is already created:

```bash
dotnet ef database update --context CatalogContext -p src/Infrastructure -s src/PublicApi
dotnet ef database update --context AppIdentityDbContext -p src/Infrastructure -s src/PublicApi
```

## Endpoints

All endpoints require JWT authentication (Bearer token from `/api/authenticate`).

### GET /api/subscription-plans

List all available subscription plans for the configured product family.

**Response:**
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription",
      "priceInDollars": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### POST /api/subscriptions

Create a subscription for the authenticated user. Idempotent: calling this multiple times with the same user and plan returns the same subscription.

**Request:**
```json
{
  "productHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "subscriptionId": 123456,
  "customerId": 789012,
  "productHandle": "eshop-pro",
  "status": "active"
}
```

### GET /api/my-subscriptions

Get all active subscriptions for the authenticated user.

**Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 123456,
      "productHandle": "eshop-pro",
      "planName": "Pro Plan",
      "priceInDollars": 299.00,
      "state": "active",
      "nextBillingAt": "2024-10-06T00:00:00Z"
    }
  ]
}
```

## Verification Guide

### 1. Build and Run

```bash
cd repo
dotnet build eShopOnWeb.sln

# Run PublicApi
cd src/PublicApi
dotnet run

# In another terminal, run Web (if needed for testing)
cd ../Web
dotnet run
```

The PublicApi will start on `https://localhost:25363`.

### 2. Get JWT Token

```bash
curl -X POST "https://localhost:25363/api/authenticate" \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

Expected response:
```json
{
  "result": true,
  "token": "eyJ0eXAi..."
}
```

Copy the token for subsequent requests.

### 3. Test Subscription Plans Endpoint

```bash
curl -X GET "https://localhost:25363/api/subscription-plans" \
  -H "Authorization: Bearer <your-token>" \
  --insecure
```

Expected: List of available plans from your Maxio product family.

### 4. Test Create Subscription

```bash
curl -X POST "https://localhost:25363/api/subscriptions" \
  -H "Authorization: Bearer <your-token>" \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{"productHandle":"eshop-pro"}'
```

Expected response:
```json
{
  "subscriptionId": <id>,
  "customerId": <id>,
  "productHandle": "eshop-pro",
  "status": "active"
}
```

First call creates a new Maxio customer (with reference `eshop-{userId}`) and subscription.
Subsequent calls return the same subscription (idempotent).

### 5. Test Get User Subscriptions

```bash
curl -X GET "https://localhost:25363/api/my-subscriptions" \
  -H "Authorization: Bearer <your-token>" \
  --insecure
```

Expected: Array containing the subscription created above.

### 6. Verify in Maxio Dashboard

Log in to your Maxio sandbox at `https://<subdomain>.chargify.com/settings/customers`:

1. You should see a new customer with reference `eshop-{userId}`
2. That customer should have an active subscription to your product
3. The subscription state should be "Active"
4. The next billing date should be visible

## Troubleshooting

### Issue: "Maxio:ApiKey is required"

The API key environment variable is not set. Verify:
- `MAXIO_API_KEY` environment variable is set
- Or `Maxio:ApiKey` is in user-secrets
- Or `launchSettings.json` is correctly configured

### Issue: 404 Not Found on Maxio API

- Verify subdomain is correct
- Verify product family handle exists in Maxio
- Check that the product has pricing configured

### Issue: "The type or namespace name 'Maxio' could not be found"

This was resolved by using HTTP client instead of the SDK. If you see this error, rebuild:
```bash
dotnet clean
dotnet build
```

### Issue: Database errors with in-memory database

The in-memory database loses all data on restart. This is expected and by design for development. To use SQL Server:

1. Remove `UseOnlyInMemoryDatabase=true` from launchSettings.json
2. Ensure LocalDB is installed or use a SQL Server instance
3. Run migrations
4. Update connection strings in `appsettings.json`

### Issue: HTTP 401 Unauthorized from Maxio API

Verify your API key is correct. The Maxio API expects:
- Basic Auth header with API key as username, empty password
- Content-Type: application/json

This is handled automatically by `MaxioService`.

## Design Decisions

### HTTP Client vs SDK

The integration uses `System.Net.Http.HttpClient` directly instead of the `Maxio.AdvancedBillingSdk` package because:
- Simpler dependency management
- Direct control over serialization (System.Text.Json)
- No SDK compatibility issues across .NET versions
- Smaller surface area for auditing

### Idempotent Customer Creation

Customers are created with a `reference` field set to `eshop-{userId}`. This allows idempotent lookups:

```csharp
// First call: Customer doesn't exist, create it
var customerId = await service.CreateOrGetSubscriptionAsync(...);

// Second call with same user: Looks up by reference, reuses customer
var customerId = await service.CreateOrGetSubscriptionAsync(...);
// Returns same customerId, doesn't create duplicate
```

### Local Subscription Storage

Subscriptions are stored locally in the `Subscriptions` table because:
- Allows quick lookups without hitting Maxio API every time
- Stores derived data (price in dollars, friendly plan name)
- Provides audit trail of user subscription history
- Survives Maxio data resets (the reference mapping persists locally)

## Sandbox Entities (Pre-seeded)

The Maxio sandbox comes with these entities (handles are stable, IDs may change on re-seed):

| Entity | Handle | Sample ID |
|--------|--------|-----------|
| Product Family | `eshop-subscribe` | 3023074 |
| Pro Plan | `eshop-pro` | 7126957 |
| Basic Plan | `basic-plan` | 7126958 |
| Metered Component | `api-call` | 3057195 |

Use these handles in `MAXIO_DEFAULT_PRODUCT_FAMILY` and subscription requests.

## Security Notes

- **Never commit secrets to git**: Use environment variables, user-secrets, or secure vaults
- **API Key format**: The key is used as the username in HTTP Basic Auth, with an empty password
- **HTTPS only**: All Maxio API calls use HTTPS
- **JWT validation**: The endpoints require valid JWT bearer tokens from `/api/authenticate`
- **No payment methods required**: Sandbox plans don't require credit cards (payment_method_not_required = true)

## Future Enhancements

Possible improvements for production:

1. **Webhook handling**: Listen to Maxio webhooks for subscription state changes (renewed, failed, cancelled)
2. **Payment method management**: Add endpoints to manage saved payment methods
3. **Subscription amendments**: Allow plan upgrades/downgrades
4. **Cancellation**: Add subscription cancellation endpoint
5. **Invoice retrieval**: Expose user's invoices and payment history
6. **Metered components**: Track and charge for usage-based metrics

## References

- Maxio API Docs: https://developers.maxio.com/http/getting-started/how-to-get-started
- Maxio Help Center: https://docs.maxio.com/
- eShopOnWeb Reference: https://github.com/dotnet-architecture/eShopOnWeb
