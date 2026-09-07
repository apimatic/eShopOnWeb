# Maxio Subscription Integration Verification Guide

This guide walks through verifying that the Maxio Advanced Billing subscription integration is working correctly in eShopOnWeb.

## Setup

1. **Verify environment variables are set** (should already be from .NET user-secrets):
   ```bash
   # Check user-secrets
   cd src/PublicApi
   dotnet user-secrets list
   ```
   Expected output should show:
   - `Maxio:ApiKey` = `fWTwAdK7mBXpcY0BGmCusZ5ZopjIHBgSLlbLEjtBA`
   - `Maxio:Subdomain` = `cp-exp-3`
   - `Maxio:Environment` = `US`
   - `Maxio:ProductFamilyHandle` = `eshop-subscribe`

2. **Build and run PublicApi**:
   ```bash
   cd src/PublicApi
   dotnet run
   ```
   Should start on `https://localhost:5100` (check console output for actual port)

## Test Flow

### Step 1: Get Authentication Token

Get a JWT token for testing:

```bash
curl -X POST https://localhost:5100/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@123"
  }' \
  --insecure
```

Extract the `token` field from the response.

### Step 2: List Available Subscription Plans

```bash
curl -X GET https://localhost:5100/api/subscription-plans \
  -H "Authorization: Bearer {TOKEN}" \
  --insecure
```

**Expected response** (HTTP 200):
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "priceInDollars": 299.00,
      "description": "..."
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "priceInDollars": 29.00,
      "description": "..."
    }
  ]
}
```

### Step 3: Create a Subscription

Subscribe to the Pro plan:

```bash
curl -X POST https://localhost:5100/api/subscriptions \
  -H "Authorization: Bearer {TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

**Expected response** (HTTP 201):
```json
{
  "subscription": {
    "id": 123456,
    "customerId": 987654,
    "state": "active",
    "productPriceInCents": 29900,
    "productPriceInDollars": 299.00,
    "nextBillingDate": "2024-10-07T00:00:00Z"
  }
}
```

**Verify**: 
- State should be "active" or "pending" (depends on sandbox configuration)
- Next billing date should be 1 month from now

### Step 4: Get User's Subscriptions

Retrieve the user's subscriptions:

```bash
curl -X GET https://localhost:5100/api/my-subscriptions \
  -H "Authorization: Bearer {TOKEN}" \
  --insecure
```

**Expected response** (HTTP 200):
```json
{
  "subscriptions": [
    {
      "id": 123456,
      "customerId": 987654,
      "state": "active",
      "productPriceInCents": 29900,
      "productPriceInDollars": 299.00,
      "nextBillingDate": "2024-10-07T00:00:00Z"
    }
  ]
}
```

**Verify**:
- The subscription returned should match the one created in Step 3
- State should remain "active"

### Step 5: Test Idempotency

Subscribe again with the same user:

```bash
curl -X POST https://localhost:5100/api/subscriptions \
  -H "Authorization: Bearer {TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

**Verify**:
- Should succeed (customer was re-looked-up by reference, not duplicated)
- `GET /api/my-subscriptions` should still show only ONE Pro subscription

### Step 6: Create Another Subscription with Basic Plan

Subscribe to Basic plan:

```bash
curl -X POST https://localhost:5100/api/subscriptions \
  -H "Authorization: Bearer {TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "basic-plan"
  }' \
  --insecure
```

**Expected**: 
- HTTP 201 with new subscription details
- `GET /api/my-subscriptions` should now show TWO subscriptions

## What Was Implemented

### 1. **Configuration & Credentials**
- `MaxioSettings.cs` — configuration model for Maxio settings
- User secrets configuration in PublicApi project
- DI registration of Maxio client with BasicAuth

### 2. **Service Layer** (`Services/MaxioSubscriptionService.cs`)
- `GetSubscriptionPlansAsync()` — calls `Products.ListProducts` to fetch available plans
- `CreateSubscriptionAsync()` — idempotent customer creation + subscription enrollment
  - Uses `Customers.ReadCustomerByReference()` for lookup by userId
  - Uses `Customers.CreateCustomer()` for creation (only if not found)
  - Uses `Subscriptions.CreateSubscription()` to enroll in plan
- `GetUserSubscriptionsAsync()` — fetches active subscriptions via `Customers.ListCustomerSubscriptions()`

### 3. **Endpoints** (MinimalApi pattern, JWT-authenticated)
- `GET /api/subscription-plans` — list available plans
- `POST /api/subscriptions` — create/subscribe with `productHandle` in request body
- `GET /api/my-subscriptions` — user's active subscriptions

### 4. **Data Models**
- DTOs for endpoints: `SubscriptionPlanDto`, `SubscriptionDto`
- AutoMapper mappings between service layer and endpoint DTOs
- User identity extracted from JWT `ClaimTypes.NameIdentifier` or "sub" claim

## Known Limitations (In-Memory Database)

- Subscription mappings (userId ↔ Maxio customerId) persist only within the current app run
- On app restart, all in-memory data is lost
- For production, implement persistent storage of userId ↔ customerId mapping

## Troubleshooting

| Issue | Solution |
|-------|----------|
| 401 Unauthorized on endpoints | Get a new token; ensure `Bearer {TOKEN}` is in Authorization header |
| 400 Bad Request on POST /subscriptions | Verify `productHandle` matches a real plan (check Step 2 response) |
| 500 Internal Server Error | Check PublicApi logs for Maxio SDK errors; ensure credentials are correct |
| "Customer not found" on GET /my-subscriptions | User hasn't subscribed yet (run Step 3 first) |
| Connection timeout to Maxio | Verify network access to `cp-exp-3.chargify.com` |

## Cleanup

Stop the PublicApi server:
```bash
# Ctrl+C in the terminal where PublicApi is running
```

All test data is lost on restart (in-memory database behavior).
