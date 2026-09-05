# Maxio Subscription Billing Integration - Verification Guide

## Overview

This guide walks through verifying the Maxio subscription billing integration for eShopOnWeb. The integration adds three HTTP endpoints to the PublicApi project for managing recurring subscriptions:

- `GET /api/subscription-plans` — List available subscription plans
- `POST /api/subscriptions` — Create a subscription for the authenticated user  
- `GET /api/my-subscriptions` — Retrieve user's active subscriptions

## Prerequisites

### Environment Variables

Ensure these environment variables are set (they seed the Maxio configuration):

```bash
MAXIO_API_KEY=<your-api-key>
MAXIO_SITE_SUBDOMAIN=cp-exp-1
MAXIO_ENVIRONMENT=US
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

The integration reads these at startup and configures the Maxio client accordingly.

### Database Setup

Run with in-memory database for testing (no SQL Server LocalDB required):

```bash
UseOnlyInMemoryDatabase=true
```

Or if using SQL Server, ensure migrations are applied.

### HTTPS Dev Certificate

Both PublicApi and Web hosts use HTTPS. Verify the dev cert is trusted:

```bash
dotnet dev-certs https --check
```

If not trusted, install it:

```bash
dotnet dev-certs https --trust
```

## Build & Run

### Build the Solution

```bash
cd C:\path\to\eShopOnWeb
dotnet build eShopOnWeb.sln
```

Expected: All projects build with no errors (warnings about known vulnerabilities in dependencies are expected and can be ignored).

### Start PublicApi

```bash
cd src/PublicApi
dotnet run --launch-profile=https
```

This starts the PublicApi on its default HTTPS port (check `Properties/launchSettings.json` for the exact port, typically `https://localhost:24343`).

Wait for the message: `"LAUNCHING PublicApi"`.

## Verification Flow

### Step 1: Authenticate & Get a Token

**Request:**
```bash
curl -X POST "https://localhost:24343/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@123"
  }' \
  -k  # Skip cert validation for self-signed dev certs
```

**Expected Response:**
```json
{
  "result": true,
  "token": "eyJhbGc...",
  "username": "demouser@microsoft.com",
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false
}
```

**Extract the token** from the response — you'll use it for the next calls.

### Step 2: List Available Subscription Plans

**Request:**
```bash
curl -X GET "https://localhost:24343/api/subscription-plans" \
  -H "Authorization: Bearer <token-from-step-1>" \
  -k
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with premium features",
      "priceInCents": 29900,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic plan for getting started",
      "priceInCents": 2900,
      "intervalUnit": "month"
    }
  ]
}
```

**Verify:**
- At least two plans are returned (Pro and Basic)
- Plan handles match configured product family handles
- Prices are in cents (29900 = $299.00/mo, 2900 = $29.00/mo)
- Interval unit is "month"

### Step 3: Create a Subscription (Pro Plan)

**Request:**
```bash
curl -X POST "https://localhost:24343/api/subscriptions" \
  -H "Authorization: Bearer <token-from-step-1>" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  -k
```

**Expected Response:**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productHandle": "eshop-pro",
  "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
  "nextAssessmentAt": "2026-10-06T00:00:00Z",
  "productPriceInCents": 29900
}
```

**Verify:**
- Subscription ID is a positive integer (created in Maxio)
- State is "active" (ready to bill)
- Product handle matches the requested plan
- Next assessment date (billing date) is populated and in the future
- Price matches the Pro Plan price ($299.00 = 29900 cents)

### Step 4: Retrieve User's Subscriptions

**Request:**
```bash
curl -X GET "https://localhost:24343/api/my-subscriptions" \
  -H "Authorization: Bearer <token-from-step-1>" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
      "nextAssessmentAt": "2026-10-06T00:00:00Z",
      "productPriceInCents": 29900
    }
  ]
}
```

**Verify:**
- The subscription created in Step 3 is returned
- All fields match the creation response
- Next assessment date is the next billing date

### Step 5: Create a Second Subscription (Basic Plan) — Idempotency Test

**Request:**
```bash
curl -X POST "https://localhost:24343/api/subscriptions" \
  -H "Authorization: Bearer <token-from-step-1>" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "basic-plan"
  }' \
  -k
```

**Expected Response:**
```json
{
  "subscriptionId": 12345679,
  "state": "active",
  "productHandle": "basic-plan",
  "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
  "nextAssessmentAt": "2026-10-06T00:00:00Z",
  "productPriceInCents": 2900
}
```

**Verify:**
- A new subscription is created with a different ID
- Price matches Basic Plan ($29.00 = 2900 cents)

### Step 6: Verify Idempotency

**Request (retry the same subscription):**
```bash
curl -X POST "https://localhost:24343/api/subscriptions" \
  -H "Authorization: Bearer <token-from-step-1>" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "basic-plan"
  }' \
  -k
```

**Expected Behavior:**
- The same subscription ID from Step 5 is returned (not a duplicate)
- No new subscription is created
- Demonstrates idempotency via reference field

### Step 7: List All User Subscriptions

**Request:**
```bash
curl -X GET "https://localhost:24343/api/my-subscriptions" \
  -H "Authorization: Bearer <token-from-step-1>" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
      "nextAssessmentAt": "2026-10-06T00:00:00Z",
      "productPriceInCents": 29900
    },
    {
      "subscriptionId": 12345679,
      "state": "active",
      "productHandle": "basic-plan",
      "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
      "nextAssessmentAt": "2026-10-06T00:00:00Z",
      "productPriceInCents": 2900
    }
  ]
}
```

**Verify:**
- Both subscriptions are listed
- Each has the correct plan, price, and billing dates

## Error Cases

### Missing Authorization Header

**Request:**
```bash
curl -X GET "https://localhost:24343/api/subscription-plans" -k
```

**Expected Response:** `401 Unauthorized`

### Invalid Product Handle

**Request:**
```bash
curl -X POST "https://localhost:24343/api/subscriptions" \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{ "productHandle": "invalid-plan" }' \
  -k
```

**Expected Response:** `400 Bad Request` with error message about invalid product

### Unauthenticated User

**Request:**
```bash
curl -X GET "https://localhost:24343/api/my-subscriptions" -k
```

**Expected Response:** `401 Unauthorized`

## Production Readiness Checklist

- [x] **Idempotency:** Duplicate calls return the same subscription (via reference field)
- [x] **Error Handling:** SDK exceptions are caught and returned as HTTP errors
- [x] **Authentication:** JWT tokens required on all subscription endpoints
- [x] **Configuration:** Maxio credentials loaded from environment variables (no hardcoding)
- [x] **Build:** Solution builds with no errors
- [x] **Resilience:** HTTP client configured with retries, timeouts, and connection pooling
- [x] **Logging:** SDK client configured for observability

## Architecture Notes

### File Structure

```
src/PublicApi/
├── MaxioSettings.cs                      # Configuration class
├── Services/
│   └── MaxioSubscriptionService.cs       # Maxio operations (plans, subscriptions, customers)
├── SubscriptionEndpoints/
│   ├── SubscriptionPlansListEndpoint.cs  # GET /api/subscription-plans
│   ├── SubscriptionsCreateEndpoint.cs    # POST /api/subscriptions
│   └── SubscriptionsListEndpoint.cs      # GET /api/my-subscriptions
└── Program.cs                            # DI setup for Maxio client
```

### Key Design Decisions

1. **Idempotency by Reference:** Customer and subscription references are set to the user ID, preventing duplicate accounts on retry.
2. **Error Boundary:** All SDK exceptions are caught and mapped to HTTP error responses, with user-safe messages.
3. **Long-Lived Client:** Maxio client is registered as singleton with a long-lived HttpClient and connection pooling.
4. **Resilience:** Retries (3 attempts), timeouts (30s per attempt), and exponential backoff configured on the client.

### Secrets Handling

Maxio API key is loaded from the `MAXIO_API_KEY` environment variable at startup and passed to the SDK client. It is never stored in appsettings.json or committed to the repository.

## Cleanup

### Stop PublicApi

Press `Ctrl+C` in the PublicApi terminal window.

### Clear In-Memory Database

The in-memory database is cleared on app restart. No cleanup needed.

## Troubleshooting

### HTTPS Certificate Errors

If you see TLS errors, ensure the dev cert is trusted:
```bash
dotnet dev-certs https --trust
```

### Port Already in Use

If the PublicApi port is already bound, either:
1. Stop the other process using that port
2. Modify `Properties/launchSettings.json` to use a different port

### 404 on Endpoints

Verify the PublicApi is running and responding:
```bash
curl https://localhost:24343/swagger -k
```

You should see Swagger UI.

### 401 Unauthorized

Ensure the JWT token from `/api/authenticate` is included in the `Authorization: Bearer <token>` header on all subscription endpoints.

### Maxio API Errors

If you see errors from Maxio (e.g., "product not found"), verify:
1. The `MAXIO_SITE_SUBDOMAIN` environment variable is set to `cp-exp-1` (sandbox)
2. The product family handle is `eshop-subscribe` (default)
3. The product handles (`eshop-pro`, `basic-plan`) exist on the sandbox site

## Next Steps

The integration is now ready for:
1. **Frontend Integration:** Add UI components to browse and subscribe to plans
2. **Webhook Handling:** Listen for subscription state changes from Maxio
3. **Payment Collection:** Enable payment method capture if needed (currently not required per product family settings)
4. **Metered Billing:** Use the seeded `api-call` metered component to track usage and bill accordingly
