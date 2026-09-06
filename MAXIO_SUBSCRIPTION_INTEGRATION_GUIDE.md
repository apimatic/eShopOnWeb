# Maxio Subscription Billing Integration Guide

## Overview

This document describes how to set up, configure, and test the Maxio Advanced Billing subscription integration for eShopOnWeb.

## Features Implemented

- **GET /api/subscription-plans** - List available subscription plans from Maxio
- **POST /api/subscriptions** - Create a new subscription for the authenticated user
- **GET /api/my-subscriptions** - Retrieve all subscriptions for the authenticated user

## Architecture

### New Entities

- **Subscription** (`src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs`) - Domain entity tracking user subscriptions
- **Subscription State Enum** - Tracks subscription status (Active, Paused, Canceled, etc.)

### Services

- **IMaxioService** (`src/ApplicationCore/Interfaces/IMaxioService.cs`) - Interface for Maxio API operations
- **MaxioService** (`src/Infrastructure/Services/MaxioService.cs`) - HTTP-based implementation using Maxio REST API

### API Endpoints

All endpoints are in `src/PublicApi/SubscriptionEndpoints/`:

1. **SubscriptionPlansListEndpoint** - GET /api/subscription-plans
   - Public endpoint (no authentication required)
   - Returns all available plans from Maxio

2. **CreateSubscriptionEndpoint** - POST /api/subscriptions
   - JWT authenticated endpoint
   - Creates Maxio customer (if needed) and subscription
   - Stores subscription record in local database

3. **GetUserSubscriptionsEndpoint** - GET /api/my-subscriptions
   - JWT authenticated endpoint
   - Returns all subscriptions for authenticated user

### Database

- **Migration** `20260906021012_AddSubscriptionSupport.cs` - Adds Subscriptions table to CatalogContext

## Prerequisites

1. .NET 8.0 SDK (or rollForward: latestMajor in global.json)
2. ASP.NET Core 8.0 runtime (or appropriate rollForward setting)
3. Maxio Advanced Billing sandbox account with:
   - API key generated
   - Product Family configured (handle: `eshop-subscribe`)
   - At least one product/plan configured (handles: `eshop-pro`, `basic-plan`)
4. Maxio sandbox site credentials

## Configuration Setup

### 1. Set Environment Variables

Set the following environment variables (either in your system or via IDE):

```
MAXIO_API_KEY=<your-maxio-api-key>
MAXIO_SITE_SUBDOMAIN=<your-maxio-site-subdomain>
MAXIO_ENVIRONMENT=sandbox
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

**Important**: Do NOT commit these values to the repository. Use environment variables or .NET user-secrets.

### 2. Configure via User Secrets (Recommended for Development)

If using Windows and Visual Studio, use the Secrets Manager:

```powershell
# From the src/PublicApi directory:
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:BaseUrl" ""  # Leave empty to auto-construct from subdomain
```

Or use the secrets ID from PublicApi.csproj:

```powershell
dotnet user-secrets set --id df4e43b1-13a8-4293-82e6-5b5e9821f008 "Maxio:ApiKey" "your-api-key"
```

### 3. Configure via appsettings (Non-Production Only)

Update `src/PublicApi/appsettings.Development.json`:

```json
{
  "Maxio": {
    "ApiKey": "your-api-key",
    "Subdomain": "your-subdomain",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": null
  }
}
```

## Database Configuration

### Option 1: In-Memory Database (Development)

Add to appsettings or environment:
```
UseOnlyInMemoryDatabase=true
```

This uses EF Core's in-memory provider (faster startup, data lost on restart).

### Option 2: SQL Server LocalDB

Ensure LocalDB is installed and connection strings are configured in appsettings.json.

## Running the Application

### 1. Restore Dependencies

```bash
cd repo
dotnet restore eShopOnWeb.sln
```

### 2. Apply Migrations (if using real database)

```bash
dotnet ef database update --project src/Infrastructure --startup-project src/PublicApi --context CatalogContext
```

### 3. Run PublicApi

```bash
dotnet run --project src/PublicApi --configuration Development
```

The API will be available at: `https://localhost:25443`

Swagger UI: `https://localhost:25443/swagger`

## Testing the Integration

### 1. Authenticate and Get JWT Token

**Endpoint:** `POST /api/authenticate`

**Request:**
```json
{
  "username": "demouser@microsoft.com",
  "password": "P@ssw0rd!"
}
```

**Response:**
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com"
}
```

Save the token for subsequent authenticated requests.

### 2. List Available Plans

**Endpoint:** `GET /api/subscription-plans`

**No authentication required**

**cURL:**
```bash
curl -X GET "https://localhost:25443/api/subscription-plans" \
  -H "accept: application/json" \
  --insecure
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription plan",
      "price": 299.00,
      "interval": "month",
      "intervalUnit": 1
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription plan",
      "price": 29.00,
      "interval": "month",
      "intervalUnit": 1
    }
  ]
}
```

### 3. Create a Subscription

**Endpoint:** `POST /api/subscriptions`

**Authentication:** Required (Bearer token)

**Request:**
```json
{
  "planHandle": "eshop-pro"
}
```

**cURL:**
```bash
curl -X POST "https://localhost:25443/api/subscriptions" \
  -H "accept: application/json" \
  -H "Authorization: Bearer <your-jwt-token>" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure
```

**Expected Response:**
```json
{
  "subscription": {
    "id": 1,
    "maxioSubscriptionId": 12345678,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "planPrice": 299.00,
    "state": "Active",
    "createdDate": "2026-09-06T12:34:56.789Z",
    "nextBillingDate": "2026-10-06T12:34:56.789Z"
  }
}
```

### 4. Get User's Subscriptions

**Endpoint:** `GET /api/my-subscriptions`

**Authentication:** Required (Bearer token)

**cURL:**
```bash
curl -X GET "https://localhost:25443/api/my-subscriptions" \
  -H "accept: application/json" \
  -H "Authorization: Bearer <your-jwt-token>" \
  --insecure
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 12345678,
      "planHandle": "eshop-pro",
      "planName": "Pro Plan",
      "planPrice": 299.00,
      "state": "Active",
      "createdDate": "2026-09-06T12:34:56.789Z",
      "nextBillingDate": "2026-10-06T12:34:56.789Z"
    }
  ]
}
```

## Key Design Decisions

### 1. HTTP-Based Maxio Client

- Uses standard `HttpClient` with Basic authentication
- No external SDK dependency required
- Direct JSON parsing for flexibility
- Extensible for future Maxio capabilities

### 2. Customer Idempotency

- `GetOrCreateCustomerAsync()` checks if customer exists before creating
- Prevents duplicate customer creation on retry
- Uses email as lookup key with user ID as reference

### 3. Local Subscription Tracking

- Subscriptions are stored in local database
- Links Maxio subscription ID to application user
- Enables offline access and audit trail

### 4. JWT Authentication

- All user subscriptions use existing JWT authentication
- User identity comes from `ClaimsPrincipal`
- No separate API keys required

### 5. Minimal API Pattern

- Endpoints use .NET 6+ minimal APIs
- No controller-based architecture
- Direct dependency injection in route handlers

## Troubleshooting

### "Maxio connection failed"

1. Verify MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN are correct
2. Check network connectivity to Maxio API
3. Verify API key has appropriate permissions in Maxio dashboard

### "Customer not found in Maxio"

- Verify the email address used during subscription creation
- Check Maxio dashboard to confirm customer exists

### "Plan not found"

- Verify planHandle matches exactly (case-sensitive)
- Check Maxio product family configuration
- Ensure plan is active in Maxio

### "Database migration errors"

If using real database and migrations fail:

```bash
# Reset migrations (development only)
dotnet ef migrations remove --project src/Infrastructure --startup-project src/PublicApi --context CatalogContext --force

# Recreate
dotnet ef migrations add AddSubscriptionSupport --project src/Infrastructure --startup-project src/PublicApi --context CatalogContext
dotnet ef database update --project src/Infrastructure --startup-project src/PublicApi --context CatalogContext
```

## Production Considerations

### Security

1. **API Key Protection**
   - Store in Azure Key Vault or equivalent
   - Use environment variables (never commit to repo)
   - Rotate API keys periodically

2. **HTTPS Only**
   - Remove `RequireHttpsMetadata = false` in JWT bearer options
   - Ensure all API calls use HTTPS

3. **Rate Limiting**
   - Implement rate limiting on subscription endpoints
   - Consider caching plan lists

### Logging

Current implementation includes:
- Error logging via `ILogger<MaxioService>`
- HTTP request/response logging in MaxioService
- EF Core change tracking for subscription database operations

### Monitoring

Recommended additions:
- Application Insights integration
- Subscription lifecycle event tracking
- Maxio API error rate monitoring
- Database performance metrics

### Testing

Test coverage for:
- Maxio API integration (mocked)
- Subscription creation flow
- Edge cases (duplicate subscriptions, invalid plans)
- Database persistence
- JWT authentication

## Files Added/Modified

### New Files

- `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs`
- `src/ApplicationCore/Constants/MaxioConfiguration.cs`
- `src/ApplicationCore/Interfaces/IMaxioService.cs`
- `src/ApplicationCore/Specifications/UserSubscriptionsSpecification.cs`
- `src/Infrastructure/Services/MaxioService.cs`
- `src/Infrastructure/Data/Config/SubscriptionConfiguration.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlansListEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/GetUserSubscriptionsEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionDto.cs`
- `src/Infrastructure/Data/Migrations/20260906021012_AddSubscriptionSupport.cs`
- `src/Infrastructure/Data/Migrations/20260906021012_AddSubscriptionSupport.Designer.cs`

### Modified Files

- `src/PublicApi/Program.cs` - Added Maxio service registration
- `src/Infrastructure/Dependencies.cs` - Added Maxio configuration
- `src/Infrastructure/Data/CatalogContext.cs` - Added Subscription DbSet
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/PublicApi/appsettings.Development.json` - Added Maxio configuration section

## Next Steps

1. Set up Maxio sandbox credentials
2. Configure environment variables or user secrets
3. Run migrations (if using real database)
4. Test endpoints using provided cURL commands
5. Implement UI components to consume subscription endpoints
6. Add webhook handling for Maxio lifecycle events
7. Implement subscription management (pause, resume, cancel)

## References

- [Maxio Advanced Billing API Documentation](https://developers.maxio.com/)
- [Maxio .NET SDK](https://github.com/maxio-com/ab-dotnet-sdk)
- [ASP.NET Core Minimal APIs](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)
- [JWT Authentication in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity/spa)
