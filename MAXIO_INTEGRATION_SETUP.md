# Maxio Subscription Billing Integration Setup & Verification Guide

This document provides step-by-step instructions for setting up and verifying the Maxio subscription billing integration with eShopOnWeb.

## Prerequisites

- .NET 8.0 SDK (with DOTNET_ROLL_FORWARD=Major for SDK/runtime mismatch)
- Maxio sandbox account with credentials for site `cp-exp-2`
- API credentials with access to the billing API

## 1. Environment Setup

### Set Environment Variables

Before running the application, set the following environment variables with your Maxio credentials:

```bash
# Unix/Linux/macOS
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Windows (PowerShell)
$env:MAXIO_API_KEY="your-api-key"
$env:MAXIO_SITE_SUBDOMAIN="cp-exp-2"
$env:MAXIO_ENVIRONMENT="sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Windows (Command Prompt)
set MAXIO_API_KEY=your-api-key
set MAXIO_SITE_SUBDOMAIN=cp-exp-2
set MAXIO_ENVIRONMENT=sandbox
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### Alternative: Use .NET User Secrets (Development Only)

For development, you can store secrets securely using .NET user-secrets:

```bash
cd src/PublicApi

# Initialize if not already done (check output indicates if already initialized)
dotnet user-secrets init

# Set Maxio credentials
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

## 2. Running the Application

### PublicApi Service

The subscription endpoints are exposed through the PublicApi service.

```bash
cd src/PublicApi

# Ensure environment variables are set, then run:
# With SDK/runtime mismatch handling:
DOTNET_ROLL_FORWARD=Major dotnet run

# Or with UseOnlyInMemoryDatabase setting:
dotnet run -- --UseOnlyInMemoryDatabase=true
```

The API will start on ports specified in `launchSettings.json` (typically `https://localhost:25483`).

## 3. Available Endpoints

All endpoints require JWT Bearer token authentication.

### 3.1 List Subscription Plans
**Endpoint:** `GET /api/subscription-plans`

**Authentication:** JWT Bearer token (from `/api/authenticate`)

**Example:**
```bash
curl -X GET https://localhost:25483/api/subscription-plans \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json"
```

**Response:**
```json
{
  "success": true,
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription",
      "priceInCents": 29900,
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic subscription",
      "priceInCents": 2900,
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 3.2 Create Subscription
**Endpoint:** `POST /api/subscriptions`

**Authentication:** JWT Bearer token

**Request Body:**
```json
{
  "productHandle": "eshop-pro"
}
```

**Example:**
```bash
curl -X POST https://localhost:25483/api/subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}'
```

**Response:**
```json
{
  "success": true,
  "message": "Subscription created successfully",
  "subscriptionId": 12345678,
  "customerId": 87654321,
  "state": "active",
  "productHandle": "eshop-pro",
  "nextBillingAt": "2026-10-06T00:00:00Z",
  "currentPeriodEndsAt": "2026-10-06T00:00:00Z"
}
```

### 3.3 List User's Subscriptions
**Endpoint:** `GET /api/my-subscriptions`

**Authentication:** JWT Bearer token

**Example:**
```bash
curl -X GET https://localhost:25483/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json"
```

**Response:**
```json
{
  "success": true,
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "customerId": 87654321,
      "productHandle": "eshop-pro",
      "state": "active",
      "balanceInCents": 0,
      "balance": 0.00,
      "nextBillingAt": "2026-10-06T00:00:00Z",
      "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
      "createdAt": "2026-09-06T12:00:00Z",
      "updatedAt": "2026-09-06T12:00:00Z"
    }
  ]
}
```

## 4. Integration Flow

### Hero Flow: Subscribe

1. **Authenticate User**
   - User logs in via `/api/authenticate` endpoint
   - Receives JWT token in response

2. **List Plans**
   - Call `GET /api/subscription-plans` with JWT token
   - Display available plans to user

3. **Create Subscription**
   - User selects a plan and submits subscription form
   - Call `POST /api/subscriptions` with `productHandle` parameter
   - On backend:
     - Extract user ID from JWT claim
     - Create/retrieve Maxio customer using user reference (idempotent)
     - Create subscription in Maxio
     - Return subscription details and next billing date to user

4. **View Subscriptions**
   - Call `GET /api/my-subscriptions` to display user's active subscriptions
   - Show plan names, next billing dates, and current status

## 5. Verification Checklist

### Build Verification
```bash
cd repo
dotnet build src/PublicApi/PublicApi.csproj -c Debug
```
✓ Build completes successfully with no errors

### Configuration Verification
- [ ] Environment variables or user-secrets are configured with Maxio credentials
- [ ] `appsettings.json` contains Maxio section with keys:
  - `ApiKey` (empty by default, set via env var or user-secrets)
  - `Subdomain` (default: "cp-exp-2")
  - `ProductFamilyHandle` (default: "eshop-subscribe")
  - `BaseUrl` (optional override)

### Runtime Verification

1. **Start PublicApi Service**
   ```bash
   cd src/PublicApi
   DOTNET_ROLL_FORWARD=Major dotnet run
   ```

2. **Get JWT Token**
   ```bash
   # First, register/login a user
   curl -X POST https://localhost:25483/api/authenticate \
     -H "Content-Type: application/json" \
     -d '{
       "username": "testuser@example.com",
       "password": "YourPassword123!"
     }'
   ```
   Save the returned JWT token

3. **Test List Plans Endpoint**
   ```bash
   curl -X GET https://localhost:25483/api/subscription-plans \
     -H "Authorization: Bearer TOKEN_FROM_STEP_2" \
     -H "Content-Type: application/json"
   ```
   ✓ Response contains list of subscription plans

4. **Test Create Subscription Endpoint**
   ```bash
   curl -X POST https://localhost:25483/api/subscriptions \
     -H "Authorization: Bearer TOKEN_FROM_STEP_2" \
     -H "Content-Type: application/json" \
     -d '{"productHandle": "eshop-pro"}'
   ```
   ✓ Response contains subscription details and confirmation

5. **Test List Subscriptions Endpoint**
   ```bash
   curl -X GET https://localhost:25483/api/my-subscriptions \
     -H "Authorization: Bearer TOKEN_FROM_STEP_2" \
     -H "Content-Type: application/json"
   ```
   ✓ Response contains the subscription created in step 4

## 6. Key Implementation Details

### Authentication
- All endpoints use JWT Bearer authentication
- User identity extracted from JWT claims (NameIdentifier, Email, etc.)
- Customer reference in Maxio is the eShopOnWeb user ID for consistency

### Idempotent Customer Creation
- `GetOrCreateCustomerAsync()` first tries to look up customer by reference
- If not found, creates new customer
- This ensures double-clicks don't create duplicate customers

### Sandbox Testing
- Uses Maxio sandbox at `cp-exp-2`
- Pre-seeded catalog with:
  - Product Family: `eshop-subscribe`
  - Pro Plan: `eshop-pro` ($299/month)
  - Basic Plan: `basic-plan` ($29/month)
  - No payment method required
  - No trial period

### Error Handling
- HTTP Basic auth with Maxio API (API key as username, "X" as password)
- Errors logged to console/configured logger
- Graceful error responses to client

## 7. Troubleshooting

### Connection Errors
**Error:** "Failed to connect to Maxio API"
- Verify `MAXIO_API_KEY` environment variable is set
- Verify `MAXIO_SITE_SUBDOMAIN` is correct (should be "cp-exp-2")
- Check internet connectivity

### Authentication Errors
**Error:** "Unauthorized" from Maxio
- Verify API key is valid for the sandbox site
- Check that credentials haven't expired

### User Not Authenticated
**Error:** "User not authenticated" when calling subscription endpoints
- Ensure JWT token is included in Authorization header
- Token format: `Authorization: Bearer <token>`
- Token must be issued by `/api/authenticate` endpoint

### Product Family Not Found
**Error:** "No plans returned" from list plans endpoint
- Verify `MAXIO_DEFAULT_PRODUCT_FAMILY` is set to "eshop-subscribe"
- Check that product family exists in Maxio sandbox

## 8. Architecture Notes

### Services
- `MaxioClient`: Handles all Maxio API communication
  - Configures HTTP Basic auth automatically
  - Provides high-level methods: GetOrCreateCustomerAsync, CreateSubscriptionAsync, etc.
  - Graceful error handling with logging

### Endpoints (in `/SubscriptionEndpoints`)
- `SubscriptionPlansEndpoint`: Lists available plans from product family
- `CreateSubscriptionEndpoint`: Creates subscription for authenticated user
- `ListSubscriptionsEndpoint`: Lists user's subscriptions

### DTOs
- Request/Response DTOs follow existing eShopWeb patterns
- BaseRequest/BaseResponse for consistency
- Snake_case JSON serialization for Maxio API compatibility

### Configuration
- `MaxioConfiguration` class binds to "Maxio" section in appsettings
- Supports override via `BaseUrl` for custom endpoints
- Defaults use standard Maxio sandbox URLs

## 9. Future Enhancements

- Webhook handling for subscription lifecycle events
- Subscription cancellation endpoint
- Plan upgrade/downgrade endpoints
- Metered component tracking for API usage
- Billing portal integration for customer self-service
