# Maxio Subscription Billing Integration Verification Guide

This guide walks you through verifying the Maxio Advanced Billing subscription integration in eShopOnWeb.

## Prerequisites

- .NET SDK 8.0+  (if only .NET 10 is installed, use `DOTNET_ROLL_FORWARD=Major`)
- Maxio sandbox credentials set as environment variables:
  - `MAXIO_API_KEY`
  - `MAXIO_SITE_SUBDOMAIN`
  - `MAXIO_ENVIRONMENT`
  - `MAXIO_DEFAULT_PRODUCT_FAMILY`
- ASP.NET Core 8.0 runtime (or use rollForward)
- In-memory database configuration: `UseOnlyInMemoryDatabase=true`

## Setup Steps

### 1. Configure Environment Variables

Ensure these environment variables are set for your session:

```bash
# PowerShell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-1"
$env:MAXIO_ENVIRONMENT = "US"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
```

### 2. Start the PublicApi Service

```bash
cd src/PublicApi
dotnet run --no-build
```

The PublicApi will start on `https://localhost:25703`.

### 3. Verify HTTPS Certificate

If you get certificate errors, ensure your dev certificate is trusted:

```bash
dotnet dev-certs https --check
# If needed: dotnet dev-certs https --clean && dotnet dev-certs https --trust
```

## Testing the Integration

### Step 1: Authenticate and Get JWT Token

First, create a test user and get a JWT token:

```bash
# Using curl (Windows: use curl or PowerShell Invoke-WebRequest)
$body = @{
    username = "demouser@microsoft.com"
    password = "Pass@word123"
} | ConvertTo-Json

$auth = Invoke-WebRequest `
  -Uri "https://localhost:25703/api/authenticate" `
  -Method POST `
  -Headers @{"Content-Type" = "application/json"} `
  -Body $body `
  -SkipCertificateCheck

$token = ($auth.Content | ConvertFrom-Json).token
Write-Output "Token: $token"
```

**Note:** The test user `demouser@microsoft.com` is seeded during database initialization. If this user doesn't exist, you can register a new user through the web application first.

### Step 2: Get Available Subscription Plans

With the JWT token, retrieve the available subscription plans:

```bash
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$plans = Invoke-WebRequest `
  -Uri "https://localhost:25703/api/subscription-plans" `
  -Method GET `
  -Headers $headers `
  -SkipCertificateCheck

$plansJson = $plans.Content | ConvertFrom-Json
Write-Output ($plansJson.plans | ConvertTo-Json -Depth 10)
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "Month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "Month"
    }
  ]
}
```

### Step 3: Subscribe to a Plan

Subscribe the current user to the Pro Plan:

```bash
$subscribeBody = @{
    productHandle = "eshop-pro"
} | ConvertTo-Json

$subscription = Invoke-WebRequest `
  -Uri "https://localhost:25703/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -Body $subscribeBody `
  -SkipCertificateCheck

$subJson = $subscription.Content | ConvertFrom-Json
Write-Output ($subJson | ConvertTo-Json -Depth 10)
```

**Expected Response:**
```json
{
  "subscriptionId": 12345678,
  "state": "Active",
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "productPriceInCents": 29900,
  "nextAssessmentAt": "2026-10-06T00:00:00",
  "activatedAt": "2026-09-06T12:34:56"
}
```

### Step 4: List User Subscriptions

Retrieve all subscriptions for the current user:

```bash
$mySubscriptions = Invoke-WebRequest `
  -Uri "https://localhost:25703/api/my-subscriptions" `
  -Method GET `
  -Headers $headers `
  -SkipCertificateCheck

$subsJson = $mySubscriptions.Content | ConvertFrom-Json
Write-Output ($subsJson.subscriptions | ConvertTo-Json -Depth 10)
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "Active",
      "currentPeriodEndsAt": "2026-10-06T00:00:00",
      "nextAssessmentAt": "2026-10-06T00:00:00",
      "activatedAt": "2026-09-06T12:34:56",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "productPriceInCents": 29900
    }
  ]
}
```

### Step 5: Test Error Cases

#### Invalid Authorization
```bash
$noAuth = Invoke-WebRequest `
  -Uri "https://localhost:25703/api/subscription-plans" `
  -Method GET `
  -SkipCertificateCheck `
  -ErrorAction SilentlyContinue

# Should return 401 Unauthorized
Write-Output $noAuth.StatusCode  # Should be 401
```

#### Missing Required Field
```bash
$badSubscribe = Invoke-WebRequest `
  -Uri "https://localhost:25703/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -Body '{}' `
  -SkipCertificateCheck `
  -ErrorAction SilentlyContinue

# Should return 400 Bad Request
Write-Output $badSubscribe.StatusCode  # Should be 400
```

## Idempotency Verification

The integration supports idempotent subscription creation. To verify:

1. Subscribe a user to a plan (Step 3 above)
2. Subscribe the same user to the **same** plan again
3. Verify that:
   - No error is returned
   - The same subscription ID is returned (not a duplicate)
   - Maxio customer is looked up by reference (user ID) and reused

## Configuration

### Configuration Keys

The integration reads from the `Maxio:` configuration section:

| Key | Environment Variable | Required | Notes |
|-----|----------------------|----------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Yes | Maxio sandbox API key |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Yes | Maxio site subdomain (e.g., `cp-exp-1`) |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | No | Product family handle; defaults to `eshop-subscribe` |
| `Maxio:BaseUrl` | Not required | No | Optional base URL override (for proxy/mock servers) |

### Database Configuration

Since LocalDB is not available, use in-memory database:

```bash
$env:UseOnlyInMemoryDatabase = "true"
```

**Note:** In-memory database data does not persist between runs. User ID ↔ subscription mappings are only available within a single run.

## Architecture Overview

### Endpoints

- **GET `/api/subscription-plans`** — Lists available plans from Maxio
  - No request body required
  - Returns array of plan objects with pricing and billing interval
  - Filters to plans with handles `eshop-pro` and `basic-plan` from the `eshop-subscribe` product family

- **POST `/api/subscriptions`** — Subscribe current user to a plan
  - Request body: `{ "productHandle": "eshop-pro" }`
  - Creates or retrieves Maxio customer (idempotent, using user ID as reference)
  - Creates subscription for the customer
  - Returns subscription object with ID, state, and pricing

- **GET `/api/my-subscriptions`** — List subscriptions for current user
  - No request body required
  - Retrieves customer by user ID reference
  - Returns array of subscription objects with state and next billing date

### Authentication

All endpoints require JWT Bearer authentication. The token is obtained via `/api/authenticate`.

### Error Handling

- **400 Bad Request:** Missing required fields (e.g., `productHandle` in subscription creation)
- **401 Unauthorized:** Missing or invalid JWT token
- **404 Not Found:** User not found or customer not found (for read operations, returns empty list)
- **500 Internal Server Error:** Maxio API errors, customer creation failures, subscription creation failures

### Idempotency

Subscription creation is idempotent:
- User ID is passed as the customer reference to Maxio
- On repeat subscription creation, the customer is looked up by reference
- If customer already exists, the lookup succeeds and a new subscription is created for that customer
- If subscription to the same product already exists, Maxio prevents duplicate subscriptions

## Troubleshooting

### Certificate Errors
```bash
# Trust the development certificate
dotnet dev-certs https --trust
```

### Maxio API Errors (500)
Check the following:
- API key and subdomain are correct in configuration
- Maxio site is online and accessible
- Product handles (`eshop-pro`, `basic-plan`) exist on the site
- Product family `eshop-subscribe` exists and contains these products

### User Not Found (401)
- Verify user exists in the database (check `demouser@microsoft.com` or register a new user)
- Verify JWT token is valid and not expired

### "No customer found" on subscription list
- This is expected if the user has never subscribed (returns empty list)
- Subscribe to a plan first (Step 3), then list subscriptions

## Next Steps

After verification:
1. Integrate subscription endpoints into the web UI (if desired)
2. Configure production Maxio credentials for live billing
3. Add webhook handlers for Maxio events (subscription state changes, failed payments, etc.)
4. Implement subscription cancellation and plan changes
5. Add metered component usage tracking (if using the metered API component)
