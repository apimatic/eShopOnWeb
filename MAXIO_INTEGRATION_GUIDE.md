# Maxio Advanced Billing Integration for eShopOnWeb

This guide explains how to set up and verify the Maxio subscription billing integration for the eShopOnWeb reference application.

## Architecture Overview

The subscription capability is **additive and parallel** to the existing order/basket flow. It enables users to:
- Browse available subscription plans
- Subscribe to a plan
- View their active subscriptions
- Manage subscriptions through the Maxio API

### Key Components

| Component | Purpose | Location |
|-----------|---------|----------|
| **Subscription Entity** | Tracks user subscriptions in the local database | `src/ApplicationCore/Entities/Subscription.cs` |
| **MaxioSubscriptionService** | Facade for all Maxio API operations | `src/PublicApi/Services/MaxioSubscriptionService.cs` |
| **HTTP Endpoints** | Public API endpoints for subscription operations | `src/PublicApi/SubscriptionEndpoints/` |
| **MaxioSettings** | Configuration binding for Maxio credentials | `src/PublicApi/MaxioSettings.cs` |
| **SDK Client Registration** | DI wiring for the Maxio SDK client | `src/PublicApi/Program.cs` |

## Setup Instructions

### 1. Set Maxio Credentials in User Secrets

The Maxio API key must be configured via .NET user-secrets (never hardcoded).

**On Windows (PowerShell):**
```powershell
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

**On macOS/Linux (Bash):**
```bash
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

Replace `YOUR_MAXIO_API_KEY` with your actual Maxio sandbox API key.

**Verify secrets were set:**
```bash
dotnet user-secrets list
```

### 2. Configure Environment

Set the environment variable for database and SDK features:

```bash
# Use in-memory database (ephemeral, loses data on restart)
export UseOnlyInMemoryDatabase=true

# Or set this if running the application
set UseOnlyInMemoryDatabase=true  # Windows
```

### 3. Run the Application

```bash
cd src/PublicApi
dotnet run
```

The application will start on `https://localhost:28103`.

---

## Verification Guide

### Step 1: Authenticate

Get a JWT token by authenticating with the demo user:

**Request:**
```bash
curl -X POST https://localhost:28103/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  -k
```

**Response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "result": true,
  "username": "demouser@microsoft.com"
}
```

Save the `token` value for the remaining requests.

### Step 2: List Available Plans

**Request:**
```bash
curl -X GET https://localhost:28103/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

**Expected Response:**
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "priceInDollars": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "priceInDollars": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 3: Create a Subscription

**Request:**
```bash
curl -X POST https://localhost:28103/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
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
  "nextBillingAt": "2026-10-07T00:00:00Z",
  "priceInDollars": 299.00
}
```

### Step 4: List User's Subscriptions

**Request:**
```bash
curl -X GET https://localhost:28103/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "nextBillingAt": "2026-10-07T00:00:00Z",
      "priceInDollars": 299.00
    }
  ]
}
```

---

## Key Implementation Details

### Authentication Flow

- All subscription endpoints require JWT authentication (Bearer token)
- The token is obtained from the `/api/authenticate` endpoint
- The logged-in user's username is extracted from the JWT `Name` claim
- The user is looked up in the Identity database to get their user ID

### Idempotency

- **Customer creation** is idempotent by user ID (reference field)
  - If a customer already exists for the user, the existing customer ID is reused
  - Double-clicking "subscribe" won't create duplicate customers

- **Subscription creation** uses a unique reference key (userId + productHandle + timestamp)
  - Multiple subscriptions to different plans are allowed
  - Multiple subscriptions to the same plan in quick succession are prevented by Maxio

### Data Persistence

- **In-memory database** (with `UseOnlyInMemoryDatabase=true`):
  - Subscription records are stored locally for the session
  - All data is lost when the application stops
  - Useful for testing and demo purposes

- **SQL Server database** (production):
  - Requires local SQL Server instance or remote connection
  - Set connection strings in `appsettings.json` or via configuration
  - Subscriptions persist across application restarts

### Error Handling

The endpoints return standard HTTP status codes:

| Status | Scenario |
|--------|----------|
| `200 OK` | Operation succeeded |
| `400 Bad Request` | Invalid request (e.g., plan not found, validation error) |
| `401 Unauthorized` | Missing or invalid JWT token |
| `5xx Server Error` | Maxio API error or internal exception |

---

## Troubleshooting

### Issue: "User not authenticated" error
- **Cause**: Missing or invalid Bearer token
- **Solution**: Call `/api/authenticate` first to get a valid token, then include it in the `Authorization: Bearer` header

### Issue: "Failed to list subscription plans" error
- **Cause**: Invalid Maxio API key or subdomain
- **Solution**: Verify credentials in user-secrets: `dotnet user-secrets list`

### Issue: "Failed to create customer" error (422)
- **Cause**: Reference already exists (user already has a Maxio customer)
- **Solution**: This is expected on retry. The existing customer will be reused

### Issue: "API is not responding"
- **Cause**: Maxio sandbox is down or network connectivity issue
- **Solution**: Check https://www.chargify.com status page and verify internet connection

### Issue: Subscription shows "unknown" state
- **Cause**: Maxio returned an unrecognized enum value
- **Solution**: This is safe - the subscription exists but the state is newer than the SDK knows about

---

## Endpoint Reference

### GET /api/subscription-plans
Lists all available subscription plans from the configured product family.

- **Auth**: Required (Bearer token)
- **Request**: No body
- **Response**: `ListSubscriptionPlansResponse` containing array of `PlanDto`

### POST /api/subscriptions
Creates a new subscription for the authenticated user.

- **Auth**: Required (Bearer token)
- **Request Body**: `{ "productHandle": "string" }`
- **Response**: `CreateSubscriptionResponse` with subscription details
- **Idempotency**: Safe to call multiple times for different plans; same plan returns cached customer

### GET /api/my-subscriptions
Lists all active and pending subscriptions for the authenticated user.

- **Auth**: Required (Bearer token)
- **Request**: No body
- **Response**: `ListMySubscriptionsResponse` containing array of `UserSubscriptionDto`

---

## Configuration Reference

### appsettings.json
```json
{
  "Maxio": {
    "ApiKey": "",                          // Loaded from user-secrets
    "Subdomain": "cp-exp-2",              // Sandbox subdomain
    "ProductFamilyHandle": "eshop-subscribe",  // Product family containing plans
    "BaseUrl": null                        // Optional override for custom base URL
  }
}
```

### Seeded Maxio Entities (Sandbox)

| Entity | Handle | Details |
|--------|--------|---------|
| Product Family | `eshop-subscribe` | Container for plans |
| Pro Plan | `eshop-pro` | $299/mo, monthly billing |
| Basic Plan | `basic-plan` | $29/mo, monthly billing |
| Metered Component | `api-call` | $0.01/unit (optional, not used by default) |

---

## Next Steps

1. **Test the complete flow** using the verification guide above
2. **Integrate with UI** (optional): Add a frontend component to call these endpoints
3. **Monitor subscriptions** in the Maxio dashboard (https://cp-exp-2.chargify.com)
4. **Production setup**: Repeat the user-secrets setup on your production server with real credentials

---

## Support

For issues related to:
- **eShopOnWeb integration**: Check this guide and the code comments
- **Maxio API questions**: Refer to [Maxio Advanced Billing API docs](https://advanced-billing.chargify.com/api-docs)
- **SDK issues**: Check the [SDK repository](https://github.com/maxio-com/ab-dotnet-sdk)
