# Maxio Subscription Billing Integration — Verification Guide

This guide walks through testing the subscription billing integration added to eShopOnWeb with Maxio Advanced Billing as the system of record.

## Prerequisites

### Environment Setup

1. **Maxio API Key**: Must be set in `MAXIO_API_KEY` environment variable or user secrets
2. **.NET SDK**: Requires .NET 10 SDK (or use `dotnet roll forward` if only .NET 8 is needed)
3. **Database**: Integration uses in-memory database (no SQL Server LocalDB required)

### Credentials & Secrets

The Maxio API key is stored in `.NET User Secrets`, not in source code:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "your_api_key_here"
```

Environment variables used:
- `MAXIO_API_KEY` — API key for Maxio sandbox account
- `MAXIO_SITE_SUBDOMAIN` — Site subdomain (default: `cp-exp-3`)
- `MAXIO_ENVIRONMENT` — `Us` or `Eu` (default: `Us`)
- `MAXIO_DEFAULT_PRODUCT_FAMILY` — Product family handle (default: `eshop-subscribe`)

The configuration falls back to `appsettings.json` if environment variables are not set.

## Startup

### Running PublicApi

```bash
cd src/PublicApi
dotnet run --configuration Debug
```

The API will start on `https://localhost:28863` (port 28863 from launchSettings.json).

Note: On first run, the application seeds the database with catalog items and test users.

## Testing the Subscription Flow

### Step 1: Authenticate and Get JWT Token

All subscription endpoints require JWT bearer authentication. First, get a token:

```bash
curl -X POST https://localhost:28863/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  -k  # Ignore self-signed cert
```

Response will include a `token` field. Save this token for subsequent requests.

**Expected Response:**
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "username": "demouser@microsoft.com",
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false
}
```

### Step 2: List Available Subscription Plans

Using the token from Step 1:

```bash
curl -X GET https://localhost:28863/api/subscription-plans \
  -H "Authorization: Bearer <TOKEN>" \
  -k
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "Month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic subscription plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "Month"
    }
  ]
}
```

**Validation:**
- ✓ Two plans returned (Pro and Basic)
- ✓ Prices in cents (Pro: $299.00, Basic: $29.00)
- ✓ Interval is 1 month

### Step 3: Subscribe to a Plan

Subscribe the logged-in user to the Pro plan:

```bash
curl -X POST https://localhost:28863/api/subscriptions \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "productId": 7126957,
    "productHandle": "eshop-pro"
  }' \
  -k
```

**Expected Response:**
```json
{
  "subscriptionId": 12345678,
  "productHandle": "eshop-pro",
  "state": "active",
  "balanceInCents": 0,
  "currentPeriodEndsAt": "2026-10-07T15:30:00+00:00"
}
```

**Validation:**
- ✓ Subscription ID returned
- ✓ State is "active"
- ✓ Balance is 0 (no charges yet, free trial mode)
- ✓ Current period ends at a future date

### Step 4: Retrieve User's Subscriptions

Get the list of subscriptions for the logged-in user:

```bash
curl -X GET https://localhost:28863/api/my-subscriptions \
  -H "Authorization: Bearer <TOKEN>" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "productHandle": "eshop-pro",
      "state": "active",
      "balanceInCents": 0,
      "currentPeriodEndsAt": "2026-10-07T15:30:00+00:00"
    }
  ]
}
```

**Validation:**
- ✓ Subscription from Step 3 appears in the list
- ✓ State is "active"
- ✓ Same subscription ID as Step 3

### Step 5: Test Idempotency

Re-run Step 3 with the same token. Since the user already has an active subscription for this product, Maxio will return an error:

```bash
curl -X POST https://localhost:28863/api/subscriptions \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "productId": 7126957,
    "productHandle": "eshop-pro"
  }' \
  -k
```

**Expected Response (400 Bad Request):**
```json
{
  "error": "Subscription creation error: ..."
}
```

This is expected behavior — Maxio prevents duplicate subscriptions for the same customer/product.

### Step 6: Subscribe to Different Plan

Subscribe to the Basic plan instead:

```bash
curl -X POST https://localhost:28863/api/subscriptions \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "productId": 7126958,
    "productHandle": "basic-plan"
  }' \
  -k
```

**Expected Response:**
Should succeed with a new subscription ID (different from Step 3).

### Step 7: Verify Multiple Subscriptions

Re-run Step 4 to confirm the user now has two subscriptions:

```bash
curl -X GET https://localhost:28863/api/my-subscriptions \
  -H "Authorization: Bearer <TOKEN>" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "productHandle": "eshop-pro",
      "state": "active",
      "balanceInCents": 0,
      "currentPeriodEndsAt": "2026-10-07T15:30:00+00:00"
    },
    {
      "subscriptionId": 87654321,
      "productHandle": "basic-plan",
      "state": "active",
      "balanceInCents": 0,
      "currentPeriodEndsAt": "2026-10-07T15:30:00+00:00"
    }
  ]
}
```

**Validation:**
- ✓ Both subscriptions appear
- ✓ Different subscription IDs
- ✓ Both states are "active"

## Verifying Integration Internals

### Database Entities

Two new tables track subscriptions locally:

1. **SubscriptionMappings** — User ID ↔ Maxio Customer ID (1:1 mapping)
2. **UserSubscriptions** — User ID ↔ Maxio Subscription details (1:many)

These enable fast local lookup of subscriptions without querying Maxio on every request.

### Idempotent Customer Creation

When a user subscribes for the first time:
1. System looks up a Maxio customer by the user's ID (reference field)
2. If not found, creates a new customer with the user's email and name
3. Stores the Maxio customer ID locally for future subscriptions

This means:
- Multiple subscriptions from the same user reuse the same Maxio customer
- Duplicate subscription attempts fail at Maxio level (not ours)
- User deletion should cascade and remove subscriptions

### Maxio Interaction Points

All Maxio calls use the **Maxio Advanced Billing .NET SDK** (v1.0.2):

- **Products.ReadProductByHandle()** — Fetch plan details by handle
- **Customers.ReadCustomerByReference()** — Lookup existing customer (404 = not found)
- **Customers.CreateCustomer()** — Create new customer (idempotent via reference field)
- **Subscriptions.CreateSubscription()** — Enroll customer in plan
- **Subscriptions.ListSubscriptions()** — Query subscriptions (not used in this flow)

Error handling:
- Case A (typed errors): CreateCustomer, CreateSubscription
- Case B (RawError): ReadProductByHandle, ReadCustomerByReference

## Cleanup & Reset

To reset and test from scratch:

1. **Stop the running application** (Ctrl+C)
2. **Delete user secrets** (optional):
   ```bash
   cd src/PublicApi
   dotnet user-secrets clear
   ```
3. **Restart** — In-memory database will reinitialize
4. Re-authenticate and re-test

## Troubleshooting

### "Maxio:ApiKey not configured"

The Maxio API key is required. Set it via:
- Environment variable: `MAXIO_API_KEY=...`
- User secrets: `dotnet user-secrets set "Maxio:ApiKey" "..."`
- appsettings.json: Not recommended (never commit secrets)

### "Failed to fetch Pro plan: 404"

The plans may not exist on the sandbox. Verify:
- Site subdomain is correct (`cp-exp-3` by default)
- Plans with handles `eshop-pro` and `basic-plan` exist in Maxio
- API key has read access to the Products API

### "User ID not found in claims"

The JWT token may not contain the expected claim. Verify:
- Token was issued by `/api/authenticate`
- Token is passed in the `Authorization: Bearer <TOKEN>` header
- Token has not expired

### Connection Timeouts

Retries are configured with:
- Max retries: 3
- Per-attempt timeout: 30 seconds
- Exponential backoff with jitter

If requests hang, check network connectivity to `cp-exp-3.chargify.com` (US) or `.ebilling.maxio.com` (EU).

## Architecture Notes

### Three Endpoints, One Service Layer

- **PublicApi** — REST endpoints, JWT auth, response serialization
- **MaxioSubscriptionService** — Business logic, Maxio SDK calls, error handling
- **Infrastructure** — Entities, EF Core, database persistence

### Error Handling Strategy

- Endpoint catches `IMaxioSubscriptionService` exceptions → returns 400 Bad Request with error message
- Service catches Maxio SDK exceptions → wraps in a general `Exception` with context
- SDK exceptions are Case A (typed error) or Case B (RawError) per operation

### No Logging Infrastructure Added

By design, logging is delegated to existing infrastructure. To see HTTP requests/responses:
- Add a custom `DelegatingHandler` to the Maxio `HttpClient` (see dotnet-configuration-resilience skill)
- Or set `Logging:LogLevel:Default` to `Information` in appsettings.Development.json

## Next Steps

Once verified, consider:
1. **Error message refinement** — Surface validation errors from Maxio to callers
2. **Subscription management** — Add cancel, pause, resume endpoints
3. **Metered components** — Use the `api-call` component for usage tracking
4. **Webhooks** — Listen to Maxio events (subscription state changes, etc.)
5. **UI integration** — Build a subscription management page in the Blazor web app
