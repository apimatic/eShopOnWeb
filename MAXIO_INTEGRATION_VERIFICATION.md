# Maxio Subscription Integration Verification Guide

This guide walks you through verifying the Maxio subscription billing integration in eShopOnWeb.

## Prerequisites

- .NET 10 SDK installed (or `.NET 8.0` runtime with `DOTNET_ROLL_FORWARD=Major`)
- Maxio sandbox API credentials set in user-secrets:
  ```bash
  cd src/PublicApi
  dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"
  dotnet user-secrets set "Maxio:Subdomain" "cp-exp-1"
  ```
- Environment variables configured (if using external config):
  ```bash
  export MAXIO_API_KEY="<your-api-key>"
  export MAXIO_SITE_SUBDOMAIN="cp-exp-1"
  export MAXIO_ENVIRONMENT="sandbox"
  export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
  ```

## Step 1: Start the PublicApi Service

```bash
cd src/PublicApi
dotnet run --configuration Release --urls "https://localhost:29183"
```

Expected output:
- Server listening on `https://localhost:29183`
- Swagger UI available at `https://localhost:29183/swagger/index.html`

## Step 2: Authenticate and Get a JWT Token

Use Curl or Postman to POST to the authenticate endpoint:

```bash
curl -X POST "https://localhost:29183/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  --insecure
```

**Response:**
```json
{
  "result": true,
  "correlationId": "...",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Copy the token** — you'll use it as the Bearer token for all subsequent requests.

## Step 3: List Available Subscription Plans

```bash
curl -X GET "https://localhost:29183/api/subscription-plans" \
  -H "Authorization: Bearer <YOUR-JWT-TOKEN>" \
  --insecure
```

**Expected Response (200 OK):**
```json
[
  {
    "handle": "eshop-pro",
    "name": "Pro",
    "priceInCents": 29900,
    "intervalInMonths": 1,
    "description": null
  },
  {
    "handle": "basic-plan",
    "name": "Basic",
    "priceInCents": 2900,
    "intervalInMonths": 1,
    "description": null
  }
]
```

**Verification:**
- ✓ Two plans returned (`eshop-pro` and `basic-plan`)
- ✓ Prices match Maxio sandbox (Pro: $299/mo, Basic: $29/mo)
- ✓ Monthly billing interval (1 month)

## Step 4: Create a Subscription

```bash
curl -X POST "https://localhost:29183/api/subscriptions" \
  -H "Authorization: Bearer <YOUR-JWT-TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}' \
  --insecure
```

**Expected Response (200 OK):**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "nextBillingAt": "2026-10-07T00:00:00+00:00"
}
```

**Verification:**
- ✓ Subscription created with ID
- ✓ State is `active` (no payment method required per config)
- ✓ Next billing date is ~1 month from now
- ✓ Maxio customer record created (idempotent: calling again with same user doesn't create duplicate)

### Test Idempotency

Call the same endpoint again with the same user/token:

```bash
curl -X POST "https://localhost:29183/api/subscriptions" \
  -H "Authorization: Bearer <YOUR-JWT-TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "basic-plan"}' \
  --insecure
```

**Expected Response (200 OK):**
```json
{
  "subscriptionId": 87654321,
  "state": "active",
  "nextBillingAt": "2026-10-07T00:00:00+00:00"
}
```

**Verification:**
- ✓ A *new* subscription is created (same user, different plan)
- ✓ Maxio customer was reused (not duplicated)
- ✓ Different subscription ID from first call

## Step 5: Retrieve User Subscriptions

```bash
curl -X GET "https://localhost:29183/api/my-subscriptions" \
  -H "Authorization: Bearer <YOUR-JWT-TOKEN>" \
  --insecure
```

**Expected Response (200 OK):**
```json
[
  {
    "subscriptionId": 12345678,
    "productHandle": "eshop-pro",
    "productName": "Pro",
    "nextBillingAt": "2026-10-07T00:00:00+00:00",
    "state": "active"
  },
  {
    "subscriptionId": 87654321,
    "productHandle": "basic-plan",
    "productName": "Basic",
    "nextBillingAt": "2026-10-07T00:00:00+00:00",
    "state": "active"
  }
]
```

**Verification:**
- ✓ Both subscriptions returned for the logged-in user
- ✓ Plans match what was subscribed
- ✓ States are `active`
- ✓ Next billing dates populated

## Step 6: Error Handling Verification

### Test Invalid Plan Handle

```bash
curl -X POST "https://localhost:29183/api/subscriptions" \
  -H "Authorization: Bearer <YOUR-JWT-TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "nonexistent-plan"}' \
  --insecure
```

**Expected Response (400 Bad Request):**
```json
{
  "error": "Invalid subscription parameters"
}
```

**Verification:**
- ✓ Error handled gracefully
- ✓ No unhandled exceptions
- ✓ Client-friendly error message

### Test Missing Authorization

```bash
curl -X GET "https://localhost:29183/api/subscription-plans" \
  --insecure
```

**Expected Response (401 Unauthorized):**
```
No authorization token provided
```

**Verification:**
- ✓ Endpoints properly require JWT authentication

## Step 7: Verify No Secrets in Code

```bash
# From repo root
grep -r "MAXIO_API_KEY" --include="*.cs" --include="*.json" src/ || echo "✓ No hardcoded API keys"
grep -r "chargify" --include="*.cs" --include="*.json" src/ | grep -v "https://" || echo "✓ No hardcoded credentials"
cat src/PublicApi/appsettings.json | grep -i "apikey" || echo "✓ ApiKey not in appsettings"
```

**Verification:**
- ✓ No secrets in code
- ✓ No API keys in configuration files
- ✓ All sensitive data loaded from user-secrets/environment

## Step 8: Verify Database Persistence

If running with `UseOnlyInMemoryDatabase=true`:

1. Create a subscription (Step 4)
2. Retrieve subscriptions (Step 5) — should return the subscription
3. **Stop and restart the app** (`Ctrl+C` and `dotnet run` again)
4. Retrieve subscriptions again (Step 5)

**Verification:**
- ✓ Subscriptions are lost after app restart (in-memory database)
- ℹ️ For production, persist to SQL Server and add database migrations

## Step 9: Clean Up

Stop the running service:

```bash
# Press Ctrl+C in the terminal running PublicApi
```

## Summary

| Endpoint | Method | Auth | Expected Status | Verified |
|----------|--------|------|-----------------|----------|
| `/api/subscription-plans` | GET | JWT | 200 | ✓ |
| `/api/subscriptions` | POST | JWT | 200 | ✓ |
| `/api/my-subscriptions` | GET | JWT | 200 | ✓ |
| Missing auth | ANY | None | 401 | ✓ |
| Invalid plan | POST | JWT | 400 | ✓ |

## Troubleshooting

### "Unable to connect to Maxio"
- Verify Maxio API key is set correctly in user-secrets
- Check `MAXIO_API_KEY` environment variable
- Ensure `MAXIO_SITE_SUBDOMAIN` is `cp-exp-1`

### "Subscription plans not returned"
- Verify plans `eshop-pro` and `basic-plan` exist on site `cp-exp-1`
- Check Maxio API logs for 404 errors
- Confirm product family `eshop-subscribe` is configured

### "Customer creation failed"
- Check Maxio sandbox for duplicate customers (reference field is userId)
- Verify email format in CreateCustomer request
- Review Maxio API error response details

### In-memory database resets
- Expected behavior with `UseOnlyInMemoryDatabase=true`
- For persistent storage: configure SQL Server connection and run migrations

