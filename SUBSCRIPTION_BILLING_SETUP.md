# Maxio Subscription Billing Integration for eShopOnWeb

This guide explains how to set up and verify the Maxio subscription billing integration.

## Setup Instructions

### 1. Configure Maxio Credentials via User Secrets

The PublicApi project uses .NET user-secrets to store sensitive Maxio credentials. Run the following commands to configure them:

```bash
cd src/PublicApi

# Initialize user-secrets (if not already initialized)
dotnet user-secrets init

# Set the Maxio API Key
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"

# Set the Maxio Site Subdomain (e.g., "apimatic-hackathon")
dotnet user-secrets set "Maxio:Subdomain" "YOUR_MAXIO_SUBDOMAIN"

# Set the Product Family Handle (e.g., "eshop-subscribe")
dotnet user-secrets set "Maxio:ProductFamilyHandle" "YOUR_PRODUCT_FAMILY_HANDLE"

# Optionally set a custom base URL (leave blank to derive from subdomain)
# dotnet user-secrets set "Maxio:BaseUrl" "https://custom-url.com"
```

**Note:** Get these values from your environment variables or from your Maxio sandbox configuration:
- `MAXIO_API_KEY` → Set as `Maxio:ApiKey`
- `MAXIO_SITE_SUBDOMAIN` → Set as `Maxio:Subdomain`
- `MAXIO_DEFAULT_PRODUCT_FAMILY` → Set as `Maxio:ProductFamilyHandle`

### 2. Configure Database (Choose One)

#### Option A: Use In-Memory Database (Recommended for Development)

When running locally without LocalDB, use the in-memory database:

```bash
# Set environment variable or pass via dotnet run
set UseOnlyInMemoryDatabase=true
```

**Caveat:** Data will be lost on application restart.

#### Option B: Use SQL Server LocalDB

If you have SQL Server Express with LocalDB installed, you can use the connection strings defined in `appsettings.json`.

### 3. Build and Run

```bash
cd src/PublicApi

# For .NET 8 with rollForward:
set DOTNET_ROLL_FORWARD=Major
dotnet run

# Or just:
dotnet run
```

The PublicApi will start on:
- HTTPS: https://localhost:7243
- HTTP: http://localhost:5000

## Verification Guide

### Step 1: Get a JWT Token

First, authenticate to get a bearer token. You need to know a valid user's credentials.

**Request:**
```bash
curl -X POST https://localhost:7243/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username": "demouser@microsoft.com", "password": "Pass@word1"}'
```

**Response:**
```json
{
  "correlationId": "...",
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com"
}
```

Save the `token` value for use in authenticated endpoints.

### Step 2: List Available Subscription Plans

This endpoint does NOT require authentication.

**Request:**
```bash
curl -X GET https://localhost:7243/api/subscription-plans
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "$299.00/mo professional plan",
      "priceInCents": 29900,
      "priceFormatted": "$299.00",
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "$29.00/mo basic plan",
      "priceInCents": 2900,
      "priceFormatted": "$29.00",
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 3: Create a Subscription

This endpoint REQUIRES authentication and a valid bearer token.

**Request:**
```bash
curl -X POST https://localhost:7243/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -d '{
    "planHandle": "eshop-pro"
  }'
```

**Expected Response (201 or 200):**
```json
{
  "id": 12345678,
  "productName": "Pro Plan",
  "productHandle": "eshop-pro",
  "state": "active",
  "createdAt": "2026-07-29T12:34:56Z",
  "nextBillingAt": "2026-08-29T12:34:56Z"
}
```

**Idempotency Check:** If you create a subscription for the same plan again with the same token, you should get the same subscription (no duplicate).

### Step 4: List User's Subscriptions

This endpoint REQUIRES authentication.

**Request:**
```bash
curl -X GET https://localhost:7243/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "state": "active",
      "createdAt": "2026-07-29T12:34:56Z",
      "nextBillingAt": "2026-08-29T12:34:56Z",
      "currentPeriodEndsAt": "2026-08-29T12:34:56Z"
    }
  ]
}
```

### Step 5: Create Another Plan Subscription

Create a subscription for a different plan:

**Request:**
```bash
curl -X POST https://localhost:7243/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -d '{
    "planHandle": "basic-plan"
  }'
```

**Expected Response:**
```json
{
  "id": 12345679,
  "productName": "Basic Plan",
  "productHandle": "basic-plan",
  "state": "active",
  "createdAt": "2026-07-29T12:35:00Z",
  "nextBillingAt": "2026-08-29T12:35:00Z"
}
```

### Step 6: Verify User Has Both Subscriptions

**Request:**
```bash
curl -X GET https://localhost:7243/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Expected Response:** Should now contain both subscriptions.

## Endpoints Summary

| Method | Endpoint                  | Auth   | Description                              |
|--------|---------------------------|--------|------------------------------------------|
| GET    | `/api/subscription-plans` | None   | List all available subscription plans    |
| POST   | `/api/subscriptions`      | JWT    | Create a subscription for the user       |
| GET    | `/api/my-subscriptions`   | JWT    | List the user's active subscriptions     |

## JWT Authentication

The PublicApi uses JWT tokens with:
- Bearer scheme in `Authorization` header: `Authorization: Bearer <token>`
- Token issued by `/api/authenticate` endpoint
- Token includes user claims: `NameIdentifier`, `Email`, `Name`

## Maxio Integration Details

### Architecture

- **MaxioConfiguration**: Holds Maxio API credentials (loaded from user-secrets)
- **MaxioApiClient**: Low-level HTTP client for Maxio API calls with Basic Auth
- **MaxioSubscriptionService**: Business logic for subscription operations
- **Endpoints**: Three PublicApi endpoints for subscriptions functionality

### Key Features

1. **Idempotent Customer Creation**: Uses customer reference (eShopOnWeb user ID) to ensure only one Maxio customer per user
2. **Idempotent Subscriptions**: Detects existing subscriptions for the same plan and returns them
3. **No Payment Method Required**: Plans are configured without requiring payment on subscription
4. **Production-Grade**: Proper error handling, logging, JWT authentication, and configuration management

### Maxio API Calls Made

1. `GET /customers/lookup.json?reference={userId}` - Check if customer exists
2. `POST /customers.json` - Create customer if not found
3. `GET /product_families/handle:{familyHandle}/products.json` - List plans
4. `POST /subscriptions.json` - Create subscription
5. `GET /customers/{customerId}/subscriptions.json` - List customer subscriptions

## Troubleshooting

### "Invalid user-secrets configuration"
- Ensure you've run `dotnet user-secrets init` in the PublicApi directory
- Check that secrets are set with: `dotnet user-secrets list`

### "Failed to connect to Maxio"
- Verify your API Key is correct
- Verify your Subdomain is correct
- Ensure you can reach `https://{subdomain}.chargify.com/products.json`
- Check Maxio sandbox is up and accessible

### "Customer already exists error"
- This is normal - it means the user was already created. The endpoint handles this gracefully.

### "Plan not found"
- Verify the Product Family Handle is correct
- Verify the plan handles (`eshop-pro`, `basic-plan`) exist in your Maxio site
- You can list all plans with the `/api/subscription-plans` endpoint to see available handles

### "Unauthorized" on subscription endpoints
- Make sure you're including the `Authorization: Bearer <token>` header
- Get a fresh token from `/api/authenticate`
- Ensure the token hasn't expired

## Database Notes

- **In-Memory**: Data is not persisted. Stop and restart clears everything.
- **UserId → Subscription Mapping**: Only persists for the duration of a single application run with in-memory DB
- The Maxio API is the system of record for subscriptions; local data is just cached

## Configuration Schema

### appsettings.json
```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "",
    "ProductFamilyHandle": "",
    "BaseUrl": null
  }
}
```

These values are overridden by user-secrets in development.

## Environment Setup

- **OS**: Windows 11 Pro
- **.NET SDK**: 8.0.x (via rollForward: latestFeature in global.json, rolls forward to .NET 10+)
- **Database**: In-Memory (no SQL Server required)
- **Authentication**: JWT Bearer tokens
- **Maxio Site**: Sandbox (`apimatic-hackathon`)

## Next Steps

After verification, you can:
1. Extend endpoints with additional operations (plan details, subscription cancellation, etc.)
2. Add webhook listeners for Maxio events
3. Integrate with the web storefront for UI
4. Add more complex subscription features (metered components, upgrades/downgrades, etc.)
