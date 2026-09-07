# Maxio Subscription Billing Integration - Verification Guide

## Overview

This document provides step-by-step instructions to verify the working Maxio subscription billing integration added to eShopOnWeb.

## Prerequisites

- .NET SDK (v8.0 or higher, rollForward: latestMajor)
- ASP.NET Core 8.0 runtime or higher
- Maxio sandbox account with credentials configured
- Environment variables for Maxio configuration (see Setup section)

## Setup: Configure Maxio Credentials

### 1. Set Environment Variables

Before running, configure the following environment variables with your Maxio sandbox credentials:

```bash
# Windows (PowerShell)
$env:MAXIO_API_KEY="your_maxio_api_key"
$env:MAXIO_SITE_SUBDOMAIN="your_sandbox_subdomain"
$env:MAXIO_ENVIRONMENT="sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Or add to .NET user-secrets for development
dotnet user-secrets set "Maxio:ApiKey" "your_maxio_api_key"
dotnet user-secrets set "Maxio:Subdomain" "your_sandbox_subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### 2. Database Configuration

The integration uses an in-memory database by default when `UseOnlyInMemoryDatabase=true` is set in environment variables. Note that the in-memory database loses all data on application restart.

For development with SQL Server LocalDB:
```bash
$env:UseOnlyInMemoryDatabase="false"
```

## Running the Integration

### 1. Start the PublicApi Service

```bash
cd src/PublicApi
dotnet run
```

The API will start on `https://localhost:28923/` (or as configured in appsettings.json).

### 2. Access Swagger UI

Open your browser to: `https://localhost:28923/swagger/index.html`

This displays all available endpoints, including the subscription endpoints.

## Verification Flows

### Flow 1: Authenticate and Get JWT Token

**Endpoint**: `POST /api/authenticate`

1. Open Swagger UI
2. Find "AuthEndpoints" section
3. Click "Try it out" on the `/api/authenticate` endpoint
4. Enter credentials:
   ```json
   {
     "username": "demouser@microsoft.com",
     "password": "Pass@word$123"
   }
   ```
5. Execute and copy the `token` from the response

### Flow 2: List Subscription Plans

**Endpoint**: `GET /api/subscription-plans`

1. In Swagger, find "SubscriptionEndpoints" section
2. Click "Try it out" on `/api/subscription-plans`
3. Execute (no authentication needed for this endpoint)
4. Verify response contains plans:
   - Basic Plan ($29/month)
   - Pro Plan ($299/month)

**Expected Response**:
```json
{
  "plans": [
    {
      "id": 1,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Our basic subscription plan with essential features",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 2,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Our professional plan with advanced features",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "correlationId": "..."
}
```

### Flow 3: Create a Subscription

**Endpoint**: `POST /api/subscriptions`

1. In Swagger, find "SubscriptionEndpoints" section
2. Click on `/api/subscriptions` endpoint
3. Click "Authorize" button (top right)
4. Paste your JWT token from Flow 1: `Bearer <your_token>`
5. Click "Try it out"
6. Enter request body:
   ```json
   {
     "productHandle": "eshop-pro"
   }
   ```
7. Execute

**Expected Response** (201 Created):
```json
{
  "subscription": {
    "id": 0,
    "maxioSubscriptionId": 123456,
    "plan": {
      "id": 0,
      "handle": "eshop-pro",
      "name": "eshop-pro",
      "description": "",
      "priceInCents": 0,
      "interval": 1,
      "intervalUnit": "month"
    },
    "state": "active",
    "currentPeriodStartsAt": "2026-09-07T00:00:00Z",
    "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
    "nextBillingAt": "2026-10-07T00:00:00Z"
  },
  "message": "Subscription created successfully",
  "correlationId": "..."
}
```

**What Happens Behind the Scenes**:
- System creates/retrieves Maxio customer using user's ID as reference
- System creates subscription in Maxio sandbox
- Subscription state is returned as "active"
- Next billing date is approximately 30 days from creation

### Flow 4: List User's Subscriptions

**Endpoint**: `GET /api/my-subscriptions`

1. In Swagger, locate `/api/my-subscriptions` endpoint
2. Click "Authorize" (if not already authorized)
3. Paste your JWT token again if needed
4. Click "Try it out" and Execute

**Expected Response** (200 OK):
```json
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 123456,
      "plan": {
        "id": 2,
        "handle": "eshop-pro",
        "name": "Pro Plan",
        "description": "Our professional plan with advanced features",
        "priceInCents": 29900,
        "interval": 1,
        "intervalUnit": "month"
      },
      "state": "active",
      "currentPeriodStartsAt": "2026-09-07T00:00:00Z",
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z"
    }
  ],
  "correlationId": "..."
}
```

## Testing with cURL

### Get Auth Token
```bash
curl -X POST "https://localhost:28923/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word$123"}' \
  -k
```

### List Plans
```bash
curl -X GET "https://localhost:28923/api/subscription-plans" -k
```

### Create Subscription
```bash
curl -X POST "https://localhost:28923/api/subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  -k
```

### List My Subscriptions
```bash
curl -X GET "https://localhost:28923/api/my-subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

## Verification Checklist

- [ ] PublicApi application builds successfully
- [ ] PublicApi starts without errors
- [ ] `/api/subscription-plans` returns both plans
- [ ] `/api/authenticate` returns a valid JWT token
- [ ] `/api/subscriptions` creates a subscription and returns Maxio subscription ID
- [ ] Subscription state is "active"
- [ ] Next billing date is approximately 30 days in the future
- [ ] `/api/my-subscriptions` retrieves created subscriptions for authenticated user
- [ ] User can subscribe to Pro plan or Basic plan
- [ ] Double-subscribing same user doesn't create duplicate customers in Maxio

## Architecture Overview

### Data Flow

1. **User Authentication**: User authenticates with email/password → JWT token issued
2. **Plan Listing**: Public endpoint lists available subscription plans from database
3. **Subscription Creation**:
   - User provides product handle
   - System looks up existing Maxio customer using user ID as reference
   - If not found, system creates new Maxio customer (idempotent via reference)
   - System creates subscription via Maxio API
   - Subscription data returned to user
4. **Subscription Retrieval**: User retrieves their subscriptions filtered by user ID

### Key Files

**ApplicationCore**:
- `Entities/SubscriptionAggregate/SubscriptionPlan.cs` - Plan entity
- `Entities/SubscriptionAggregate/UserSubscription.cs` - User subscription mapping
- `Interfaces/IMaxioService.cs` - Maxio service interface
- `Services/MaxioService.cs` - Maxio API integration
- `Settings/MaxioSettings.cs` - Configuration
- `Specifications/UserSubscriptionsByUserIdSpec.cs` - Query specification

**PublicApi**:
- `SubscriptionEndpoints/GetSubscriptionPlansEndpoint.cs` - List plans
- `SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - Create subscription
- `SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs` - List user subscriptions
- `SubscriptionEndpoints/SubscriptionPlanDto.cs` - Plan DTO
- `SubscriptionEndpoints/UserSubscriptionDto.cs` - Subscription DTO

**Infrastructure**:
- `Identity/AppIdentityDbContext.cs` - Updated with subscription entities
- `Identity/AppIdentityDbContextSeed.cs` - Seeds subscription plans

### Idempotency

- **Customer Creation**: Uses `reference` field (user ID) to ensure only one customer per user
- **Subscription Creation**: Each subscription attempt with Maxio creates distinct subscription
- **Plan Seeding**: On-demand, checks if plans already exist before inserting

## Troubleshooting

### "Maxio:ApiKey is not configured"
- Ensure environment variables are set before running the application
- Or configure via .NET user-secrets in development

### "Failed to create/retrieve Maxio customer"
- Verify Maxio sandbox credentials
- Verify Maxio API is accessible (check network connectivity)
- Check Maxio API response status codes

### "Subscription created successfully" but no subscription returned
- Check Maxio API response format
- Verify JSON parsing in MaxioService
- Check Maxio API documentation for response schema

### In-memory database data loss
- If using `UseOnlyInMemoryDatabase=true`, subscriptions are lost on restart
- Plans are reseeded on application start
- User credentials persist across restarts
- For persistent testing, configure SQL Server or use a real database

## Next Steps for Production

1. **Migrate to Real Database**: Replace in-memory database with SQL Server or PostgreSQL
2. **Error Handling**: Implement comprehensive error handling and retry logic
3. **Logging**: Add detailed logging for debugging and monitoring
4. **Validation**: Add input validation and rate limiting
5. **Webhooks**: Implement Maxio webhooks to sync subscription state changes
6. **Testing**: Add unit tests for MaxioService and endpoint handlers
7. **Documentation**: Create API documentation for clients
8. **Security**: Review JWT secret management and credential handling

## Support

For issues with:
- **Maxio API**: Visit https://docs.maxio.com and check Maxio API reference
- **eShopOnWeb**: Check GitHub repository documentation
- **Integration**: Review maxio-docs MCP server for latest Maxio API details
