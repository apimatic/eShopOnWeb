# Maxio Subscription Billing Integration - Setup & Verification Guide

This guide walks through setting up and verifying the new recurring subscription billing capability in eShopOnWeb, powered by Maxio Advanced Billing.

---

## Prerequisites

- .NET SDK 8.0 or newer (or use `DOTNET_ROLL_FORWARD=Major` with SDK 10)
- Maxio sandbox credentials (API key, subdomain)
- The in-memory database will be used if `UseOnlyInMemoryDatabase=true` is set

---

## 1. Configure Maxio Credentials

### Option A: Using User Secrets (Recommended)

1. Open PowerShell in the repository root:
   ```powershell
   cd src/PublicApi
   ```

2. Initialize user secrets if not already done:
   ```powershell
   dotnet user-secrets init
   ```

3. Set Maxio configuration from environment variables:
   ```powershell
   $apiKey = $env:MAXIO_API_KEY
   $subdomain = $env:MAXIO_SITE_SUBDOMAIN
   $productFamily = $env:MAXIO_DEFAULT_PRODUCT_FAMILY
   $environment = $env:MAXIO_ENVIRONMENT
   
   dotnet user-secrets set "Maxio:ApiKey" "$apiKey"
   dotnet user-secrets set "Maxio:Subdomain" "$subdomain"
   dotnet user-secrets set "Maxio:ProductFamilyHandle" "$productFamily"
   dotnet user-secrets set "Maxio:Environment" "$environment"
   ```

### Option B: Using appsettings.json (Development Only)

Edit `src/PublicApi/appsettings.Development.json` and add:
```json
{
  "Maxio": {
    "ApiKey": "your-api-key-here",
    "Subdomain": "your-subdomain",
    "ProductFamilyHandle": "eshop-subscribe",
    "Environment": "sandbox"
  }
}
```

---

## 2. Set Up the Database

### For In-Memory Database (Development):

Set environment variable before running:
```powershell
$env:UseOnlyInMemoryDatabase = "true"
```

The subscription plans will be seeded automatically from Maxio during app startup.

### For SQL Server:

Update connection strings in `src/PublicApi/appsettings.json` if using a real database.

---

## 3. Run the Application

### Start PublicApi:

```powershell
cd src/PublicApi
dotnet run
```

The app will:
1. Seed the database with catalog data
2. Fetch subscription plans from Maxio and populate them
3. Start listening on `https://localhost:24483`

Expected startup output:
```
PublicApi App created...
Seeding Database...
Seeded X subscription plans from Maxio
LAUNCHING PublicApi
```

---

## 4. Authenticate & Get JWT Token

All subscription endpoints require JWT authentication. First, authenticate to get a bearer token:

### Request:
```bash
POST https://localhost:24483/api/authenticate
Content-Type: application/json

{
  "username": "demouser@microsoft.com",
  "password": "Pass@word1"
}
```

### Response:
```json
{
  "token": "eyJhbGci...",
  "result": true,
  "username": "demouser@microsoft.com"
}
```

**Save the `token` value** — you'll use it as a Bearer token in subsequent requests.

---

## 5. List Available Subscription Plans

Lists all plans seeded from Maxio:

### Request:
```bash
GET https://localhost:24483/api/subscription-plans
```

### Response:
```json
{
  "plans": [
    {
      "id": 1,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional tier",
      "priceInCents": 29900,
      "price": 299.00,
      "intervalValue": 1,
      "intervalUnit": "month"
    },
    {
      "id": 2,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic tier",
      "priceInCents": 2900,
      "price": 29.00,
      "intervalValue": 1,
      "intervalUnit": "month"
    }
  ]
}
```

---

## 6. Subscribe to a Plan

Creates a subscription in both Maxio and the local database:

### Request:
```bash
POST https://localhost:24483/api/subscriptions
Authorization: Bearer {your-token}
Content-Type: application/json

{
  "planId": 1
}
```

### Response (201 Created):
```json
{
  "subscriptionId": 100,
  "maxioSubscriptionId": 5678901,
  "planName": "Pro Plan",
  "state": "active",
  "nextBillingDate": "2026-10-06T12:34:56Z",
  "priceInCents": 29900,
  "price": 299.00
}
```

**What happens:**
1. The system checks if a Maxio customer exists for the user (by user ID as reference)
2. If not, creates one with user's email, first name, last name
3. Creates a subscription in Maxio (no payment method required; payment_collection_method = automatic)
4. Stores the subscription relationship in the local database
5. Returns the subscription details including next billing date

---

## 7. View User's Subscriptions

Lists all subscriptions for the authenticated user:

### Request:
```bash
GET https://localhost:24483/api/my-subscriptions
Authorization: Bearer {your-token}
```

### Response:
```json
{
  "subscriptions": [
    {
      "id": 100,
      "maxioSubscriptionId": 5678901,
      "planName": "Pro Plan",
      "state": "active",
      "balanceInCents": 0,
      "balance": 0.00,
      "currentPeriodEndsAt": "2026-10-06T12:34:56Z",
      "nextAssessmentAt": "2026-10-06T12:34:56Z"
    }
  ]
}
```

---

## 8. Verify Integration Works End-to-End

Run this complete test sequence:

### Step 1: Get Token
```powershell
$token = (curl -X POST https://localhost:24483/api/authenticate `
  -H "Content-Type: application/json" `
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | `
  ConvertFrom-Json).token
```

### Step 2: List Plans
```powershell
curl -H "Authorization: Bearer $token" `
  https://localhost:24483/api/subscription-plans | `
  ConvertFrom-Json | Select-Object -ExpandProperty plans
```

Expected: Shows at least 2 plans (Pro $299/mo and Basic $29/mo)

### Step 3: Subscribe (use the plan ID from Step 2)
```powershell
$subscription = curl -X POST https://localhost:24483/api/subscriptions `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{"planId":1}' | ConvertFrom-Json

$subscription | Select-Object subscriptionId, planName, state, nextBillingDate
```

Expected: Shows `state: "active"` and a future `nextBillingDate`

### Step 4: List User Subscriptions
```powershell
curl -H "Authorization: Bearer $token" `
  https://localhost:24483/api/my-subscriptions | `
  ConvertFrom-Json | Select-Object -ExpandProperty subscriptions
```

Expected: Shows the subscription created in Step 3

### Step 5: Verify Idempotency (Try subscribing again to same plan)
```powershell
curl -X POST https://localhost:24483/api/subscriptions `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{"planId":1}' | ConvertFrom-Json
```

Expected: Returns error "User already has this subscription"

---

## Architecture Overview

### Domain Model
- **SubscriptionPlan**: Cached list of available plans from Maxio
- **UserSubscription**: Maps users to their active subscriptions

### Service Layer
- **IMaxioService**: Handles all Maxio API communication
  - Lists products by product family
  - Creates/retrieves customers
  - Creates subscriptions
  - Retrieves customer subscriptions

### Endpoints (All JWT-authenticated)
- `GET /api/subscription-plans`: List available plans
- `POST /api/subscriptions`: Create subscription (with idempotency check)
- `GET /api/my-subscriptions`: User's subscriptions

### Data Persistence
- Subscription plans cached locally after first seed
- User subscriptions stored locally with Maxio IDs for cross-reference
- In-memory database for development (uses EntityFramework InMemory provider)

---

## Troubleshooting

### Plans Not Seeding
- **Check**: Maxio credentials are correct and environment variables set
- **Check**: `ProductFamilyHandle` matches the Maxio product family handle
- **Check**: Logs show "Seeded X subscription plans" message during startup

### Subscription Creation Fails
- **Check**: User email must be valid (will use `unknown@example.com` if not provided)
- **Check**: Maxio customer creation succeeded (check in Maxio dashboard)
- **Check**: Maxio credentials have permission to create subscriptions

### JWT Token Issues
- **Check**: Token is included in `Authorization: Bearer {token}` header
- **Check**: Token not expired (typically valid for 1 hour)
- **Check**: User exists in Identity database

### Database Issues (If using SQL Server)
- Run migrations: `dotnet ef database update --context CatalogContext`
- Check connection string in `appsettings.json`

---

## Sandbox Test Credentials

The sandbox has pre-seeded product family with two plans:

| Plan | Handle | Price | Monthly |
|------|--------|-------|---------|
| Pro | eshop-pro | $299.00 | Yes |
| Basic | basic-plan | $29.00 | Yes |

No setup fees, no trial period, no payment method required (payment_collection_method = automatic).

---

## Security Notes

- Secrets are stored in user-secrets locally, never in the repository
- JWT tokens are required for all subscription endpoints
- User identity is extracted from JWT claims (NameIdentifier, Email, first_name, last_name)
- Maxio API key is passed via Basic Authentication (key:x)
- All endpoints use HTTPS in production

---

## Next Steps for Production

1. **Migrate to SQL Server**: Replace in-memory database with persistent storage
2. **Payment Webhooks**: Implement Maxio webhooks for subscription lifecycle events
3. **Dunning Workflows**: Handle failed payment retries and cancellations
4. **Billing Portal**: Integrate Maxio's self-service portal for subscription management
5. **Usage-based Add-ons**: If needed, implement metered components for per-usage charges
6. **Subscription Management**: Add endpoints for upgrades, downgrades, cancellations

---

## Support

For Maxio API details, refer to the Maxio documentation accessed via the maxio-docs MCP server.
For eShopOnWeb architecture questions, see the main repository README.
