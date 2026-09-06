# Maxio Subscription Integration Verification Guide

This guide provides step-by-step instructions to verify that the subscription billing integration with Maxio Advanced Billing is working correctly.

## Prerequisites

- .NET 8 SDK or later (or .NET 10 SDK with rollForward enabled)
- Maxio sandbox credentials already configured in user-secrets:
  - `Maxio:ApiKey`
  - `Maxio:Subdomain` (cp-exp-3 for sandbox)
  - `Maxio:ProductFamilyHandle` (eshop-subscribe)

Verify configuration is set:
```bash
cd src/PublicApi
dotnet user-secrets list
```

Expected output should include all three Maxio settings.

## Step 1: Build and Run

### Build the PublicApi

```bash
cd src/PublicApi
dotnet build
```

### Run the PublicApi Service

```bash
# Option A: With in-memory database (for testing without SQL Server)
export UseOnlyInMemoryDatabase=true
export DOTNET_ROLL_FORWARD=Major
dotnet run

# Option B: With SQL Server (if LocalDB is available)
dotnet run
```

The API will start at `https://localhost:5002` (or check the console for the actual port).

## Step 2: Authenticate

### Get an Authentication Token

First, you need to authenticate to get a JWT token. Use the authenticate endpoint:

```bash
curl -X POST "https://localhost:5002/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  -k
```

**Note:** The `-k` flag ignores SSL certificate validation for dev certificates.

Expected response:
```json
{
  "correlationId": "uuid",
  "result": true,
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "username": "demouser@microsoft.com",
  "token": "eyJhbGciOiJIUzI1NiIs..."
}
```

Copy the `token` value for use in the following requests.

## Step 3: Test Subscription Endpoints

### Store the Token
```bash
export TOKEN="<paste-token-here>"
```

### 3A. List Available Subscription Plans

```bash
curl -X GET "https://localhost:5002/api/subscription-plans" \
  -H "Authorization: Bearer $TOKEN" \
  -k
```

Expected response:
```json
{
  "correlationId": "uuid",
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription plan",
      "defaultPrice": "299.00"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic subscription plan",
      "defaultPrice": "29.00"
    }
  ]
}
```

### 3B. Subscribe to a Plan

Subscribe the authenticated user to the "Pro" plan:

```bash
curl -X POST "https://localhost:5002/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  -k
```

Expected response (201 Created):
```json
{
  "correlationId": "uuid",
  "success": true,
  "subscription": {
    "id": 12345678,
    "customerId": 87654321,
    "productId": 7126957,
    "state": "active",
    "currentPeriodStartsAt": "2026-09-07T00:00:00Z",
    "nextBillingAt": "2026-10-07T00:00:00Z",
    "createdAt": "2026-09-07T12:34:56Z",
    "updatedAt": "2026-09-07T12:34:56Z"
  }
}
```

**Note:** On the in-memory database, the subscription data will be lost when the service restarts.

### 3C. Retrieve User's Subscriptions

Get all subscriptions for the authenticated user:

```bash
curl -X GET "https://localhost:5002/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -k
```

Expected response:
```json
{
  "correlationId": "uuid",
  "success": true,
  "subscriptions": [
    {
      "id": 12345678,
      "customerId": 87654321,
      "productId": 7126957,
      "state": "active",
      "currentPeriodStartsAt": "2026-09-07T00:00:00Z",
      "nextBillingAt": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:34:56Z",
      "updatedAt": "2026-09-07T12:34:56Z"
    }
  ]
}
```

## Step 4: Verify Key Features

### 4A. Idempotent Customer Creation

Call the subscribe endpoint twice with the same user (same JWT token):

```bash
# First call - creates customer and subscription
curl -X POST "https://localhost:5002/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}' \
  -k

# Second call - should reuse the same customer
curl -X POST "https://localhost:5002/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "basic-plan"}' \
  -k
```

Expected behavior:
- First call creates a Maxio customer and subscription
- Second call reuses the same Maxio customer (no duplicate customers created)
- User can have multiple subscriptions

### 4B. Error Handling

Try subscribing with an invalid plan handle:

```bash
curl -X POST "https://localhost:5002/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "invalid-plan"}' \
  -k
```

Expected response (404):
```json
{
  "correlationId": "uuid",
  "success": false,
  "subscription": null,
  "errorMessage": "Plan 'invalid-plan' not found"
}
```

### 4C. Authentication Requirement

Try accessing subscription endpoints without a token:

```bash
curl -X GET "https://localhost:5002/api/my-subscriptions" \
  -k
```

Expected response (401 Unauthorized) - no response body or authentication error.

## Step 5: Check Logs

Monitor the console output for:
- Successful Maxio API calls
- Customer creation/lookup logs
- Subscription creation logs
- Error handling and exception messages

Look for log entries like:
```
info: Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.MaxioSubscriptionService
      Retrieved 2 subscription plans from Maxio

info: Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.MaxioSubscriptionService
      Found existing Maxio customer for email demouser@microsoft.com with ID 87654321

info: Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints.MaxioSubscriptionService
      Created subscription with ID 12345678 for customer 87654321
```

## Troubleshooting

### Issue: "Maxio:ApiKey not configured"
**Solution:** Ensure user-secrets are properly set:
```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-3"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### Issue: SSL Certificate Error
**Solution:** Trust the development certificate or use the `-k` flag with curl to skip certificate validation.

### Issue: "401 Unauthorized" on subscription endpoints
**Solution:** Ensure you're passing a valid JWT bearer token. The token must be obtained by authenticating at `/api/authenticate`.

### Issue: Database state lost after restart (in-memory database)
**Solution:** This is expected behavior with `UseOnlyInMemoryDatabase=true`. To persist data, set up SQL Server or use Azure SQL.

### Issue: Connection to Maxio fails
**Solution:** Verify:
- API key is correct
- Subdomain is correct (cp-exp-3 for sandbox)
- Network connectivity to chargify.com
- Maxio API is accessible from your location

## Success Criteria

The integration is working correctly when:

✅ All subscription endpoints return 200/201 responses with valid data  
✅ Plans are correctly retrieved from Maxio  
✅ Customers are created idempotently (no duplicates)  
✅ Subscriptions are created and can be listed  
✅ Proper error messages are returned for invalid requests  
✅ Authentication is enforced on all subscription endpoints  
✅ Logs show successful Maxio API interactions  
✅ No unhandled exceptions or SDK errors

## Architecture Summary

The subscription integration consists of:

- **MaxioSubscriptionService** (`SubscriptionEndpoints/MaxioSubscriptionService.cs`)
  - Core business logic for Maxio interactions
  - Handles customer creation/lookup (idempotent)
  - Creates and retrieves subscriptions
  - Manages error handling and logging

- **Three REST Endpoints**
  - `GET /api/subscription-plans` - List available plans
  - `POST /api/subscriptions` - Create subscription for user
  - `GET /api/my-subscriptions` - Get user's active subscriptions

- **Request/Response DTOs**
  - `SubscriptionPlanDto` - Plan information
  - `SubscriptionDto` - Subscription details
  - `SubscribeRequest` / `SubscribeResponse` - Endpoint payloads

- **Error Handling**
  - SDK exception types mapped to meaningful error messages
  - HTTP status codes aligned with error severity
  - Logging for debugging and monitoring

## Next Steps

To integrate with the web frontend:
1. Add subscription management UI to the Web project
2. Implement subscription plan selection and checkout flow
3. Add subscription status display in user account
4. Implement webhook handlers for Maxio events (optional)
5. Add subscription analytics and reporting (optional)
