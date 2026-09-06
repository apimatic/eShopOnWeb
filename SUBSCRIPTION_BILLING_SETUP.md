# Maxio Subscription Billing Integration for eShopOnWeb

## Overview

This document describes the subscription billing integration added to eShopOnWeb using Maxio Advanced Billing. The feature enables users to subscribe to recurring plans in addition to the existing one-time commerce capabilities.

## Architecture

The integration consists of:

1. **Maxio API Client** (`src/ApplicationCore/Services/MaxioApiClient.cs`)
   - Handles HTTP communication with Maxio Advanced Billing API
   - Implements customer management, plan retrieval, and subscription operations
   - Uses Basic Auth with API key for authentication

2. **Domain Model** (`src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs`)
   - Tracks user subscriptions with Maxio customer and subscription IDs
   - Stores subscription state, dates, and product handles
   - Implements `IAggregateRoot` for repository pattern

3. **API Endpoints** (Ardalis.ApiEndpoints pattern)
   - `GET /api/subscription-plans` - List available plans (public)
   - `POST /api/subscriptions` - Create subscription (JWT authenticated)
   - `GET /api/my-subscriptions` - Get user's subscriptions (JWT authenticated)

4. **Configuration**
   - Maxio settings loaded from environment variables
   - Subscription persistence via EF Core migration

## Setup & Deployment

### Prerequisites

- .NET 8.0 runtime or .NET 10 SDK (with rollForward enabled)
- Maxio Advanced Billing sandbox account
- Environment variables configured

### Environment Variables

Configure the following before running:

```bash
# Maxio API Credentials
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="your-sandbox-subdomain"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Optional: Override base URL (for testing)
# export Maxio:BaseUrl="https://custom.endpoint.com"

# Database Configuration
export UseOnlyInMemoryDatabase="true"  # Or "false" for SQL Server
```

### Storing Secrets

Store Maxio credentials in .NET user-secrets (NOT in appsettings.json):

```bash
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### Database Migration

The Subscription table is created via EF Core migration:

```bash
cd src/PublicApi
dotnet ef database update --project ../Infrastructure
```

### Running the Application

```bash
cd src/PublicApi
dotnet run
```

Visit: `https://localhost:25683/swagger` to explore endpoints.

## Integration Flow

### 1. Authentication

The user authenticates via the existing `/api/authenticate` endpoint to obtain a JWT token:

```bash
curl -X POST https://localhost:25683/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"user@example.com","password":"password"}'
```

Response includes `token` field.

### 2. Browse Plans

Retrieve available subscription plans (no authentication required):

```bash
curl https://localhost:25683/api/subscription-plans
```

Response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "billingIntervalDays": 1,
      "billingIntervalUnit": "month",
      "description": "Professional plan with advanced features"
    }
  ]
}
```

### 3. Subscribe

Create a subscription (requires JWT):

```bash
curl -X POST https://localhost:25683/api/subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

Response:
```json
{
  "id": 12345678,
  "customerId": 987654,
  "productHandle": "eshop-pro",
  "state": "active",
  "priceMonthly": 299.00,
  "currentPeriodEndsAt": "2026-10-06T07:55:00Z",
  "nextAssessmentAt": "2026-10-06T07:55:00Z",
  "activatedAt": "2026-09-06T07:55:00Z"
}
```

**Key Flow:**
1. Endpoint retrieves authenticated user ID from JWT claims
2. Maxio client creates/finds customer with user ID as reference
3. Checks for existing active subscriptions (idempotent)
4. Creates subscription on Maxio platform
5. Persists subscription record to local database

### 4. View Subscriptions

Retrieve authenticated user's subscriptions:

```bash
curl https://localhost:25683/api/my-subscriptions \
  -H "Authorization: Bearer <token>"
```

Response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "state": "active",
      "priceMonthly": 299.00,
      "currentPeriodEndsAt": "2026-10-06T07:55:00Z",
      "nextAssessmentAt": "2026-10-06T07:55:00Z",
      "activatedAt": "2026-09-06T07:55:00Z"
    }
  ]
}
```

## Idempotency & Error Handling

**Customer Creation (Idempotent)**
- If customer already exists (reference matches), returns existing customer
- Prevents duplicate Maxio customers for the same user

**Subscription Creation (Idempotent within a session)**
- If active subscription exists for product, returns error
- Stores subscription ID locally to prevent re-enrollment

**Error Responses** return HTTP 400 with error details:
```json
{
  "message": "You already have an active subscription for this plan"
}
```

## Data Persistence

### In-Memory Database (Development)
- Subscriptions lost on app restart
- Use `UseOnlyInMemoryDatabase=true`

### SQL Server (Production)
- Persistent storage via EF Core
- Migration: `20260906000000_AddSubscriptions.cs`
- Connection: SQL Server or LocalDB

## Testing Scenarios

### Test Case 1: Browse & Subscribe

```bash
# Get token
TOKEN=$(curl -s -X POST https://localhost:25683/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"testuser","password":"password"}' \
  | jq -r '.token')

# Get plans
curl https://localhost:25683/api/subscription-plans | jq

# Subscribe to first plan handle
curl -X POST https://localhost:25683/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'

# View subscriptions
curl https://localhost:25683/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" | jq
```

### Test Case 2: Duplicate Subscription Prevention

```bash
# Try subscribing to same plan again (should fail)
curl -X POST https://localhost:25683/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'

# Response: 400 "You already have an active subscription for this plan"
```

### Test Case 3: Multiple Plans

```bash
# Subscribe to different plan
curl -X POST https://localhost:25683/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"basic-plan"}'

# Should succeed if different handle
# View should show both subscriptions
curl https://localhost:25683/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN"
```

## Production Considerations

### Security
- **API Keys:** Never commit Maxio API key. Use user-secrets or environment variables
- **JWT:** Verify token claims for user identity
- **HTTPS:** Always use in production

### Reliability
- **Error Handling:** Log Maxio API failures; return user-friendly messages
- **Timeout:** Set reasonable HTTP timeouts for Maxio API calls
- **Retry:** Consider exponential backoff for transient failures

### Monitoring
- Track subscription creation/failure rates
- Monitor Maxio API response times
- Alert on authentication failures

### Data Consistency
- Reconcile local subscriptions with Maxio on periodic basis
- Use Maxio webhooks for real-time updates (future enhancement)

## Future Enhancements

1. **Webhook Integration**
   - Listen for subscription state changes from Maxio
   - Update local records in real-time

2. **Subscription Management**
   - `PATCH /api/subscriptions/{id}` - Change plan
   - `DELETE /api/subscriptions/{id}` - Cancel subscription

3. **Billing Portal**
   - Link to Maxio customer billing portal
   - Invoice history and payment methods

4. **Metered Billing**
   - Report usage for metered components
   - Support variable charges based on usage

## Troubleshooting

### "Failed to create Maxio customer"
- Verify MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN
- Check Maxio API key has correct permissions
- Verify sandbox account is active

### "Failed to get plans"
- Confirm MAXIO_DEFAULT_PRODUCT_FAMILY handle is correct
- Verify plans exist in Maxio for this product family
- Check API key permissions

### "Package not found" errors during build
- Clear NuGet cache: `dotnet nuget locals all --clear`
- Restore packages: `dotnet restore`

### Database errors
- For in-memory: data resets on restart (expected)
- For SQL Server: ensure connection string is correct
- Run migrations: `dotnet ef database update`

## Files Modified/Created

### New Files
- `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs` - Domain model
- `src/ApplicationCore/Settings/MaxioSettings.cs` - Configuration model
- `src/ApplicationCore/Services/MaxioApiClient.cs` - Maxio API client
- `src/Infrastructure/Data/Config/SubscriptionConfiguration.cs` - EF configuration
- `src/Infrastructure/Data/Migrations/20260906000000_AddSubscriptions.cs` - Database migration
- `src/PublicApi/SubscriptionEndpoints/` - Three API endpoints + supporting DTOs

### Modified Files
- `src/Infrastructure/Data/CatalogContext.cs` - Added Subscriptions DbSet
- `src/PublicApi/Program.cs` - Registered Maxio services and configuration

### Files Unchanged
- Core eShopOnWeb functionality (catalog, basket, orders)
- Web and BlazorAdmin projects
- Existing APIs and endpoints

## Verification Checklist

Before deployment:

- [ ] Environment variables set for Maxio credentials
- [ ] User-secrets configured (if using local dev)
- [ ] Database migration applied
- [ ] Application builds without errors
- [ ] Can retrieve subscription plans (GET /api/subscription-plans)
- [ ] Can authenticate user (POST /api/authenticate)
- [ ] Can create subscription (POST /api/subscriptions)
- [ ] Can list user subscriptions (GET /api/my-subscriptions)
- [ ] Subscription appears in Maxio dashboard
- [ ] Local database persists subscription (if using SQL Server)
