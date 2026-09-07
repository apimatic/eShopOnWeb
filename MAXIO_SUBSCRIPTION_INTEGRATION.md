# Maxio Subscription Integration — Verification Guide

This guide walks through testing the Maxio subscription billing integration added to eShopOnWeb.

## Prerequisites

- eShopOnWeb PublicApi is running (see setup steps below)
- Maxio credentials are stored in user secrets (already configured)
- A Maxio sandbox account with the following seeded entities:
  - Product Family: `eshop-subscribe`
  - Plans: `eshop-pro` ($299/month), `basic-plan` ($29/month)

## Setup & Run

### 1. Start the PublicApi Service

```bash
cd src/PublicApi
DOTNET_ROLL_FORWARD=Major dotnet run UseOnlyInMemoryDatabase=true
```

**Note:** Due to environment constraints:
- `UseOnlyInMemoryDatabase=true` uses in-memory data (no persistence across restart)
- `DOTNET_ROLL_FORWARD=Major` handles SDK/runtime version mismatch

The API will start on `https://localhost:5002` (check console output for exact port).

### 2. Verify Secrets are Configured

Maxio credentials should be loaded from user secrets:

```bash
cd src/PublicApi
dotnet user-secrets list
```

Expected output should include:
```
Maxio:ApiKey = <key>
Maxio:Subdomain = cp-exp-1
Maxio:ProductFamilyHandle = eshop-subscribe
```

## Testing the Integration

### Step 1: Create a Test User

First, authenticate to get a JWT token. POST to `/api/authenticate`:

**Request:**
```bash
curl -X POST https://localhost:5002/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "email": "testuser@example.com",
    "password": "Pass@123"
  }' \
  --insecure
```

**Response:**
```json
{
  "success": true,
  "message": "User successfully authenticated.",
  "token": "eyJhbGc..."
}
```

Save the `token` value for the next requests.

### Step 2: List Available Plans

GET `/api/subscription-plans` (requires authentication):

**Request:**
```bash
TOKEN="eyJhbGc..."

curl -X GET https://localhost:5002/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Expected response:**
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Professional Plan",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 3: Create a Subscription

POST `/api/subscriptions` (requires authentication):

**Request:**
```bash
curl -X POST https://localhost:5002/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "userEmail": "testuser@example.com",
    "userFirstName": "Test",
    "userLastName": "User",
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

**Expected response (HTTP 201 Created):**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "currentPeriodEndsAt": "2024-10-07T12:34:56Z",
  "productHandle": "eshop-pro"
}
```

**What happens internally:**
1. Service checks if a Maxio customer exists for this user (using userId as reference)
2. If not, creates a new customer with email, first/last name
3. Creates a subscription to the specified product handle
4. Returns subscription state and next billing date
5. Stores userId ↔ subscriptionId mapping in memory

### Step 4: Retrieve User's Subscriptions

GET `/api/my-subscriptions` (requires authentication):

**Request:**
```bash
curl -X GET https://localhost:5002/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Expected response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "currentPeriodEndsAt": "2024-10-07T12:34:56Z",
      "productHandle": ""
    }
  ]
}
```

---

## Implementation Details

### Architecture

**Service Layer:** `MaxioService` (singleton, DI-registered)
- Handles all Maxio SDK operations
- Maintains in-memory mapping of userId → subscriptionId (lost on restart)
- Idempotent customer creation (uses userId as reference)

**Endpoints:** Three minimal API endpoints in `SubscriptionEndpoints/`
- All require JWT authentication via `RequireAuthorization()`
- Extract userId from JWT claims (`sub` or `nameid`)
- Return error details in BadRequest (400) responses

**Configuration:**
- `Maxio:ApiKey` — API key from user secrets
- `Maxio:Subdomain` — Sandbox subdomain (`cp-exp-1`)
- `Maxio:ProductFamilyHandle` — Default product family (`eshop-subscribe`)
- `Maxio:BaseUrl` — Optional override (blank uses default `https://{subdomain}.chargify.com`)
- `Maxio:Environment` — Set from env var (US or EU; defaults to US)

### Data Persistence

**In-memory database:**
- User ↔ subscription mapping is stored in a `Dictionary<int, int>` in `MaxioService`
- Resets on service restart
- Sufficient for MVP/testing; production would use persistent storage

**Idempotency:**
- Customer creation uses `Reference = userId.ToString()` to detect duplicates
- Subscription creation uses `Reference = userId-productHandle-timestamp` for uniqueness
- Maxio detects duplicate references and returns existing customer/subscription

### Error Handling

Follows `dotnet-error-handling` patterns:
- **SDK exceptions** (non-2xx) are caught and re-thrown as `InvalidOperationException` with caller-safe messages
- **Connection failures** propagate; endpoints return 400 Bad Request
- **Missing auth** automatically rejected by `RequireAuthorization()` middleware

---

## Troubleshooting

### "Maxio:ApiKey is required"
- User secrets not configured. Run: `dotnet user-secrets set "Maxio:ApiKey" "<key>"` in src/PublicApi

### "401 Unauthorized" on subscription endpoints
- JWT token expired or malformed. Get a fresh token from `/api/authenticate`
- Token must be passed as `Authorization: Bearer <token>`

### "Failed to retrieve product families"
- Product family "eshop-subscribe" not found in sandbox
- Verify in Maxio UI that the handle exists and is spelled correctly

### "Failed to create subscription"
- Customer may not have email set, or plan may not exist
- Check Maxio sandbox UI for the products and customer

### Empty subscription list
- Subscription not created yet (create one first at Step 3)
- Or subscriptionId was lost (app restarted — data was in-memory only)

---

## Next Steps (Production)

Before going to production, consider:

1. **Persistent subscription mapping:** Store userId ↔ subscriptionId in a database table
2. **Webhook handlers:** Listen for Maxio events (subscription renewed, canceled, etc.) and update app state
3. **Billing dashboard:** UI to show subscription state, upcoming charges, payment methods
4. **Cancellation flow:** Endpoint to cancel subscriptions and handle Maxio webhooks
5. **Retry logic:** Configure SDK retry/timeout options per `dotnet-configuration-resilience`
6. **Logging:** Add structured logging via `DelegatingHandler` (see `dotnet-configuration-resilience`)
