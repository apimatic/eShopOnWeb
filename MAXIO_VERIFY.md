# Maxio Subscription Billing Integration — Verification Guide

This guide walks you through verifying the Maxio subscription billing integration is working end-to-end.

## Prerequisites

- .NET 10 SDK (or allow rollForward to .NET 10 from 8.0.x)
- Maxio Advanced Billing sandbox account credentials (`cp-exp-3` site)
- Curl or Postman for testing HTTP endpoints
- Environment variables set:
  - `MAXIO_API_KEY` — your Maxio sandbox API key
  - `MAXIO_SITE_SUBDOMAIN` — `cp-exp-3` (sandbox site)
  - `MAXIO_ENVIRONMENT` — `Sandbox` (optional; defaults to sandbox)
  - `MAXIO_DEFAULT_PRODUCT_FAMILY` — `eshop-subscribe` (or your product family handle)

## Setup

### 1. Set Environment Variables (Windows PowerShell)

```powershell
$env:MAXIO_API_KEY = "your_sandbox_api_key_here"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-3"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

### 2. Build the Solution

```bash
cd repo
dotnet build eShopOnWeb.sln
```

All projects should build cleanly with warnings about pre-existing vulnerabilities only.

### 3. Start the PublicApi Server

```bash
cd src/PublicApi
dotnet run
```

The server will start on `https://localhost:27963` (adjust the port in launchSettings.json if needed). You'll see:
```
info: Microsoft.eShopWeb.PublicApi.Program[0]
      LAUNCHING PublicApi
```

## Test Flows

### Flow 1: Authenticate & Get JWT Token

**Goal:** Obtain a JWT bearer token for protected endpoints.

**Endpoint:** `POST https://localhost:27963/api/authenticate`

**Request:**
```bash
curl -k -X POST https://localhost:27963/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

**Expected Response:**
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com",
  "correlationId": "..."
}
```

**Save the token** for use in the remaining tests.

### Flow 2: List Available Subscription Plans

**Goal:** Retrieve plans from the product family.

**Endpoint:** `GET https://localhost:27963/api/subscription-plans`

**Request:**
```bash
curl -k -X GET https://localhost:27963/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Professional Plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "correlationId": "..."
}
```

**Verify:** At least two plans are returned (Pro and Basic). Prices are in cents (29900 = $299.00).

### Flow 3: Create a Subscription

**Goal:** Create a subscription for the authenticated user.

**Endpoint:** `POST https://localhost:27963/api/subscriptions`

**Request:**
```bash
curl -k -X POST https://localhost:27963/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

**Expected Response:** (HTTP 201 Created)
```json
{
  "subscription": {
    "id": 54321098,
    "state": "active",
    "productHandle": "eshop-pro",
    "productId": 7126957,
    "currentPeriodEndsAt": "2026-10-07T00:00:00",
    "nextAssessmentAt": "2026-10-07T00:00:00"
  },
  "correlationId": "..."
}
```

**Verify:**
- Subscription ID is returned (integer > 0)
- State is "active"
- Product matches the requested handle
- Current period and next assessment dates are set

**Idempotency Test:** Run the create request again with the same productHandle. It should return HTTP 201 again (assuming subscription doesn't already exist; the implementation uses `Customer.Reference` for idempotency). If a second attempt returns a conflict, that indicates the idempotency is working correctly.

### Flow 4: Retrieve User's Subscriptions

**Goal:** List all subscriptions for the authenticated user.

**Endpoint:** `GET https://localhost:27963/api/my-subscriptions`

**Request:**
```bash
curl -k -X GET https://localhost:27963/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 54321098,
      "state": "active",
      "productHandle": "eshop-pro",
      "productId": 7126957,
      "currentPeriodEndsAt": "2026-10-07T00:00:00",
      "nextAssessmentAt": "2026-10-07T00:00:00"
    }
  ],
  "correlationId": "..."
}
```

**Verify:** The subscription created in Flow 3 appears in the list.

## Troubleshooting

### "Product family not found" error (500)
- Verify `Maxio:ProductFamilyHandle` in appsettings.json or environment variable `MAXIO_DEFAULT_PRODUCT_FAMILY` matches the actual family in Maxio (e.g., `eshop-subscribe`)
- Check Maxio sandbox UI to confirm the family exists

### "API key invalid" error (401)
- Verify `MAXIO_API_KEY` environment variable is set and contains a valid sandbox API key from Maxio
- API keys are not email addresses; they are generated in the Maxio dashboard

### "Customer could not be created" error (422)
- Ensure the customer reference (user ID) doesn't already exist with different name/email in Maxio
- The implementation extracts name from the user ID (first part before `@`); ensure it's valid

### "Subscription creation failed" error (422)
- Verify the product handle exists in the product family
- Confirm the product does not require payment upfront (this implementation assumes no payment required on MVP)

### "Unauthorized" on protected endpoints
- Ensure the JWT token is included in the `Authorization: Bearer` header
- Token expires; get a fresh one from the authenticate endpoint if needed
- Verify token is from the correct endpoint (e.g., `/api/authenticate`, not login)

## Architecture Notes

- **Service Layer:** `IMaxioSubscriptionService` wraps all Maxio SDK interactions; errors are caught and wrapped in `InvalidOperationException`
- **Endpoints:** Three minimal API endpoints in `SubscriptionEndpoints/` follow existing PublicApi conventions
- **Configuration:** All Maxio settings are bound from `appsettings.json` Maxio section; credentials come from environment variables (never hardcoded)
- **DI:** MaxioAdvancedBillingClient is registered as a singleton; HTTP client is managed by the named factory pattern to ensure proper timeout and connection lifecycle
- **Database:** In-memory database is used (UseOnlyInMemoryDatabase=true); subscriptions are not persisted across app restarts. Maxio is the system of record.
- **Error Handling:** SDK exceptions are caught per operation and provide diagnostic logging; typed error accessors (TryGet*) distinguish validation errors from transport/API errors

## Next Steps

After verification:
1. Update appsettings.json with your production Maxio site and family details
2. Integrate the endpoints into a web UI (cart → checkout → subscription options)
3. Add payment profile handling if the product requires payment upfront
4. Implement webhook handlers for billing events (optional; not in MVP)
5. Add database persistence for user-subscription relationships if needed
