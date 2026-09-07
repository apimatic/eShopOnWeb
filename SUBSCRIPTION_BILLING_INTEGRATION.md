# Maxio Subscription Billing Integration for eShopOnWeb

This guide explains how to set up and test the recurring subscription billing feature for eShopOnWeb using Maxio Advanced Billing.

## Overview

The integration adds three new API endpoints to the PublicApi project:

1. **GET /api/subscription-plans** - List available subscription plans (public endpoint)
2. **POST /api/subscriptions** - Create a new subscription for the authenticated user (requires JWT)
3. **GET /api/my-subscriptions** - Get the user's current subscriptions (requires JWT)

## Setup

### 1. Prerequisites

- .NET SDK 8.0+ (the SDK will roll forward to .NET 10 if needed)
- Maxio Advanced Billing sandbox account
- ASP.NET Core 8.0 runtime (or ability to run with `DOTNET_ROLL_FORWARD=Major`)

### 2. Environment Variables

Set the following environment variables with your Maxio credentials:

```bash
# Maxio API Configuration
MAXIO_API_KEY=your_api_key_here
MAXIO_SITE_SUBDOMAIN=your_site_subdomain
MAXIO_ENVIRONMENT=sandbox
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

**Important:** These values are read from environment variables and stored in .NET user-secrets. Never commit the actual values to the repository.

### 3. Initialize User-Secrets

Run the setup script to configure user-secrets with your Maxio credentials:

```powershell
# Windows PowerShell
.\setup-maxio-secrets.ps1

# Bash/Linux
pwsh .\setup-maxio-secrets.ps1
```

Or manually set each secret:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "your_api_key_here"
dotnet user-secrets set "Maxio:Subdomain" "your_site_subdomain"
dotnet user-secrets set "Maxio:Environment" "sandbox"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### 4. Database Setup

The integration uses an in-memory database for development (configured via `UseOnlyInMemoryDatabase=true` in `appsettings.Development.json`). The database is created automatically on startup.

For production deployments, update `appsettings.json` to use a SQL Server connection string:

```json
{
  "ConnectionStrings": {
    "MaxioBillingConnection": "Server=...;Database=...;"
  }
}
```

### 5. Running the Application

```bash
cd src/PublicApi
dotnet run
```

The PublicApi will start on `https://localhost:28363` with Swagger UI available at `/swagger`.

## Testing the Integration

### Prerequisites for Testing

You need a valid JWT token to access authenticated endpoints. Follow these steps:

#### 1. Get a JWT Token

First, authenticate to get a token:

```bash
curl -X POST "https://localhost:28363/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }'
```

Save the returned `token` value for use in subsequent API calls.

### Testing Endpoints

#### 1. List Available Subscription Plans

```bash
curl -X GET "https://localhost:28363/api/subscription-plans" \
  -H "Accept: application/json"
```

**Expected Response:**
```json
{
  "correlationId": "uuid",
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "$299/mo Pro Plan",
      "price": 299.00,
      "currency": "USD",
      "billingCycle": "1 month(s)"
    },
    {
      "handle": "basic-plan",
      "name": "$29/mo Basic Plan",
      "price": 29.00,
      "currency": "USD",
      "billingCycle": "1 month(s)"
    }
  ]
}
```

#### 2. Create a Subscription

```bash
curl -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }'
```

**Expected Response (201 Created):**
```json
{
  "correlationId": "uuid",
  "subscriptionId": 12345678,
  "state": "active",
  "planName": "$299/mo Pro Plan",
  "planHandle": "eshop-pro",
  "price": 299.00,
  "billingCycle": "month",
  "currentPeriodEndsAt": "2026-10-07T...",
  "nextAssessmentAt": "2026-10-07T...",
  "activatedAt": "2026-09-07T...",
  "createdAt": "2026-09-07T..."
}
```

#### 3. List User's Subscriptions

```bash
curl -X GET "https://localhost:28363/api/my-subscriptions" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Accept: application/json"
```

**Expected Response:**
```json
{
  "correlationId": "uuid",
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "planName": "$299/mo Pro Plan",
      "planHandle": "eshop-pro",
      "price": 299.00,
      "billingCycle": "month",
      "currentPeriodEndsAt": "2026-10-07T...",
      "nextAssessmentAt": "2026-10-07T...",
      "activatedAt": "2026-09-07T...",
      "createdAt": "2026-09-07T..."
    }
  ]
}
```

## Implementation Details

### Architecture

1. **MaxioApiClient** (`src/PublicApi/Maxio/MaxioApiClient.cs`)
   - HTTP client that communicates with Maxio API
   - Implements Basic Auth using API key
   - Handles JSON serialization/deserialization with snake_case property mapping

2. **SubscriptionService** (`src/PublicApi/Maxio/SubscriptionService.cs`)
   - Business logic layer
   - Manages customer and subscription lifecycle
   - Ensures idempotent customer creation (using user ID as reference)
   - Caches Maxio customer ID mapping in local database

3. **MaxioBillingDbContext** (`src/PublicApi/Maxio/MaxioBillingDbContext.cs`)
   - Entity Framework context for subscription-related data
   - Stores mapping between eShopOnWeb users and Maxio customers
   - Uses in-memory database for development

4. **SubscriptionEndpoints** (`src/PublicApi/SubscriptionEndpoints/SubscriptionEndpoints.cs`)
   - HTTP endpoint handlers
   - JWT token validation
   - Request/response DTOs

### Key Design Decisions

1. **User Identity Mapping**: The integration uses the eShopOnWeb user ID as the Maxio customer reference, enabling idempotent customer creation and lookups.

2. **Payment Collection**: Configured as "remittance" (no payment method required) for the sandbox demo. Change to "automatic" in production if credit card capture is needed.

3. **In-Memory Database**: The local database stores only the user-to-Maxio-customer mapping. All subscription data comes directly from Maxio.

4. **JWT Claims**: Updated `IdentityTokenClaimService` to include user ID and email in JWT tokens, enabling subscription endpoints to identify the user.

5. **Error Handling**: Endpoints return appropriate HTTP status codes (400 for invalid requests, 401 for auth failures, 500 for service errors).

### Maxio API Contract

The integration uses the Maxio OpenAPI specification located in `maxio-spec/openapi.yaml`. Key endpoints:

- `POST /customers.json` - Create customer
- `GET /customers/lookup.json` - Look up customer by reference
- `POST /subscriptions.json` - Create subscription
- `GET /subscriptions.json` - List subscriptions
- `GET /products.json` - List products

All API calls use Basic Authentication with the format: `Authorization: Basic base64(api_key:x)`

## Sandbox Test Data

The following products are pre-configured on the Maxio sandbox (`cp-exp-2`):

| Entity | Handle | Notes |
|--------|--------|-------|
| Product Family | `eshop-subscribe` | Container for plans |
| Pro Plan | `eshop-pro` | $299.00/mo |
| Basic Plan | `basic-plan` | $29.00/mo |
| Metered Component | `api-call` | $0.01/unit (seeded) |

**Note:** Numeric IDs may change during sandbox re-seeding. Always use handles for references.

## Troubleshooting

### 401 Unauthorized on Subscription Endpoints

**Cause**: Invalid or missing JWT token
**Solution**: 
1. Get a fresh token from `/api/authenticate`
2. Ensure the token is passed in the `Authorization: Bearer <token>` header

### 400 Bad Request - "User information not found in token"

**Cause**: JWT token missing required claims
**Solution**: 
1. Verify the `IdentityTokenClaimService` is updated with NameIdentifier and Email claims
2. Get a new token after deploying the updated service

### 500 Internal Server Error

**Cause**: Possible Maxio API connection issues
**Solution**:
1. Verify environment variables are correctly set
2. Check Maxio API key and subdomain are valid
3. Ensure Maxio sandbox site has the expected products configured
4. Check application logs for specific error details

### "Connection refused" errors

**Cause**: Application trying to connect to wrong Maxio URL
**Solution**:
1. Verify `Maxio:BaseUrl` is not overriding the correct URL
2. Ensure `Maxio:Subdomain` matches your actual Maxio site subdomain
3. Verify network connectivity to `{subdomain}.chargify.com`

## Production Deployment Checklist

- [ ] Store Maxio credentials in Azure Key Vault or equivalent secret management
- [ ] Update `Maxio:Environment` to `production` (or `eu` for EU hosting)
- [ ] Change payment collection method from `remittance` to `automatic` if card capture is required
- [ ] Configure SQL Server connection string for MaxioBillingDbContext
- [ ] Run database migrations: `dotnet ef database update`
- [ ] Test subscription workflows in production Maxio site before going live
- [ ] Implement error logging and monitoring for subscription operations
- [ ] Add rate limiting to subscription endpoints
- [ ] Implement webhook handlers for Maxio events (subscription status changes, failed payments, etc.)
- [ ] Document subscription lifecycle and cancellation procedures

## File Structure

```
src/PublicApi/
├── Maxio/
│   ├── MaxioSettings.cs              # Configuration class
│   ├── MaxioApiClient.cs             # HTTP client for Maxio API
│   ├── MaxioCustomerMapping.cs       # Entity for user-customer mapping
│   ├── MaxioBillingDbContext.cs      # EF Core context
│   ├── ISubscriptionService.cs       # Service interface
│   └── SubscriptionService.cs        # Service implementation
├── SubscriptionEndpoints/
│   ├── SubscriptionEndpoints.cs      # Endpoint handlers and DTOs
│   ├── SubscriptionDto.cs            # Subscription DTO
│   └── SubscriptionPlanDto.cs        # Plan DTO
└── appsettings.json                  # Configuration
```

## Next Steps

1. Implement subscription cancellation endpoint
2. Add webhook handlers for Maxio events
3. Implement usage-based billing for metered components
4. Add subscription upgrade/downgrade functionality
5. Implement invoicing and payment history retrieval
6. Add admin endpoints for subscription management
