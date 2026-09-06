# Maxio Subscription Integration Verification Guide

This guide provides step-by-step instructions to verify the recurring subscription billing integration using Maxio Advanced Billing.

## Overview

The integration adds three new API endpoints to eShopOnWeb's PublicApi for managing recurring subscriptions:
- **GET /api/subscription-plans** - List available subscription plans
- **POST /api/subscriptions** - Subscribe a user to a plan (creates Maxio customer if needed)
- **GET /api/my-subscriptions** - Retrieve logged-in user's active subscriptions

## Prerequisites

1. .NET 8.0 SDK and runtime installed
2. Maxio Advanced Billing sandbox account with credentials
3. Curl or Postman for testing API endpoints

## Setup Instructions

### 1. Configure Maxio Credentials (Environment Variables)

Before running, set these environment variables with your Maxio sandbox credentials:

```bash
# Linux/Mac
export MAXIO_API_KEY="your_api_key_here"
export MAXIO_SITE_SUBDOMAIN="your_subdomain_here"
export MAXIO_ENVIRONMENT="US"  # or "EU"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Windows (PowerShell)
$env:MAXIO_API_KEY="your_api_key_here"
$env:MAXIO_SITE_SUBDOMAIN="your_subdomain_here"
$env:MAXIO_ENVIRONMENT="US"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Windows (CMD)
set MAXIO_API_KEY=your_api_key_here
set MAXIO_SITE_SUBDOMAIN=your_subdomain_here
set MAXIO_ENVIRONMENT=US
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

The following are already created in the Maxio sandbox:
- **Product Family**: `eshop-subscribe`
- **Plans**:
  - `eshop-pro`: $299.00/month
  - `basic-plan`: $29.00/month

### 2. Enable In-Memory Database (Local Dev Setup)

The default connection string uses LocalDB which may not be available. Set this environment variable:

```bash
export UseOnlyInMemoryDatabase=true
```

**Important**: In-memory data is lost when the application stops. User-subscription mappings persist only within a single run.

### 3. Run the PublicApi Application

```bash
cd src/PublicApi
dotnet run
```

The API will be available at `https://localhost:28123` with Swagger UI at `/swagger`.

## Testing Workflow

### Step 1: Authenticate and Get JWT Token

The public API uses JWT authentication. First, obtain a bearer token:

```bash
curl -X POST https://localhost:28123/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  --insecure
```

**Expected Response**:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com",
  "result": true,
  ...
}
```

Copy the `token` value for subsequent requests.

### Step 2: List Available Subscription Plans

```bash
curl -X GET https://localhost:28123/api/subscription-plans \
  -H "Authorization: Bearer YOUR_JWT_TOKEN_HERE" \
  --insecure
```

**Expected Response**:
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
  ]
}
```

### Step 3: Create a Subscription

Subscribe the user to a plan. The system will:
1. Check if a Maxio customer exists for this user (idempotent)
2. Create the customer if needed
3. Create the subscription

```bash
curl -X POST https://localhost:28123/api/subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  --insecure
```

**Expected Response** (HTTP 201 Created):
```json
{
  "subscriptionId": 12345678,
  "reference": "user-123",
  "state": "active",
  "currentPeriodEndsAt": "2024-10-07T04:35:00Z",
  "nextAssessmentAt": "2024-10-07T04:35:00Z"
}
```

### Step 4: List User's Subscriptions

Retrieve all active subscriptions for the logged-in user:

```bash
curl -X GET https://localhost:28123/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN_HERE" \
  --insecure
```

**Expected Response**:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "reference": "user-123",
      "state": "active",
      "currentPeriodEndsAt": "2024-10-07T04:35:00Z",
      "nextAssessmentAt": "2024-10-07T04:35:00Z",
      "product": {
        "id": 7126957,
        "handle": "eshop-pro",
        "name": "Professional Plan",
        "priceInCents": 29900,
        "interval": 1,
        "intervalUnit": "month"
      }
    }
  ]
}
```

### Step 5: Verify Idempotent Subscription Creation

Subscribe the same user to the same plan again. The request should succeed and return the same subscription (no duplicate created):

```bash
curl -X POST https://localhost:28123/api/subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  --insecure
```

**Expected**: Same response as Step 3 (subscription already active, no error).

### Step 6: Switch Plans

Subscribe to a different plan:

```bash
curl -X POST https://localhost:28123/api/subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "basic-plan"
  }' \
  --insecure
```

**Expected**: New subscription created for the basic plan.

Retrieve subscriptions again to see both:

```bash
curl -X GET https://localhost:28123/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN_HERE" \
  --insecure
```

Should show both active subscriptions.

## Error Handling

The integration includes comprehensive error handling:

| HTTP Status | Meaning | Example |
|---|---|---|
| 200 OK | List/retrieval successful | Plans, subscriptions returned |
| 201 Created | Subscription created successfully | New subscription resource |
| 400 Bad Request | Missing required field | planHandle not provided |
| 401 Unauthorized | Invalid/missing JWT token | Token expired or invalid |
| 500 Internal Server Error | Maxio API error or connection failure | API unreachable |

## Architecture Notes

### Maxio SDK Integration

- **Service**: `MaxioSubscriptionService` handles all Maxio API interactions
- **Client**: `MaxioAdvancedBillingClient` configured with Basic auth (API key + "x")
- **Error Handling**: Try/catch at service boundaries; API errors wrapped in `MaxioException`

### Idempotency

Subscriptions are idempotent when using `customer_reference` (user ID). Duplicate requests with the same user and plan result in:
- If customer doesn't exist → create customer and subscription
- If customer exists and no subscription → create subscription
- If subscription already active → return existing subscription (no error)

### User-Subscription Mapping

- **Maxio side**: User ID stored as `customer_reference` in Maxio
- **eShopOnWeb side**: User ID from JWT token's NameIdentifier claim
- **Persistence**: In-memory database (data lost on app restart in dev mode)

## Troubleshooting

### Issue: 401 Unauthorized on subscription endpoints

**Solution**: Ensure:
1. JWT token is valid and not expired
2. Token is passed in `Authorization: Bearer <token>` header
3. Authenticate endpoint returns a token successfully

### Issue: 500 Internal Server Error

**Solution**: Check:
1. Maxio environment variables are set correctly
2. Maxio sandbox account is accessible
3. Product family and plan handles exist in Maxio
4. Network connectivity to Maxio API

### Issue: Subscription fails with "Customer already exists"

**Solution**: This is normal on first subscription if customer wasn't found. The service creates the customer automatically. If it persists, check Maxio dashboard for duplicate customers with same reference.

### Issue: Data lost after stopping the app

**Solution**: This is expected with in-memory database. Configure a real database (SQL Server or PostgreSQL) in `appsettings.json` for persistence.

## Configuration Reference

The following configuration is used from environment variables (via `Maxio:` section in appsettings):

```csharp
Maxio:ApiKey        = MAXIO_API_KEY
Maxio:Subdomain     = MAXIO_SITE_SUBDOMAIN
Maxio:Environment   = MAXIO_ENVIRONMENT (default: "US")
Maxio:ProductFamilyHandle = MAXIO_DEFAULT_PRODUCT_FAMILY
Maxio:BaseUrl       = (optional override of API base URL)
```

All values are loaded from environment at runtime. No secrets are stored in the repository.

## Next Steps

1. **Production Deployment**: Configure actual database and set environment variables in deployment environment
2. **UI Integration**: Connect a frontend (Blazor Web or React) to these endpoints
3. **Webhook Handling**: Implement Maxio webhooks for subscription state changes
4. **Billing Portal**: Link to Maxio's customer portal for billing management
5. **Analytics**: Add logging/monitoring of subscription events

## Support

For Maxio API documentation: https://developer.chargify.com/
For eShopOnWeb reference: https://github.com/dotnet-architecture/eShopOnWeb
