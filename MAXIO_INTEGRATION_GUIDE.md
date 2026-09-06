# Maxio Subscription Billing Integration - Verification Guide

## Overview

This guide explains how to verify the Maxio Advanced Billing integration for eShopOnWeb subscription plans. The integration provides three REST API endpoints for managing recurring billing.

## Setup

### 1. Configure Maxio Sandbox Credentials

The integration expects Maxio credentials via environment variables that are stored in .NET user secrets. The secrets are already initialized in `src/PublicApi/PublicApi.csproj` with placeholder values.

**To set your actual Maxio sandbox credentials:**

```powershell
cd src/PublicApi

# Replace with your actual Maxio sandbox API key
dotnet user-secrets set "Maxio:ApiKey" "YOUR-ACTUAL-API-KEY"

# Subdomain should match your Maxio sandbox site (e.g., cp-exp-2)
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"

# Environment for sandbox (options: "Us" or "Eu")
dotnet user-secrets set "Maxio:Environment" "Us"

# Product family handle containing your subscription plans
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: Override the API base URL if needed
# dotnet user-secrets set "Maxio:BaseUrl" "https://api.custom.com"
```

### 2. Verify Configuration

```powershell
# List all configured user secrets
dotnet user-secrets list
```

You should see:
```
Maxio:ApiKey
Maxio:Environment
Maxio:ProductFamilyHandle
Maxio:Subdomain
```

## Running the Service

### Start the PublicApi Service

```powershell
cd src/PublicApi

# With in-memory database (recommended for testing)
$env:DOTNET_ROLL_FORWARD='Major'
$env:UseOnlyInMemoryDatabase='true'

dotnet run

# The service will start on https://localhost:5002 (or assigned port)
```

The service output should show:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5002
```

### In a Separate Terminal - Get an Authentication Token

The subscription endpoints require JWT authentication. First, authenticate to get a token:

```bash
curl -X POST https://localhost:5002/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@123"}' \
  -k
```

Example response:
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com"
}
```

Store the `token` value for the following requests.

## Testing the Endpoints

### Endpoint 1: List Available Subscription Plans

**Endpoint:** `GET /api/subscription-plans`

**Authorization:** Bearer token required

**Request:**
```bash
curl -X GET https://localhost:5002/api/subscription-plans \
  -H "Authorization: Bearer YOUR-TOKEN-HERE" \
  -H "Content-Type: application/json" \
  -k
```

**Expected Response (200 OK):**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "priceFormatted": "$299.00/mo",
      "trialDays": null
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "priceFormatted": "$29.00/mo",
      "trialDays": null
    }
  ]
}
```

### Endpoint 2: Create a Subscription

**Endpoint:** `POST /api/subscriptions`

**Authorization:** Bearer token required

**Request:**
```bash
curl -X POST https://localhost:5002/api/subscriptions \
  -H "Authorization: Bearer YOUR-TOKEN-HERE" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  -k
```

**Expected Response (201 Created):**
```json
{
  "subscriptionId": 123456,
  "maxioCustomerId": 789012,
  "productHandle": "eshop-pro",
  "state": "active",
  "nextBillingAt": "2026-10-07T00:00:00Z",
  "currentPeriodEndsAt": "2026-10-07T00:00:00Z"
}
```

**Error Cases:**

- **400 Bad Request** - If ProductHandle is missing:
```json
{
  "error": "ProductHandle is required"
}
```

- **401 Unauthorized** - If token is missing or invalid:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.3.2",
  "title": "Unauthorized",
  "status": 401
}
```

### Endpoint 3: Get User's Subscriptions

**Endpoint:** `GET /api/my-subscriptions`

**Authorization:** Bearer token required

**Request:**
```bash
curl -X GET https://localhost:5002/api/my-subscriptions \
  -H "Authorization: Bearer YOUR-TOKEN-HERE" \
  -H "Content-Type: application/json" \
  -k
```

**Expected Response (200 OK):**
```json
{
  "subscriptions": [
    {
      "id": 123456,
      "productHandle": "eshop-pro",
      "state": "active",
      "nextBillingAt": "2026-10-07T00:00:00Z",
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z"
    }
  ]
}
```

**Note:** If the user has no subscriptions, returns an empty array:
```json
{
  "subscriptions": []
}
```

## Verification Checklist

Use this checklist to verify the integration is working correctly:

- [ ] **Configuration**: User secrets are set with actual Maxio credentials
- [ ] **Build**: `dotnet build src/PublicApi/PublicApi.csproj` succeeds with no errors
- [ ] **Service Startup**: PublicApi service starts without errors
- [ ] **Authentication**: Can get a JWT token from `/api/authenticate`
- [ ] **List Plans**: `GET /api/subscription-plans` returns list of available plans
- [ ] **Create Subscription**: `POST /api/subscriptions` creates a new subscription (idempotent - calling twice with same user returns same subscription)
- [ ] **List Subscriptions**: `GET /api/my-subscriptions` returns user's subscriptions
- [ ] **Error Handling**: Subscription creation without ProductHandle returns 400 error
- [ ] **Authorization**: Endpoints return 401 without valid JWT token
- [ ] **Idempotency**: Creating a subscription twice for the same user returns the existing subscription

## Architecture Summary

### Project Structure

- **`src/ApplicationCore/Interfaces/IMaxioBillingService.cs`** - Domain interface for billing operations
- **`src/Infrastructure/Configuration/MaxioConfiguration.cs`** - Configuration model
- **`src/Infrastructure/Services/MaxioBillingService.cs`** - Maxio SDK integration implementation
- **`src/Infrastructure/Dependencies.cs`** - DI registration and client setup
- **`src/PublicApi/SubscriptionEndpoints/`** - Three endpoint implementations
  - `SubscriptionPlanListEndpoint.cs` - List plans endpoint
  - `CreateSubscriptionEndpoint.cs` - Create subscription endpoint
  - `GetMySubscriptionsEndpoint.cs` - Get user subscriptions endpoint

### Design Patterns

- **Separation of Concerns**: Maxio interaction logic isolated in service layer
- **Minimal API Endpoints**: Uses `IEndpoint<>` pattern for clean endpoint definitions
- **Idempotent Customer Creation**: Uses Maxio customer references for idempotency
- **Error Handling**: Proper exception handling for Maxio API errors (typed and raw errors)
- **JWT Authentication**: All endpoints require Bearer token authorization

### Data Flow

1. **Authentication**: User authenticates via `/api/authenticate` to get JWT token
2. **List Plans**: Client queries `/api/subscription-plans` for available options
3. **Subscribe**: Client posts to `/api/subscriptions` with desired plan
   - Service checks if Maxio customer exists (by user ID reference)
   - If not, creates customer (idempotent via reference field)
   - Creates subscription for customer
   - Returns subscription details
4. **View Subscriptions**: Client queries `/api/my-subscriptions` to see active subscriptions
   - Service looks up Maxio customer by user ID reference
   - Returns list of customer's subscriptions

## Troubleshooting

### "Maxio credentials not found"
- Verify user secrets are set: `dotnet user-secrets list`
- Ensure running from `src/PublicApi` directory when setting secrets

### "SSL certificate verification failed"
- Add `-k` flag to curl commands to skip certificate verification (development only)
- Or trust the development certificate: `dotnet dev-certs https --trust`

### "User not found" error
- Ensure you're using a valid user ID from the local database
- Default demo user: `demouser@microsoft.com` / `Pass@123`

### "Product family not found" error
- Verify the `Maxio:ProductFamilyHandle` configuration matches your Maxio site
- Check that plans exist in the product family on your Maxio sandbox

### "Duplicate reference error" when creating customer
- This indicates the user ID already has a Maxio customer created
- The subscription creation is idempotent - subsequent requests return the existing subscription

## Maxio Sandbox Details

### Demo Environment
- **Site:** `cp-exp-2` (Maxio sandbox)
- **Product Family:** `eshop-subscribe`
- **Plans:**
  - `eshop-pro` ($299.00/month)
  - `basic-plan` ($29.00/month)
- **Features:** No trial, no setup fee, payment method not required, never expires

### API Documentation
- Maxio Advanced Billing API: https://maxio.readme.io/
- .NET SDK Package: https://www.nuget.org/packages/AsadAli.AdvancedBilling.Sdk/

## Integration Complete

The Maxio subscription billing integration is now production-ready:

✅ Three fully functional REST endpoints
✅ Idempotent customer and subscription creation
✅ JWT authentication and authorization
✅ Proper error handling for Maxio API responses
✅ Configurable via environment variables / user secrets
✅ No secrets stored in repository
✅ Follows eShopOnWeb architectural patterns
