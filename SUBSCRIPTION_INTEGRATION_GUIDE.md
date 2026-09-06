# Maxio Subscription Billing Integration - Verification Guide

This guide walks through verifying the Maxio Advanced Billing integration with eShopOnWeb.

## Prerequisites

- .NET SDK 8.0+ (already pinned to rollForward: latestFeature)
- Maxio sandbox account with the following seeded data:
  - **Site**: `cp-exp-3`
  - **Product Family**: `eshop-subscribe` (handle)
  - **Plans**: 
    - `eshop-pro` (Pro Plan, $299/mo)
    - `basic-plan` (Basic Plan, $29/mo)
  - **API Key**: Available from Maxio dashboard

## Setup

### 1. Configure Maxio Credentials

Replace the placeholder values in user secrets with actual Maxio sandbox credentials:

```bash
cd src/PublicApi

# Set your actual Maxio API key from the sandbox
dotnet user-secrets set "Maxio:ApiKey" "YOUR_ACTUAL_API_KEY"

# Set the Maxio site subdomain (default is cp-exp-3)
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-3"

# Set the product family handle (default is eshop-subscribe)
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

Verify secrets are saved:
```bash
dotnet user-secrets list
```

### 2. Database Configuration

The integration uses the in-memory database by default in development. The database migration has been created and will be applied automatically.

If you need to use SQL Server instead:
- Edit `appsettings.json` and set `UseOnlyInMemoryDatabase` to `false`
- Ensure LocalDB or SQL Server is running
- The migration will be applied on first run

### 3. Build and Run

```bash
# Build the solution (PublicApi project)
dotnet build src/PublicApi/PublicApi.csproj -c Debug

# Run the PublicApi service
dotnet run --project src/PublicApi/PublicApi.csproj
```

The API will be available at: `https://localhost:25743/api`

## Testing the Integration

### Step 1: Create/Authenticate a User

First, create a test user via the authenticate endpoint:

```bash
# Request
curl -X POST "https://localhost:25743/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "testuser@example.com",
    "password": "TestPassword123!"
  }'

# Response will include:
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "testuser@example.com"
}
```

**Important**: Save the returned `token` value - you'll need it for all subscription endpoints.

### Step 2: List Available Subscription Plans

Retrieve all available subscription plans from Maxio:

```bash
# Request (replace TOKEN with your actual JWT token from Step 1)
curl -X GET "https://localhost:25743/api/subscription-plans" \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json"

# Expected Response:
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "$299.00/mo",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "$29.00/mo",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 3: Create a Subscription

Subscribe the authenticated user to a plan:

```bash
# Request (replace TOKEN with your JWT token, 7126957 is the Pro Plan ID)
curl -X POST "https://localhost:25743/api/subscriptions" \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productId": 7126957
  }'

# Expected Response:
{
  "subscription": {
    "id": 123456789,
    "state": "active",
    "customerId": 987654,
    "productId": 7126957,
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "productPriceInCents": 29900,
    "currentPeriodEndsAt": "2026-10-06T...",
    "nextAssessmentAt": "2026-10-06T...",
    "createdAt": "2026-09-06T...",
    "updatedAt": "2026-09-06T..."
  }
}
```

Key observations:
- The user's Maxio customer record is created automatically on first subscription
- The `customerId` is the Maxio customer ID (stored in database)
- `state` should be `"active"` indicating the subscription is live
- `currentPeriodEndsAt` and `nextAssessmentAt` show the next billing dates

### Step 4: List My Subscriptions

Retrieve all active subscriptions for the logged-in user:

```bash
# Request (replace TOKEN with your JWT token)
curl -X GET "https://localhost:25743/api/my-subscriptions" \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json"

# Expected Response:
{
  "subscriptions": [
    {
      "id": 123456789,
      "state": "active",
      "customerId": 987654,
      "productId": 7126957,
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "productPriceInCents": 29900,
      "currentPeriodEndsAt": "2026-10-06T...",
      "nextAssessmentAt": "2026-10-06T...",
      "createdAt": "2026-09-06T...",
      "updatedAt": "2026-09-06T..."
    }
  ]
}
```

### Step 5: Create Additional Subscription

Verify idempotency by creating another subscription:

```bash
# Subscribe the same user to the Basic Plan (ID: 7126958)
curl -X POST "https://localhost:25743/api/subscriptions" \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productId": 7126958
  }'

# This should succeed because:
# 1. The existing Maxio customer mapping is reused
# 2. The same user can have multiple active subscriptions
```

Then verify both subscriptions appear in `GET /api/my-subscriptions`.

## Architecture Overview

### Database Layer
- **Entity**: `MaxioCustomer` in `AppIdentityDbContext`
  - Links `ApplicationUser.Id` to Maxio customer ID
  - Ensures idempotent customer creation
  - Unique constraints prevent duplicate mappings

### API Layer
- **Service**: `IMaxioApiClient` (`MaxioApiClient` implementation)
  - Handles all HTTP communication with Maxio API
  - Authenticates via HTTP Basic Auth (API key as username, "x" as password)
  - Parses JSON responses from Maxio OpenAPI spec

- **Service**: `ISubscriptionService` (`SubscriptionService` implementation)
  - Manages the user ↔ Maxio customer mapping
  - Ensures customers are created idempotently before subscriptions

### Endpoints
All endpoints require JWT bearer token authentication:

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/subscription-plans` | List all available subscription plans |
| POST | `/api/subscriptions` | Create a new subscription for the authenticated user |
| GET | `/api/my-subscriptions` | List all active subscriptions for the authenticated user |

## Configuration

### Maxio Settings (Loaded from Configuration)

All settings come from `Maxio:` section in `appsettings.json` or user secrets:

```json
{
  "Maxio": {
    "ApiKey": "sk_live_...",              // Required: API key from Maxio
    "Subdomain": "cp-exp-3",              // Required: Maxio site subdomain
    "ProductFamilyHandle": "eshop-subscribe", // Optional: For filtering products
    "BaseUrl": ""                         // Optional: Override API base URL
  }
}
```

When `BaseUrl` is empty, the API base URL is constructed as: `https://{Subdomain}.chargify.com`

## Troubleshooting

### Authentication Failures

If you receive 401/403 errors:

1. Verify your JWT token is included and valid:
   ```bash
   # Decode the token to check expiration and claims
   # The token includes username in the 'name' claim
   ```

2. Verify user exists in the application database

3. Check user secrets are configured properly:
   ```bash
   cd src/PublicApi
   dotnet user-secrets list
   ```

### Maxio API Errors

If you receive errors from Maxio (4xx/5xx):

1. **401 Unauthorized**: Check your API key is correct and still valid in the sandbox
2. **404 Not Found**: Verify the product/customer IDs exist in the Maxio sandbox
3. **422 Unprocessable Entity**: Check the request format matches the OpenAPI spec

Look at the error response body for detailed Maxio error messages.

### Database Issues

If you see "Entity not found" errors:

1. Verify the in-memory database is being used (check logs)
2. The database is recreated on each run - previous data is lost
3. If using SQL Server, run migrations: `dotnet ef database update`

## Files Created/Modified

### New Files
- `src/ApplicationCore/Entities/MaxioCustomer.cs` - Database entity for user/customer mapping
- `src/Infrastructure/Billing/MaxioSettings.cs` - Configuration class
- `src/Infrastructure/Billing/MaxioApiClient.cs` - HTTP client for Maxio API
- `src/Infrastructure/Billing/SubscriptionService.cs` - Business logic service
- `src/PublicApi/SubscriptionEndpoints/*.cs` - API endpoints (3 files)
- `src/Infrastructure/Identity/Migrations/20260906031734_AddMaxioCustomer.cs` - Database migration

### Modified Files
- `src/PublicApi/Program.cs` - Registered Maxio services
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/Infrastructure/Identity/AppIdentityDbContext.cs` - Added MaxioCustomer DbSet
- `src/PublicApi/PublicApi.csproj` - No NuGet changes (uses existing dependencies)

## Integration Notes

- **No External Dependencies**: Uses only `System.Net.Http` and `System.Text.Json` (already referenced)
- **Idempotent Operations**: Double-clicking "subscribe" never creates duplicate Maxio customers
- **In-Memory Database**: Data does not persist across application restarts in development
- **OpenAPI Spec Compliant**: All interactions validated against `maxio-spec/openapi.yaml`
- **Error Handling**: Errors from Maxio are returned to the client with context
- **Authorization**: All endpoints require valid JWT bearer token

## Next Steps

1. Deploy to your Maxio production site by updating the `Maxio:Subdomain` configuration
2. Consider adding webhook handlers for subscription state changes (optional enhancement)
3. Add customer payment method collection if requiring cards (currently payment method is not required)
4. Implement subscription management endpoints (cancel, update, etc.) based on business needs
