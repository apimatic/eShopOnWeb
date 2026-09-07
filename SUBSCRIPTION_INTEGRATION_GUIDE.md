# Maxio Subscription Billing Integration - eShopOnWeb

This guide documents the production-grade Maxio subscription billing integration added to eShopOnWeb.

## Overview

The integration adds a parallel subscription billing capability to eShopOnWeb via **Maxio Advanced Billing**. Users can:
1. Browse available subscription plans
2. Subscribe to a plan
3. View their active subscriptions

The implementation follows eShopOnWeb's existing architectural patterns and does not interfere with the existing cart/order flow.

## Architecture

### Entities
- **Subscription** (`src/ApplicationCore/Entities/Subscription.cs`): Tracks user subscriptions with Maxio references
  - Stores userId, Maxio customerId, Maxio subscriptionId
  - Maintains plan handle, state, pricing, and billing dates

### Services
- **MaxioApiService** (`src/Infrastructure/Services/MaxioApiService.cs`): Handles all Maxio API interactions
  - Creates/retrieves customers via reference lookup (idempotent)
  - Lists available products (plans)
  - Creates subscriptions
  - Retrieves subscription details
  - Uses BasicAuth with Maxio API key

- **SubscriptionService** (`src/Infrastructure/Services/SubscriptionService.cs`): Manages local database persistence
  - Creates or updates subscription records
  - Retrieves user subscriptions

### Endpoints
All endpoints in **`src/PublicApi/SubscriptionEndpoints/`**:

#### GET `/api/subscription-plans`
Lists available subscription plans from Maxio.
- **Response**: 200 OK with plan details (id, name, handle, price, interval)
- **Auth**: Not required
- **Note**: Plans are fetched from the configured product family

#### POST `/api/subscriptions`
Creates a subscription for the authenticated user.
- **Request Body**:
  ```json
  {
    "productHandle": "eshop-pro"
  }
  ```
- **Response**: 201 Created with subscription details
  - SubscriptionId (Maxio subscription ID)
  - CustomerId (Maxio customer ID)
  - State (e.g., "active")
  - ProductHandle, ProductName
  - PriceInCents, IntervalUnit, Interval
  - ActivatedAt, NextAssessmentAt, CurrentPeriodEndsAt
- **Auth**: Required (JWT Bearer token)
- **Logic**:
  1. Gets current user from JWT claims
  2. Creates or retrieves Maxio customer (idempotent, keyed by userId)
  3. Creates subscription with no payment required (payment_collection_method: remittance)
  4. Stores subscription record in local database

#### GET `/api/my-subscriptions`
Retrieves all subscriptions for the authenticated user.
- **Response**: 200 OK with array of subscription objects
  - SubscriptionId, ProductHandle, ProductName, ProductId
  - State, PriceInCents, IntervalUnit, Interval
  - ActivatedAt, NextAssessmentAt
- **Auth**: Required (JWT Bearer token)
- **Note**: Returns both active and inactive subscriptions

## Configuration

### Environment Variables (Required)
Set these before running:
```bash
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_ENVIRONMENT="US"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

### User Secrets (Development)
Secrets are stored securely via .NET user-secrets:
```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

### Configuration Binding
The `appsettings.json` defines the `Maxio:` section:
```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "",
    "ProductFamilyHandle": "",
    "BaseUrl": ""
  }
}
```

- **ApiKey**: From MAXIO_API_KEY environment variable or user-secrets
- **Subdomain**: From MAXIO_SITE_SUBDOMAIN environment variable or user-secrets
- **ProductFamilyHandle**: From MAXIO_DEFAULT_PRODUCT_FAMILY environment variable or user-secrets
- **BaseUrl**: Optional override; if empty, constructs `https://{subdomain}.chargify.com`

## Database

### Migration
A migration `AddSubscriptionEntity` creates the `Subscriptions` table with:
- UserId (foreign key to AspNetUsers)
- MaxioCustomerId, MaxioSubscriptionId (Maxio references)
- ProductHandle, State, PriceInCents, IntervalUnit, Interval
- ActivatedAt, NextAssessmentAt, CreatedAt, UpdatedAt
- Unique index on (UserId, MaxioSubscriptionId)
- Index on MaxioCustomerId

### In-Memory Database
For local development, `appsettings.Development.json` sets:
```json
{
  "UseOnlyInMemoryDatabase": true
}
```

This avoids SQL Server LocalDB requirement. Data persists only within a single run.

## Verification Steps

### 1. Start the PublicApi
```bash
cd src/PublicApi
dotnet run
# Server runs on https://localhost:28523
```

### 2. Get Authentication Token
```bash
curl -k -X POST https://localhost:28523/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }'
```

Response includes a `token` field. Copy this for subsequent requests.

### 3. List Available Plans
```bash
curl -k https://localhost:28523/api/subscription-plans
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "...",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month",
      "taxable": false
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      ...
    }
  ]
}
```

### 4. Create a Subscription
```bash
TOKEN="<token-from-step-2>"
curl -k -X POST https://localhost:28523/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

Expected response (201 Created):
```json
{
  "subscriptionId": 123456789,
  "customerId": 98765,
  "state": "active",
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "priceInCents": 29900,
  "intervalUnit": "month",
  "interval": 1,
  "activatedAt": "2026-09-07T...",
  "nextAssessmentAt": "2026-10-07T...",
  "currentPeriodEndsAt": "2026-10-07T..."
}
```

**Key behaviors**:
- Idempotent customer creation: calling twice with the same user doesn't create duplicate Maxio customers
- Subscription state is "active" immediately (no payment required on sandbox plans)
- Next billing date (nextAssessmentAt) is set to one billing period from activation

### 5. Retrieve User's Subscriptions
```bash
TOKEN="<token-from-step-2>"
curl -k https://localhost:28523/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN"
```

Expected response (200 OK):
```json
{
  "subscriptions": [
    {
      "subscriptionId": 123456789,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "productId": 7126957,
      "state": "active",
      "priceInCents": 29900,
      "intervalUnit": "month",
      "interval": 1,
      "activatedAt": "2026-09-07T...",
      "nextAssessmentAt": "2026-10-07T..."
    }
  ]
}
```

### 6. Test Idempotency
Create the same subscription again (same user, same plan):
```bash
TOKEN="<token-from-step-2>"
curl -k -X POST https://localhost:28523/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

Expected: New subscription is created with a different subscriptionId. The Maxio customer is not duplicated (found via reference lookup).

## Implementation Details

### Maxio API Contract
- **Authentication**: BasicAuth with `username=API_KEY` and `password=x`
- **Endpoints Used**:
  - `POST /customers.json` - Create customer
  - `GET /customers/lookup.json?reference=X` - Find customer by reference
  - `GET /products.json?family_id=handle:X` - List products in family
  - `POST /subscriptions.json` - Create subscription
  - `GET /subscriptions/{id}.json` - Get subscription details
  - `GET /customers/{id}/subscriptions.json` - List customer subscriptions

All interaction is governed by the OpenAPI spec in `maxio-spec/openapi.yaml`.

### Error Handling
- Failed Maxio API calls return 400 Bad Request with error description
- Missing JWT token returns 401 Unauthorized
- User not found returns 401 Unauthorized
- All errors are logged via ILogger<T>

### Data Consistency
- Subscription records are created in local database only after Maxio confirms creation
- Customer reference is always the eShopOnWeb userId (ensures idempotency)
- NextAssessmentAt is always populated by Maxio; we store as-is

## Sandbox Entities

The Maxio sandbox site `cp-exp-2` has pre-seeded:
- **Product Family**: `eshop-subscribe` (id: 3023074)
  - **Pro Plan**: `eshop-pro` - $299/month
  - **Basic Plan**: `basic-plan` - $29/month
  - **Component**: `api-call` - $0.01/unit (metered)

No setup fees, no trials, no payment required (safe for testing).

## Production Considerations

1. **Payment Collection**
   - Current implementation uses `payment_collection_method: "remittance"`
   - For production, consider `"automatic"` or `"remittance"` based on business needs
   - May require collecting payment profiles via Chargify.js

2. **Customer Attributes**
   - Currently uses email and parsed username parts as first/last name
   - For production, integrate with user profile data (address, phone, etc.)

3. **Data Persistence**
   - Development uses in-memory database; production must use SQL Server or other persisted DB
   - Ensure migrations are run before deployment

4. **Logging**
   - All Maxio API calls are logged at info/error level
   - Monitor logs for API failures or unusual subscription states

5. **Security**
   - API key is stored in user-secrets locally, environment variables in CI/CD
   - Never commit actual secrets to the repository
   - Endpoints require JWT authentication (already enforced)

6. **Rate Limiting**
   - No rate limiting implemented; Maxio API has limits; monitor usage
   - Consider adding resilience patterns (retries, circuit breakers) for production

## File Manifest

### New Files
- `src/ApplicationCore/Entities/Subscription.cs` - Entity
- `src/ApplicationCore/MaxioSettings.cs` - Configuration
- `src/Infrastructure/Services/MaxioApiService.cs` - Maxio client
- `src/Infrastructure/Services/SubscriptionService.cs` - Database service
- `src/Infrastructure/Data/Config/SubscriptionConfiguration.cs` - EF configuration
- `src/Infrastructure/Data/Migrations/*/AddSubscriptionEntity.cs` - Database migration
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - Endpoint 1
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - Endpoint 2
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs` - Endpoint 3

### Modified Files
- `src/Infrastructure/Data/CatalogContext.cs` - Added Subscriptions DbSet
- `src/Infrastructure/Dependencies.cs` - Registered subscription service
- `src/PublicApi/Program.cs` - Configured Maxio HTTP client
- `src/PublicApi/appsettings.json` - Added Maxio: section
- `src/PublicApi/appsettings.Development.json` - Added in-memory DB flag
- `global.json` - Updated SDK rollForward policy

## Next Steps

1. Run the application per the verification steps above
2. Confirm all endpoints respond correctly
3. Test idempotency by creating duplicate subscriptions
4. Review logs for any Maxio API errors
5. For production deployment:
   - Replace in-memory DB with persisted database
   - Update payment collection method based on requirements
   - Add payment profile handling
   - Monitor API usage and implement rate limiting
   - Set up webhook handling for Maxio events
