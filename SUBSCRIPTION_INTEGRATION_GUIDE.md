# Maxio Subscription Billing Integration - Verification Guide

## Setup & Prerequisites

### 1. Environment Configuration

The integration uses the following environment variables (already set):
- `MAXIO_API_KEY` - Maxio sandbox API key
- `MAXIO_SITE_SUBDOMAIN` - Maxio site subdomain (cp-exp-3)
- `MAXIO_ENVIRONMENT` - Maxio environment (US/EU)
- `MAXIO_DEFAULT_PRODUCT_FAMILY` - Product family handle (eshop-subscribe)

Credentials are stored in .NET user-secrets (not in repository files).

### 2. Database Configuration

Due to environment constraints, the solution uses in-memory database:
```bash
USE_IN_MEMORY_DB=true
```

Or configure via launchSettings.json if needed.

### 3. Build & Run

```bash
cd repo
dotnet build eShopOnWeb.sln --configuration Release
cd src/PublicApi
dotnet run
```

The PublicApi service will start on: `https://localhost:5002` (or the configured port)

## Testing the Integration

### Step 1: Authenticate and Get JWT Token

First, create a test user and get a JWT token:

```bash
# Authenticate (login)
curl -X POST https://localhost:5001/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demo@test.com",
    "password": "Pass@word1"
  }' \
  --insecure
```

**Response** (save the `token` value):
```json
{
  "result": true,
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "username": "demo@test.com",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

Export the token for use in subsequent calls:
```bash
TOKEN="<paste-token-here>"
```

### Step 2: List Available Subscription Plans

Get available plans from Maxio:

```bash
curl -X GET https://localhost:5002/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Response** (shows plans from Maxio sandbox):
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "description": "$299.00 per month",
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "priceInCents": 2900,
      "description": "$29.00 per month",
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "correlationId": "..."
}
```

### Step 3: Subscribe to a Plan

Enroll the authenticated user in a subscription:

```bash
curl -X POST https://localhost:5002/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  --insecure
```

**Response** (user now has an active subscription):
```json
{
  "subscription": {
    "id": 12345,
    "customerId": 98765,
    "productId": 7126957,
    "state": "active",
    "currentPeriodEndsAt": "2026-10-07T12:34:56Z",
    "createdAt": "2026-09-07T12:34:56Z",
    "reference": "demo@test.com"
  },
  "correlationId": "..."
}
```

### Step 4: List User's Subscriptions

Retrieve all subscriptions for the authenticated user:

```bash
curl -X GET https://localhost:5002/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Response** (shows all subscriptions for the user):
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "customerId": 98765,
      "productId": 7126957,
      "state": "active",
      "currentPeriodEndsAt": "2026-10-07T12:34:56Z",
      "createdAt": "2026-09-07T12:34:56Z",
      "reference": "demo@test.com"
    }
  ],
  "correlationId": "..."
}
```

## Idempotency & Duplicate Prevention

The subscription enrollment endpoint is **idempotent per user**:

1. **First call** to `/api/subscriptions` with `planHandle="eshop-pro"`:
   - Checks if a Maxio customer exists with the user's ID as `reference`
   - Creates a new customer if needed
   - Creates a new subscription
   - Returns subscription details

2. **Second call** with same `planHandle` for the same user:
   - Finds the existing customer via reference lookup
   - Detects the existing active subscription
   - Returns the existing subscription without creating a duplicate
   - Returns HTTP 201 with the subscription details (same response shape)

**Test idempotency**:
```bash
# First subscription
curl -X POST https://localhost:5002/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}' \
  --insecure

# Second call (should return the same subscription, no duplicate)
curl -X POST https://localhost:5002/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}' \
  --insecure
```

Both should return the **same subscription ID** and **status 201**.

## Architecture & Design

### Endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| GET | `/api/subscription-plans` | JWT | List available plans from Maxio |
| POST | `/api/subscriptions` | JWT | Create subscription (idempotent) |
| GET | `/api/my-subscriptions` | JWT | List user's subscriptions |

### Layers

- **Endpoints** (`SubscriptionEndpoints/`):
  - `ListSubscriptionPlansEndpoint.cs` - GET /api/subscription-plans
  - `CreateSubscriptionEndpoint.cs` - POST /api/subscriptions
  - `ListMySubscriptionsEndpoint.cs` - GET /api/my-subscriptions

- **Service** (`SubscriptionEndpoints/SubscriptionService.cs`):
  - `ISubscriptionService` - Interface for subscription operations
  - `SubscriptionService` - Implementation with Maxio SDK calls
  - DTOs: `SubscriptionPlanDto`, `SubscriptionDto`

- **DI Registration** (`Program.cs`):
  - Maxio client configured with Basic auth (API key + "x")
  - Server options set to use sandbox subdomain
  - Service registered as scoped

### Error Handling

The integration uses typed error handling from the Maxio SDK:

- **ListProductsForProductFamily** - Case A error (typed `ListProductsForProductFamilyError`)
- **CreateSubscription** - Case A error (typed `CreateSubscriptionError`)
- **ReadCustomerByReference** - Case B error (raw `RawError`)
- **ListCustomerSubscriptions** - Case B error (raw `RawError`)

Each endpoint returns HTTP 500 with error details on SDK failures.

## Security Notes

1. **Secrets Management**:
   - API key is stored in .NET user-secrets (local dev)
   - Never committed to version control
   - Use Azure Key Vault or similar in production

2. **JWT Authentication**:
   - All subscription endpoints require valid JWT token
   - User identity extracted from `sub` claim
   - Scope is restricted to the authenticated user's subscriptions

3. **Idempotency**:
   - Uses customer reference lookup to prevent duplicate subscriptions
   - Multiple POST requests for same user → same subscription returned
   - No real duplicate charges even on transport retry

## Notes & Known Limitations

1. **In-Memory Database**: Subscription data is stored in Maxio only; eShopOnWeb has no persistent local subscription table. Restart clears any local mapping.

2. **Test Card**: Sandbox subscriptions may require payment profile info. Use test card:
   - Number: 4111 1111 1111 1111
   - Exp: 12/26
   - CVV: 123

3. **Email Field**: Currently uses userId as email in customer creation. In production, get from user profile.

4. **Product Family Handle**: Hardcoded to "eshop-subscribe" (seeded on sandbox). Can be made configurable.

5. **State Values**: Subscription states from Maxio include: `active`, `canceled`, `past_due`, `pending`, `trial`. The DTO returns string representation.

## Troubleshooting

### "Unable to find package AsadAli.AdvancedBilling.Sdk"
- Ensure version 1.0.2 is in `Directory.Packages.props`
- Run `dotnet restore`

### Unauthorized (401) on subscription endpoints
- Ensure you have a valid JWT token from `/api/authenticate`
- Pass token in `Authorization: Bearer {token}` header

### Failed to create subscription
- Check Maxio credentials in user-secrets: `dotnet user-secrets list` (from PublicApi dir)
- Verify `cp-exp-3` site exists and has `eshop-subscribe` family
- Check API key has permission to create customers/subscriptions

### Port conflicts
- Update `launchSettings.json` or set `APP_PORT_BLOCK_BASE` env var
- Stop any previous instance before running new build

## References

- **Maxio SDK Contract**: See `maxio-plan.md` for full API signatures
- **SDK Skills**: `dotnet-*` skills in marketplace for detailed usage
- **eShopOnWeb Pattern**: Follows existing endpoint & DI conventions
