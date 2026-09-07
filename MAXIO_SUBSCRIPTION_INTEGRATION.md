# Maxio Subscription Billing Integration for eShopOnWeb

## Overview

This document describes the Maxio Advanced Billing integration added to eShopOnWeb. The integration adds recurring subscription capability alongside the existing one-time commerce feature.

## What Was Implemented

### 1. **Subscription Service** (`MaxioSubscriptionService.cs`)
- Manages all communication with Maxio API
- Ensures idempotent customer creation (creates or retrieves existing customer by UserId)
- Provides subscription lifecycle management
- Includes comprehensive error handling

### 2. **Subscription Endpoints** (PublicApi)
All endpoints are JWT-authenticated (except plan list which is public):

#### **GET /api/subscription-plans**
- **Access**: Public (no authentication required)
- **Returns**: List of available subscription plans from the configured product family
- **Response**:
  ```json
  {
    "plans": [
      {
        "id": 7126957,
        "name": "Pro Plan",
        "handle": "eshop-pro",
        "price": 299.00,
        "interval": 1,
        "intervalUnit": "Month"
      },
      {
        "id": 7126958,
        "name": "Basic Plan",
        "handle": "basic-plan",
        "price": 29.00,
        "interval": 1,
        "intervalUnit": "Month"
      }
    ],
    "correlationId": "..."
  }
  ```

#### **POST /api/subscriptions**
- **Access**: JWT-authenticated (reads userId from token `sub` claim)
- **Request Body**:
  ```json
  {
    "planHandle": "eshop-pro"
  }
  ```
- **Returns**: Created subscription details
- **Response**:
  ```json
  {
    "subscription": {
      "id": 12345678,
      "state": "active",
      "productPrice": 299.00,
      "nextBillingDate": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T15:30:00Z",
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z"
    },
    "correlationId": "..."
  }
  ```

#### **GET /api/my-subscriptions**
- **Access**: JWT-authenticated
- **Returns**: All active subscriptions for the current user
- **Response**:
  ```json
  {
    "subscriptions": [
      {
        "id": 12345678,
        "state": "active",
        "productPrice": 299.00,
        "nextBillingDate": "2026-10-07T00:00:00Z",
        "createdAt": "2026-09-07T15:30:00Z",
        "currentPeriodEndsAt": "2026-10-07T00:00:00Z"
      }
    ],
    "correlationId": "..."
  }
  ```

## Configuration

### Environment Variables Required
The following environment variables must be set (already configured in your environment):
```bash
MAXIO_API_KEY=<your-api-key>
MAXIO_SITE_SUBDOMAIN=cp-exp-4
MAXIO_ENVIRONMENT=US
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### .NET User Secrets
The integration reads configuration from .NET user-secrets (already configured):
```bash
Maxio:ApiKey          # From MAXIO_API_KEY
Maxio:Subdomain       # From MAXIO_SITE_SUBDOMAIN
Maxio:ProductFamilyHandle  # From MAXIO_DEFAULT_PRODUCT_FAMILY
Maxio:BaseUrl         # Optional override (if set, uses this instead of constructing from subdomain)
```

### Verify Secrets Are Configured
```bash
cd src/PublicApi
dotnet user-secrets list
```

## Verification Procedure

### Step 1: Build the Solution
```bash
cd <repo-root>
dotnet build eShopOnWeb.sln
# Expected: Build succeeded with 0 errors
```

### Step 2: Start the PublicApi Service
```bash
cd src/PublicApi
dotnet run --launch-profile PublicApi
# Expected output: "LAUNCHING PublicApi" followed by listening on https://localhost:28883
```

### Step 3: Get an Authentication Token
Open a new terminal and authenticate:
```bash
curl -X POST https://localhost:28883/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  --insecure  # For local development only
```

**Expected Response**:
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com",
  "correlationId": "..."
}
```

**Save the token for the next steps**: `TOKEN=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...`

### Step 4: List Available Subscription Plans
```bash
curl -X GET https://localhost:28883/api/subscription-plans \
  --insecure
```

**Expected Response**: 200 OK with list of plans (Pro Plan at $299/mo, Basic Plan at $29/mo)

### Step 5: Create a Subscription
```bash
curl -X POST https://localhost:28883/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  --insecure
```

**Expected Response**: 201 Created with subscription details including:
- subscription.id (Maxio subscription ID)
- subscription.state = "active"
- subscription.productPrice = 299.00
- subscription.nextBillingDate (30 days from now)

### Step 6: Retrieve User Subscriptions
```bash
curl -X GET https://localhost:28883/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Expected Response**: 200 OK with the subscription created in Step 5

### Step 7: Verify Idempotency - Create Same Subscription Again
Run Step 5 again with the same user and plan:
```bash
curl -X POST https://localhost:28883/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  --insecure
```

**Expected Behavior**: 
- First call: Creates a new subscription (201 Created)
- Second call: Succeeds but reuses existing Maxio customer (customer reference is unique per userId)
- Same subscription ID may or may not appear twice depending on Maxio's deduplication

## Architecture & Design Decisions

### Idempotency
- **Customer Creation**: Uses Maxio customer reference (eShopOnWeb UserId) to ensure each user maps to exactly one Maxio customer
  - If customer exists, retrieval succeeds and reuses it
  - If customer doesn't exist, a new one is created
  - UserId must remain stable throughout the user's account lifetime

- **Subscription Creation**: Uses customer reference (not customer ID) to enable API-level idempotency
  - Maxio creates subscription from the customer reference if not already present

### Error Handling
- **Connection Failures**: Surfaces as 503 Service Unavailable
- **API Errors**: Surfaces as 503 Service Unavailable with error details
- **Invalid Plans**: Surfaces as 422 or 503 depending on Maxio response

### No Payment Method Required
The sandbox configuration for both plans (`eshop-pro` and `basic-plan`) does not require payment method entry:
- Subscriptions activate immediately without card capture
- In production, you would configure payment requirements on each plan

## Implementation Details

### Files Added
- `src/PublicApi/MaxioSubscriptionService.cs` - Maxio SDK wrapper service
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlansListEndpoint.cs` - GET /api/subscription-plans
- `src/PublicApi/SubscriptionEndpoints/SubscriptionCreateEndpoint.cs` - POST /api/subscriptions
- `src/PublicApi/SubscriptionEndpoints/SubscriptionGetEndpoint.cs` - GET /api/my-subscriptions

### Files Modified
- `Directory.Packages.props` - Added AsadAli.AdvancedBilling.Sdk v1.0.2
- `src/PublicApi/PublicApi.csproj` - Added SDK package reference
- `src/PublicApi/Program.cs` - Registered MaxioSubscriptionService in DI, added user-secrets configuration

### Dependencies
- **AsadAli.AdvancedBilling.Sdk v1.0.2** - Maxio Advanced Billing API SDK

### SDK Operations Used
1. `ProductFamilies.ListProductsForProductFamily()` - Discover plans
2. `Customers.ReadCustomerByReference()` - Check if customer exists
3. `Customers.CreateCustomer()` - Create new customer if needed
4. `Subscriptions.CreateSubscription()` - Create/activate subscription
5. `Subscriptions.ListSubscriptions()` - Query user subscriptions

## Troubleshooting

### "Failed to fetch subscription plans" (503)
- **Cause**: Maxio API unreachable or invalid credentials
- **Fix**: Verify environment variables are set correctly and Maxio sandbox is accessible

### "Failed to create subscription" (503)
- **Cause**: Invalid plan handle, customer creation failed, or subscription already exists
- **Fix**: Verify plan handle matches sandbox product ("eshop-pro" or "basic-plan"), ensure customer can be created

### 401 Unauthorized on Subscriptions endpoints
- **Cause**: Missing or invalid JWT token
- **Fix**: Re-run authentication endpoint to get valid token, include it in Authorization header

### Certificate errors with --insecure
- **Cause**: Development HTTPS certificate not trusted
- **Fix**: Install dev certificate: `dotnet dev-certs https --trust`

## Production Readiness

To deploy to production:

1. **Secrets Management**: Move from user-secrets to secure environment (Azure KeyVault, HashiCorp Vault, etc.)
2. **Plan Configuration**: Update product family handle and plan handles to your production Maxio site
3. **Payment Methods**: Configure payment method requirements on your production plans
4. **Error Handling**: Customize error messages for customer-facing responses
5. **Logging**: Review logging configuration and ensure Maxio operations are logged appropriately
6. **Resilience**: Consider retry policies and circuit breakers for Maxio API calls
7. **Testing**: Add integration tests for Maxio operations

## Support

For issues with:
- **eShopOnWeb architecture**: See eShopOnWeb documentation
- **Maxio API**: See Maxio Advanced Billing API documentation
- **SDK integration**: Refer to maxio-plan.md for contract details
