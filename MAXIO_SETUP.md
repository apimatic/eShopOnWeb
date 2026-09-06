# Maxio Subscription Billing Integration Setup

## Overview

This document describes how to set up and verify the Maxio Advanced Billing integration for eShopOnWeb subscriptions.

## Environment Configuration

### Step 1: Set Maxio Credentials

The integration requires three environment variables to be set. You can use .NET User Secrets for development:

```bash
# Set from environment variables or configure via User Secrets
dotnet user-secrets set "Maxio:ApiKey" "<your_api_key>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe" --project src/PublicApi
```

**Important**: Never commit actual API keys to the repository. The values go in `.gitignore`'d user-secrets files only.

### Step 2: Configure In-Memory Database (Optional but Recommended for Testing)

Since LocalDB is not available, use in-memory database:

```bash
# When running PublicApi
export UseOnlyInMemoryDatabase=true
```

Or add to `appsettings.Development.json`:
```json
{
  "UseOnlyInMemoryDatabase": true
}
```

## Running the Integration

### Step 1: Start PublicApi

```bash
cd src/PublicApi
dotnet run --urls="https://localhost:24923"
```

Expected output should show the API starting on https://localhost:24923/

### Step 2: Create a Test User

First, authenticate to get a token:

```bash
curl -X POST https://localhost:24923/api/authenticate \
  -H "Content-Type: application/json" \
  -k \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@123"
  }'
```

You should get a response like:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "result": true,
  "username": "demouser@microsoft.com"
}
```

Save the token for the next steps.

## API Endpoints - Step-by-Step Verification

### Endpoint 1: List Available Subscription Plans

**Endpoint**: `GET /api/subscription-plans`

No authentication required for this endpoint.

```bash
curl -X GET https://localhost:24923/api/subscription-plans \
  -H "Content-Type: application/json" \
  -k
```

**Expected Response**:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "priceFormatted": "$299.00",
      "description": "Professional plan with advanced features"
    },
    {
      "id": 7126958,
      "handle": "eshop-basic",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "priceFormatted": "$29.00",
      "description": "Basic subscription plan"
    }
  ]
}
```

### Endpoint 2: Subscribe to a Plan

**Endpoint**: `POST /api/subscriptions`

Requires JWT authentication (Bearer token).

```bash
BEARER_TOKEN="<token_from_step_2>"

curl -X POST https://localhost:24923/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $BEARER_TOKEN" \
  -k \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

**Expected Response** (201 Created):
```json
{
  "subscriptionId": 123456,
  "customerId": 789012,
  "state": "active",
  "nextBillingAt": "2026-10-06T00:00:00Z",
  "message": "Subscription created successfully. Next billing date: 2026-10-06"
}
```

**Key Points**:
- Creates Maxio customer (idempotent - reuses if already exists)
- Links the user to their Maxio customer via user ID as reference
- Creates subscription with specified product handle
- Returns subscription state and next billing date

### Endpoint 3: View User's Subscriptions

**Endpoint**: `GET /api/my-subscriptions`

Requires JWT authentication (Bearer token).

```bash
BEARER_TOKEN="<token_from_step_2>"

curl -X GET https://localhost:24923/api/my-subscriptions \
  -H "Authorization: Bearer $BEARER_TOKEN" \
  -k
```

**Expected Response**:
```json
{
  "customerId": 789012,
  "email": "demouser@microsoft.com",
  "subscriptions": [
    {
      "id": 123456,
      "state": "active",
      "productHandle": "eshop-pro",
      "nextBillingAt": "2026-10-06T00:00:00Z",
      "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
      "isActive": true
    }
  ]
}
```

## Verifying Idempotency

The subscription system uses the authenticated user's ID as the Maxio customer reference. This means:

1. **First subscription creation**: Creates new Maxio customer and subscription
2. **Subsequent subscription creations**: Reuses existing Maxio customer, creates new subscription

To verify idempotency:

```bash
# Test 1: Create subscription for eshop-pro
curl -X POST https://localhost:24923/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $BEARER_TOKEN" \
  -k \
  -d '{"productHandle": "eshop-pro"}'

# Note the customerId returned

# Test 2: Create another subscription (same user)
curl -X POST https://localhost:24923/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $BEARER_TOKEN" \
  -k \
  -d '{"productHandle": "eshop-basic"}'

# Should return same customerId, but different subscriptionId
```

## Troubleshooting

### 401 Unauthorized on /api/subscriptions

Ensure you're including the Bearer token:
```bash
-H "Authorization: Bearer <your_token>"
```

### 400 Bad Request on subscription creation

Common causes:
- Invalid product handle (must be "eshop-pro", "eshop-basic", etc.)
- Maxio API key not set or invalid
- Wrong Maxio sandbox subdomain

Check the response body for detailed error message.

### No subscriptions returned

If you've just created a subscription, refresh the page. In-memory database keeps data in-process only.

### Connection refused to Maxio

Verify:
- Maxio credentials are set correctly
- Network access to Maxio sandbox is available
- Subdomain is correct (cp-exp-2 for testing)

## Database Notes

**Important**: This implementation uses in-memory database for development. All data is lost when the application restarts. For production:

1. Update connection strings in `appsettings.json`
2. Run EF Core migrations to create tables
3. Update `Infrastructure/Dependencies.cs` to use SQL Server instead of in-memory

## Architecture Notes

- **Service Layer**: `IMaxioService` in `ApplicationCore/Interfaces/`
- **Implementation**: `MaxioService` in `Infrastructure/Services/`
- **Endpoints**: Three minimal API endpoints in `PublicApi/SubscriptionEndpoints/`
- **Configuration**: Settings in `Maxio:` section of appsettings
- **Authentication**: Uses existing JWT bearer token authentication from PublicApi
- **User Association**: eShopOnWeb user ID stored as Maxio customer reference

## Security

- API keys stored in user secrets, never in repository
- JWT authentication required for subscribe/list-my-subscriptions
- All Maxio API calls use HTTP Basic auth (key:x)
- HTTPS required for production
