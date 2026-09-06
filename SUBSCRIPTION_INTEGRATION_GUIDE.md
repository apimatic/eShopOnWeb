# Maxio Subscription Integration Verification Guide

This guide walks you through verifying the eShopOnWeb subscription billing integration with Maxio Advanced Billing.

## Prerequisites

1. **Environment Variables** - Ensure the following are set:
   - `MAXIO_API_KEY` - Your Maxio sandbox API key
   - `MAXIO_SITE_SUBDOMAIN` - Your Maxio site subdomain (e.g., cp-exp-4)
   - `MAXIO_ENVIRONMENT` - Maxio environment (US or EU)
   - `MAXIO_DEFAULT_PRODUCT_FAMILY` - Product family handle (e.g., eshop-subscribe)

2. **.NET SDK & Runtime** - The app automatically uses DOTNET_ROLL_FORWARD=Major to handle SDK/runtime version mismatch.

3. **In-Memory Database** - Runs with `UseOnlyInMemoryDatabase=true` (no SQL Server LocalDB required).

## Running the Application

### Start the PublicApi Server

```bash
cd src/PublicApi
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run
```

The API should start on:
- **HTTPS:** `https://localhost:27823` (or the port in launchSettings.json)
- **Swagger UI:** `https://localhost:27823/swagger/index.html`

### Verify Server is Running

```bash
curl -k https://localhost:27823/health
```

## Testing the Integration

### Step 1: Authenticate (Get JWT Token)

Call the authenticate endpoint to get a JWT token:

```bash
curl -X POST "https://localhost:27823/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser","password":"DemoUser@123"}' \
  -k
```

**Response:**
```json
{
  "username": "demouser",
  "token": "eyJhbGc...",
  "result": true
}
```

Copy the `token` value for the next steps.

### Step 2: List Available Subscription Plans

```bash
curl -X GET "https://localhost:27823/api/subscription-plans" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
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
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic subscription plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 3: Create a Subscription

Subscribe the authenticated user to the Pro Plan:

```bash
curl -X POST "https://localhost:27823/api/subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  -k
```

**Expected Response (First Time):**
```json
{
  "subscription": {
    "id": 12345678,
    "customerId": 98765,
    "productId": 7126957,
    "productHandle": "eshop-pro",
    "state": "active",
    "balanceInCents": 0,
    "currentPeriodStartsAt": "2026-09-07T00:00:00Z",
    "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
    "nextAssessmentAt": "2026-10-07T00:00:00Z",
    "activatedAt": "2026-09-07T00:00:00Z"
  },
  "isNewSubscription": true
}
```

**Idempotency Check** - Call again with the same plan handle:

```bash
curl -X POST "https://localhost:27823/api/subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  -k
```

**Expected Response (Second Time):**
```json
{
  "subscription": {
    "id": 12345678,
    ...
  },
  "isNewSubscription": false
}
```

The same subscription is returned (no duplicate created).

### Step 4: List User's Subscriptions

```bash
curl -X GET "https://localhost:27823/api/my-subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "customerId": 98765,
      "productId": 7126957,
      "productHandle": "eshop-pro",
      "state": "active",
      ...
    }
  ]
}
```

### Step 5: Subscribe to a Different Plan

```bash
curl -X POST "https://localhost:27823/api/subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"basic-plan"}' \
  -k
```

Now list subscriptions again to see both plans:

```bash
curl -X GET "https://localhost:27823/api/my-subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "productHandle": "eshop-pro",
      "state": "active"
    },
    {
      "id": 12345679,
      "productHandle": "basic-plan",
      "state": "active"
    }
  ]
}
```

## Endpoints Summary

| Endpoint | Method | Auth | Purpose |
|----------|--------|------|---------|
| `/api/subscription-plans` | GET | JWT | List available subscription plans |
| `/api/subscriptions` | POST | JWT | Create subscription for authenticated user (idempotent) |
| `/api/my-subscriptions` | GET | JWT | List subscriptions for authenticated user |
| `/api/authenticate` | POST | None | Get JWT token using username/password |

## Error Handling

### 401 Unauthorized
- Missing or invalid JWT token
- User not found in database

### 400 Bad Request
- Invalid subscription data from Maxio
- Missing required fields in request

### 422 Unprocessable Entity (from Maxio)
- Invalid product handle
- Customer data validation failure

### 500 Internal Server Error
- Connection issues with Maxio
- Unexpected errors

## Architecture Notes

- **Idempotent Customer Creation:** Users mapped by their ID as the `Reference` field in Maxio. Only one customer per user is created.
- **Idempotent Subscription Creation:** Before creating a subscription, the system checks if the user already has an active/pending subscription for that plan.
- **In-Memory Database:** Subscription mappings persist only within a single application run. Restart the app to reset.
- **Production Considerations:** For production, replace in-memory database with SQL Server and add persistent logging/monitoring.

## Troubleshooting

### "Connection refused" errors
- Verify PublicApi is running on the correct port
- Check HTTPS certificate is trusted: `dotnet dev-certs https --check`

### "401 Unauthorized" on subscription endpoints
- Ensure you're using a valid JWT token from `/api/authenticate`
- Check token hasn't expired

### "404 Plan not found"
- Verify the product handle matches Maxio catalog (e.g., "eshop-pro", "basic-plan")
- Check Maxio sandbox has the plans seeded

### "Customer or subscription creation failed"
- Check Maxio credentials in user-secrets are correct
- Verify the Maxio site subdomain is correct
- Review Maxio sandbox for any customer/product issues

## Cleanup

To reset the integration:
1. Restart the application (clears in-memory database)
2. The Maxio records persist in sandbox (customers and subscriptions remain created)
3. Manually delete test records in Maxio sandbox if needed
