# Maxio Subscription Billing Integration - Setup & Verification Guide

## Overview

This guide walks through the Maxio Advanced Billing integration added to eShopOnWeb. The integration provides three new JWT-authenticated REST API endpoints for managing recurring subscriptions.

## Prerequisites

- .NET 8.0 SDK or higher (with `rollForward: latestMajor` in global.json)
- ASP.NET Core 8.0 runtime (or allow rollforward)
- Maxio sandbox site access (cp-exp-2)
- Maxio API credentials (API key)

## Environment Configuration

### Step 1: Set Maxio API Credentials in User-Secrets

The Maxio API key must be set in user-secrets (never in the repository or appsettings files).

From the `src/PublicApi` directory:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "<YOUR_MAXIO_API_KEY>"
```

Replace `<YOUR_MAXIO_API_KEY>` with your Maxio sandbox API key from the Maxio admin console.

### Step 2: Review Configuration

The following Maxio configuration keys are set in `src/PublicApi/appsettings.json`:

```json
"Maxio": {
  "ApiKey": "",
  "Subdomain": "cp-exp-2",
  "ProductFamilyHandle": "eshop-subscribe",
  "BaseUrl": ""
}
```

- **ApiKey**: Provided via user-secrets (not in appsettings)
- **Subdomain**: Set to "cp-exp-2" (Maxio sandbox)
- **ProductFamilyHandle**: Set to "eshop-subscribe" (product family handle in Maxio)
- **BaseUrl**: Optional override. If empty, defaults to `https://{Subdomain}.chargify.com`

### Step 3: Database Configuration

The integration works with both in-memory and SQL Server databases:

- **In-memory (default for testing)**: Set `UseOnlyInMemoryDatabase=true` in environment or appsettings
- **SQL Server**: Ensure connection strings are configured in appsettings.json

## Running the Application

### Start PublicApi Service

From the repository root:

```bash
# With in-memory database (recommended for testing)
set UseOnlyInMemoryDatabase=true
dotnet run --project src/PublicApi/PublicApi.csproj

# Or with environment variable on Windows:
$env:UseOnlyInMemoryDatabase="true"
dotnet run --project src/PublicApi/PublicApi.csproj
```

The API will be available at `https://localhost:25803`

## API Endpoints

All endpoints require JWT authentication (Bearer token in Authorization header).

### 1. List Subscription Plans

**Endpoint:** `GET /api/subscription-plans`

**Authentication:** Required (JWT Bearer token)

**Response:**
```json
{
  "correlationId": "uuid",
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 2. Create Subscription

**Endpoint:** `POST /api/subscriptions`

**Authentication:** Required (JWT Bearer token)

**Request Body:**
```json
{
  "productHandle": "eshop-pro"
}
```

**Response (201 Created):**
```json
{
  "correlationId": "uuid",
  "subscription": {
    "id": 12345,
    "customerId": 67890,
    "state": "active",
    "productHandle": "eshop-pro",
    "createdAt": "2024-01-15T10:30:00Z",
    "currentPeriodEndsAt": "2024-02-15T10:30:00Z",
    "nextBillingAt": "2024-02-15T10:30:00Z"
  }
}
```

**Notes:**
- Automatically creates a Maxio customer if one doesn't exist (using eShopOnWeb user ID as reference)
- Idempotent: subscribing the same user to the same plan twice returns the same subscription
- No payment method required (sandbox plan configuration allows billing without card)

### 3. Get User's Subscriptions

**Endpoint:** `GET /api/my-subscriptions`

**Authentication:** Required (JWT Bearer token)

**Response:**
```json
{
  "correlationId": "uuid",
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "price": 299.00,
      "createdAt": "2024-01-15T10:30:00Z",
      "currentPeriodEndsAt": "2024-02-15T10:30:00Z",
      "nextBillingAt": "2024-02-15T10:30:00Z"
    }
  ]
}
```

## Verification Walkthrough

### Step 1: Authenticate

First, log in and get a JWT token:

```bash
curl -X POST https://localhost:25803/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@example.com","password":"DemoUser@123"}'
```

Response:
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@example.com"
}
```

Save the token value for the next requests.

### Step 2: List Available Plans

```bash
curl -X GET https://localhost:25803/api/subscription-plans \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json"
```

Should return the seeded plans from Maxio:
- Pro Plan (eshop-pro) - $299.00/month
- Basic Plan (basic-plan) - $29.00/month

### Step 3: Subscribe to a Plan

```bash
curl -X POST https://localhost:25803/api/subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

Expected: 201 Created response with subscription details

The endpoint will:
1. Extract the authenticated user from the JWT token
2. Create or retrieve the Maxio customer (using eShopOnWeb user ID as reference)
3. Create a subscription to the specified product
4. Return the subscription details

### Step 4: Verify Subscription Creation

```bash
curl -X GET https://localhost:25803/api/my-subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json"
```

Expected: 200 OK with list of user's subscriptions (should include the one just created)

### Step 5: Test Idempotency

Subscribe again with the same token and same product handle:

```bash
curl -X POST https://localhost:25803/api/subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

Expected: 201 Created with the same subscription (system detects existing subscription via customer reference)

### Step 6: Switch Plans

Subscribe to a different plan:

```bash
curl -X POST https://localhost:25803/api/subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"basic-plan"}'
```

Then verify both subscriptions appear in `/api/my-subscriptions`

## Error Handling

| Status | Scenario |
|--------|----------|
| 200 OK | Successful GET requests |
| 201 Created | Subscription successfully created |
| 400 Bad Request | Invalid request format or Maxio API error |
| 401 Unauthorized | Missing or invalid JWT token |
| 404 Not Found | User not found in database |
| 500 Internal Server Error | Unexpected server error (check logs) |

## Troubleshooting

### Issue: "Unauthorized" on subscription endpoints
- **Cause**: Missing or invalid JWT token
- **Solution**: Ensure token is passed in `Authorization: Bearer <token>` header
- **Debug**: Check that user logged in successfully with `/api/authenticate` endpoint

### Issue: "User not found"
- **Cause**: User ID in database doesn't match JWT
- **Solution**: Ensure you're using the same user for authentication and subscription
- **Debug**: Check eShopOnWeb user database for the user

### Issue: Maxio API errors (400+ from Maxio)
- **Cause**: Invalid plan handle, API credentials, or Maxio configuration
- **Solution**: 
  - Verify `Maxio:ApiKey` is set correctly in user-secrets
  - Verify product handles match seeded plans in Maxio ("eshop-pro", "basic-plan")
  - Check Maxio sandbox site (cp-exp-2) for plan availability
- **Debug**: Check application logs for detailed Maxio API error messages

### Issue: "UseOnlyInMemoryDatabase" not recognized
- **Cause**: Environment variable or setting not properly set
- **Solution**: 
  - For PowerShell: `$env:UseOnlyInMemoryDatabase="true"` then run app
  - For Command Prompt: `set UseOnlyInMemoryDatabase=true` then run app
  - Or add to `appsettings.Development.json`

## Architecture Notes

### Key Classes

- **MaxioService** (`src/Infrastructure/Services/MaxioService.cs`): Handles all Maxio API communication
- **Subscription Endpoints** (`src/PublicApi/SubscriptionEndpoints/`):
  - `ListSubscriptionPlansEndpoint.cs` - GET /api/subscription-plans
  - `CreateSubscriptionEndpoint.cs` - POST /api/subscriptions
  - `ListUserSubscriptionsEndpoint.cs` - GET /api/my-subscriptions

### Flow

1. **Authentication**: User calls `/api/authenticate` → eShopOnWeb issues JWT
2. **List Plans**: User calls `/api/subscription-plans` with JWT → MaxioService fetches from Maxio
3. **Subscribe**: User calls `/api/subscriptions` with JWT + product handle
   - Extract user ID from JWT claims
   - Get/create Maxio customer (using eShopOnWeb user ID as customer reference)
   - Create subscription in Maxio
   - Return subscription details to user
4. **View Subscriptions**: User calls `/api/my-subscriptions` with JWT → Fetch customer's subscriptions from Maxio

### Data Persistence

**Subscriptions**: Stored in Maxio (system of record) - eShopOnWeb has no local subscription table
**Customers**: Stored in Maxio with `customer.reference` set to eShopOnWeb user ID for correlation
**User Info**: Stored in eShopOnWeb identity database

## Next Steps for Production

1. **Certificate Management**: Ensure dev HTTPS cert is trusted or use proper certificate
2. **Secrets Management**: Use secure vault (Azure Key Vault, AWS Secrets Manager) instead of local user-secrets
3. **Database**: Migrate from in-memory to production SQL Server or compatible database
4. **Error Handling**: Implement detailed logging and monitoring for Maxio API failures
5. **Payment Methods**: When moving beyond sandbox, handle payment profile collection (via Billing.js or similar)
6. **Rate Limiting**: Add rate limiting to subscription endpoints to prevent abuse
7. **Audit Logging**: Track subscription changes with audit trail for compliance

## Support

For Maxio API documentation, see: https://maxio-billing.mintlify.app/
For eShopOnWeb reference architecture, see: https://github.com/dotnet-architecture/eShopOnWeb
