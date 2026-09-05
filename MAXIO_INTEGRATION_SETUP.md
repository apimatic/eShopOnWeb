# Maxio Subscription Billing Integration - Setup & Verification Guide

## Overview

This document describes how to set up and verify the Maxio subscription billing integration in eShopOnWeb.

## Prerequisites

- .NET SDK (must enable rollForward: latestMajor)
- Environment variables with Maxio sandbox credentials
- No SQL Server LocalDB required (uses in-memory database)

## Environment Setup

### 1. Environment Variables Required

Set these environment variables (or provide them to user-secrets):

```
MAXIO_API_KEY=<your-sandbox-api-key>
MAXIO_SITE_SUBDOMAIN=<your-sandbox-subdomain> # e.g., "cp-exp-4"
MAXIO_ENVIRONMENT=sandbox
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### 2. Load Secrets into .NET User-Secrets

From the `src/PublicApi` directory:

```bash
dotnet user-secrets set "MAXIO_API_KEY" "<your-api-key>"
dotnet user-secrets set "MAXIO_SITE_SUBDOMAIN" "<your-subdomain>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

Alternatively, configure in `appsettings.Development.json`:

```json
{
  "Maxio": {
    "ApiKey": "<api-key>",
    "Subdomain": "<subdomain>",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": null
  }
}
```

### 3. SDK/Runtime Configuration

Handle SDK version mismatch:

```bash
# Option 1: Use rollForward (simpler)
set DOTNET_ROLL_FORWARD=Major
dotnet run

# Option 2: Install ASP.NET Core 8.0 runtime (if preferred)
# Download from: https://dotnet.microsoft.com/download/dotnet/8.0
```

## Running the Application

### PublicApi Project

```bash
cd src/PublicApi

# With rollForward and in-memory database
$env:DOTNET_ROLL_FORWARD="Major"
$env:UseOnlyInMemoryDatabase="true"
dotnet run

# The API will be available at:
# https://localhost:24563/swagger
```

The application will automatically use the in-memory database and log whether Maxio billing is configured.

## Verification Steps

### Step 1: Verify Build

```bash
dotnet build src/PublicApi/PublicApi.csproj -c Release
```

Expected: Build succeeds with no errors (warnings about System.Text.Json vulnerabilities are OK).

### Step 2: Get Authentication Token

First, authenticate with the PublicApi to get a JWT token:

```bash
# POST to /api/auth/authenticate
curl -X POST "https://localhost:24563/api/auth/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  -k

# Response will include a "token" field. Copy it.
# Example: {"token":"eyJhbGciOiJIUzI1NiIs..."}
```

Save the token in a variable:
```bash
$TOKEN = "eyJhbGciOiJIUzI1NiIs..."
```

### Step 3: List Available Subscription Plans

```bash
# GET /api/subscription-plans
curl -X GET "https://localhost:24563/api/subscription-plans" \
  -H "Authorization: Bearer $TOKEN" \
  -k
```

Expected response (200 OK):
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "$299.00/mo",
      "price": 299.00,
      "billingPeriod": "1 month(s)"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "$29.00/mo",
      "price": 29.00,
      "billingPeriod": "1 month(s)"
    }
  ],
  "correlationId": "..."
}
```

### Step 4: Create a Subscription

```bash
# POST /api/subscriptions
curl -X POST "https://localhost:24563/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  -k
```

Expected response (201 Created):
```json
{
  "subscription": {
    "id": <subscription-id>,
    "state": "active",
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "price": 299.00,
    "billingPeriod": "1 month(s)",
    "nextBillingAt": "2026-10-06T..."
  },
  "correlationId": "..."
}
```

**Important**: The subscription state should be "active" and `nextBillingAt` should be populated.

### Step 5: List User Subscriptions

```bash
# GET /api/my-subscriptions
curl -X GET "https://localhost:24563/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -k
```

Expected response (200 OK):
```json
{
  "subscriptions": [
    {
      "id": <subscription-id>,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "price": 299.00,
      "billingPeriod": "1 month(s)",
      "nextBillingAt": "2026-10-06T..."
    }
  ],
  "correlationId": "..."
}
```

### Step 6: Create Second Subscription (Idempotency Check)

Create another subscription with the same user and **different plan**:

```bash
curl -X POST "https://localhost:24563/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "basic-plan"
  }' \
  -k
```

Expected behavior:
- Returns 201 Created with new subscription
- Same Maxio customer ID is reused (no duplicate customer created)
- Verify in `/api/my-subscriptions` that BOTH subscriptions appear

## Key Features Verified

✅ **Idempotent Customer Creation** - Same user always maps to same Maxio customer
✅ **JWT Authentication** - Endpoints protected by authorization
✅ **Product Listing** - Plans fetched from Maxio catalog
✅ **Subscription Creation** - Subscriptions created successfully with correct pricing
✅ **User Subscriptions** - Users can view their active subscriptions
✅ **No Database** - Works with in-memory database (data persists within session)
✅ **Billing Information** - Next billing date populated correctly

## Troubleshooting

### "Maxio API credentials not configured"

Verify environment variables are set:
```bash
# Check if variables are available
$env:MAXIO_API_KEY
$env:MAXIO_SITE_SUBDOMAIN
```

Or verify user-secrets:
```bash
cd src/PublicApi
dotnet user-secrets list
```

### 401 Unauthorized on endpoints

- Ensure you have a valid JWT token from `/api/auth/authenticate`
- Bearer token should be in Authorization header
- Check token is not expired

### 422 Unprocessable Entity on subscription creation

- Verify product handle matches exactly (case-sensitive)
- Verify plan exists in Maxio sandbox
- Check Maxio API key and subdomain are correct

### Build fails with SDK version error

Use the rollForward environment variable:
```bash
$env:DOTNET_ROLL_FORWARD="Major"
dotnet build
```

## Database Persistence Note

The in-memory database is designed for testing:
- **Data persists** within a single application session
- **Data is lost** when the application restarts
- This is intentional - allows clean state between test runs
- Customer-to-subscription mappings stored locally enable idempotency within a session

## Architecture Overview

### New Components

**Domain Models** (`src/ApplicationCore/Entities/`):
- `MaxioCustomerMapping` - Maps eShopOnWeb users to Maxio customers

**Services** (`src/ApplicationCore/Services/`):
- `MaxioApiClient` - HTTP communication with Maxio API (with auth, error handling)
- `MaxioBillingService` - Business logic for subscriptions (get/create customer, create subscription, list products)
- `MaxioApiDtos` - Request/response models for Maxio API

**Interfaces** (`src/ApplicationCore/Interfaces/`):
- `IMaxioBillingService` - Public billing service contract

**Endpoints** (`src/PublicApi/SubscriptionEndpoints/`):
- `ListSubscriptionPlansEndpoint` - GET /api/subscription-plans
- `CreateSubscriptionEndpoint` - POST /api/subscriptions
- `ListUserSubscriptionsEndpoint` - GET /api/my-subscriptions

### Integration Points

- **Configuration** - Reads Maxio settings from appsettings/user-secrets
- **DI Container** - MaxioApiClient and MaxioBillingService registered in Program.cs
- **Database** - MaxioCustomerMapping stored in CatalogContext
- **Authentication** - Endpoints require valid JWT token

## Security Considerations

✅ **No Credentials in Repo** - All secrets via environment variables or user-secrets
✅ **Basic Auth with Maxio** - API key passed securely in HTTP Basic Auth header
✅ **JWT for Endpoints** - All subscription endpoints require Bearer token
✅ **Input Validation** - Product handles validated against Maxio
✅ **User Context** - Subscriptions tied to authenticated user identity

## Production Readiness

Before production deployment:

1. **Error Handling**: Review exception handling in MaxioApiClient and MaxioBillingService
2. **Logging**: Ensure adequate observability (correlation IDs, request tracking)
3. **Rate Limiting**: Implement if calling Maxio API in high-volume scenarios
4. **Retry Logic**: Add exponential backoff for transient Maxio API failures
5. **Webhooks**: Consider implementing Maxio webhooks for subscription events
6. **Database**: Replace in-memory database with real persistence (SQL Server/PostgreSQL)
7. **Testing**: Add integration tests with Maxio sandbox
8. **Documentation**: Update API documentation in Swagger

## Next Steps

- Integrate subscription plans into UI
- Add subscription management (cancel, change plan, etc.)
- Implement webhook handlers for subscription lifecycle events
- Set up production Maxio environment
- Plan data migration strategy from test to production
