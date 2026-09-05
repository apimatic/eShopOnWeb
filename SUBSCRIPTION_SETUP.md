# Maxio Subscription Billing Integration

## Overview

This document describes how to set up and verify the Maxio subscription billing integration for eShopOnWeb. The integration adds three new API endpoints for managing recurring subscriptions:

- `GET /api/subscription-plans` — List available subscription plans
- `POST /api/subscriptions` — Create a new subscription
- `GET /api/my-subscriptions` — Retrieve current user's subscriptions

## Prerequisites

### Environment Setup

The application requires the following environment variables or user secrets to be configured:

- `MAXIO_API_KEY` — Your Maxio API key (sandbox)
- `MAXIO_SITE_SUBDOMAIN` — Your Maxio site subdomain (e.g., `cp-exp-4`)
- `MAXIO_ENVIRONMENT` — Environment setting (development or production)
- `MAXIO_DEFAULT_PRODUCT_FAMILY` — Product family handle (default: `eshop-subscribe`)

### Sandbox Setup

The following entities should already exist in your Maxio sandbox (`cp-exp-4`):

| Entity | Handle | ID | Notes |
|--------|--------|----|----|
| Product Family | `eshop-subscribe` | 3023074 | Container for subscription plans |
| Pro Plan | `eshop-pro` | 7126957 | $299.00/month |
| Basic Plan | `eshop-basic` | 7126958 | $29.00/month |
| Metered Component | `api-call` | 3057195 | $0.01 per unit (optional) |

Note: IDs are subject to change after re-seeding. Always refer to handles in the API.

## Configuration

### Development: Loading Secrets

1. Navigate to the PublicApi project:
   ```powershell
   cd src/PublicApi
   ```

2. Set the Maxio configuration in user secrets:
   ```powershell
   dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"
   dotnet user-secrets set "Maxio:Subdomain" "cp-exp-4"
   dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
   ```

3. Verify secrets were stored:
   ```powershell
   dotnet user-secrets list
   ```

### Running the Application

The application requires specific configuration to run locally:

```powershell
$env:DOTNET_ROLL_FORWARD="Major"
$env:UseOnlyInMemoryDatabase="true"

cd src/PublicApi
dotnet run
```

**Important**: The in-memory database is used for simplicity and loses all data on restart. Subscriptions created in one run are not persisted to the next run.

## API Endpoints

All endpoints require JWT authentication. Get a token from the authenticate endpoint first.

### 1. Authenticate

**Endpoint**: `POST /api/authenticate`

Request:
```json
{
  "username": "demouser@example.com",
  "password": "Pass@word1"
}
```

Response:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "result": true,
  "username": "demouser@example.com"
}
```

### 2. List Subscription Plans

**Endpoint**: `GET /api/subscription-plans`

**Authentication**: Required (Bearer token)

Response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Professional Plan",
      "priceInDollars": 299.00
    },
    {
      "id": 7126958,
      "handle": "eshop-basic",
      "name": "Basic Plan",
      "priceInDollars": 29.00
    }
  ],
  "correlationId": "550e8400-e29b-41d4-a716-446655440000"
}
```

### 3. Create Subscription

**Endpoint**: `POST /api/subscriptions`

**Authentication**: Required (Bearer token)

Request:
```json
{
  "productHandle": "eshop-pro"
}
```

Response (Success):
```json
{
  "success": true,
  "message": "Subscription created successfully",
  "subscription": {
    "id": 1,
    "productHandle": "eshop-pro",
    "productName": "Professional Plan",
    "priceInDollars": 299.00,
    "state": "active",
    "currentPeriodStartsAt": "2026-09-06T12:34:56Z",
    "currentPeriodEndsAt": "2026-10-06T12:34:56Z"
  },
  "correlationId": "550e8400-e29b-41d4-a716-446655440001"
}
```

Response (Failure):
```json
{
  "success": false,
  "message": "Failed to create/retrieve customer in billing system",
  "subscription": null,
  "correlationId": "550e8400-e29b-41d4-a716-446655440002"
}
```

### 4. List User Subscriptions

**Endpoint**: `GET /api/my-subscriptions`

**Authentication**: Required (Bearer token)

Response:
```json
{
  "subscriptions": [
    {
      "id": 7126957,
      "productHandle": "eshop-pro",
      "productName": "Professional Plan",
      "priceInDollars": 299.00,
      "state": "active",
      "currentPeriodStartsAt": "2026-09-06T12:34:56Z",
      "currentPeriodEndsAt": "2026-10-06T12:34:56Z"
    }
  ],
  "correlationId": "550e8400-e29b-41d4-a716-446655440003"
}
```

## Verification Steps

### Step 1: Start the Application

```powershell
$env:DOTNET_ROLL_FORWARD="Major"
$env:UseOnlyInMemoryDatabase="true"
cd src/PublicApi
dotnet run
```

Application starts on: `https://localhost:24403` (API), `https://localhost:24401` (Web)

### Step 2: Authenticate and Get Token

Using Postman, curl, or your preferred HTTP client:

```bash
curl -X POST https://localhost:24403/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@example.com",
    "password": "Pass@word1"
  }'
```

Copy the `token` from the response.

### Step 3: List Available Plans

```bash
curl -X GET https://localhost:24403/api/subscription-plans \
  -H "Authorization: Bearer <your-token>" \
  -H "Content-Type: application/json"
```

Verify you see the Pro Plan ($299) and Basic Plan ($29).

### Step 4: Create a Subscription

```bash
curl -X POST https://localhost:24403/api/subscriptions \
  -H "Authorization: Bearer <your-token>" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

**Expected behavior**:
- If successful: subscription is created in Maxio and stored locally; `state` is `active`
- A Maxio customer is automatically created for the user (idempotent — calling again with the same user creates the subscription but not a duplicate customer)
- Response includes `currentPeriodEndsAt` showing when the next billing date is

### Step 5: Verify Subscription Was Created

```bash
curl -X GET https://localhost:24403/api/my-subscriptions \
  -H "Authorization: Bearer <your-token>" \
  -H "Content-Type: application/json"
```

**Expected**: The subscription you just created should appear in the list with state `active`.

## Implementation Details

### Architecture

- **PublicApi.SubscriptionEndpoints**: Three minimal API endpoints following the project's IEndpoint pattern
- **PublicApi.SubscriptionService**: Business logic for subscription management
- **ApplicationCore.Services.MaxioClient**: HTTP client for Maxio API calls
- **ApplicationCore.Entities.SubscriptionAggregate**: Local persistence model for subscription tracking
- **Infrastructure.Data.Subscriptions**: EF Core configuration and repository layer

### Database

- In-memory database (development): Lose subscriptions on restart
- Schema includes indices on `UserId` and `MaxioCustomerId` for efficient lookups

### Maxio API Integration

- Uses HTTP Basic Auth: `{ApiKey}:X` encoded in `Authorization` header
- Handles customer creation/lookup with `customer.reference` = user ID (idempotent)
- Creates subscriptions via `POST /subscriptions.json` with product handle
- Fetches customer subscriptions via `GET /subscriptions.json?customer_id={id}`

### Error Handling

- Maxio API errors logged and returned as `success: false` to client
- HTTP 500 on unexpected errors (for debugging)
- HTTP 401 if authentication fails

## Security Notes

1. **Secrets**: API keys are loaded from user-secrets in development and environment variables in production. Never commit credentials to version control.
2. **Authentication**: All subscription endpoints require JWT authentication. Unauthenticated requests return HTTP 401.
3. **User Isolation**: Subscriptions are tied to the authenticated user's ID. Users can only view their own subscriptions.
4. **HTTPS**: Both PublicApi and Web servers use HTTPS. Ensure dev certificates are trusted (`dotnet dev-certs https --check`).

## Troubleshooting

### "The type or namespace name 'MaxioConfiguration' could not be found"

Ensure you have:
```csharp
using Microsoft.eShopWeb.ApplicationCore;
```

### Endpoint returns 401 Unauthorized

- Check that your Bearer token is valid and not expired
- Token should be obtained from `/api/authenticate` and passed in `Authorization: Bearer <token>` header

### Endpoint returns 500 Internal Server Error

- Check application logs (written to console when running)
- Verify Maxio credentials are correct in user-secrets
- Verify network connectivity to Maxio API

### Database Entity Issues

If the Subscription table doesn't exist:
- Ensure `UseOnlyInMemoryDatabase=true` environment variable is set, OR
- Run EF Core migrations: `dotnet ef database update --context CatalogContext`

## Next Steps

### Production Deployment

1. **Persistent Database**: Replace in-memory database with SQL Server
   - Update connection string in appsettings.json
   - Run migrations: `dotnet ef database update`

2. **Secrets Management**:
   - Use Azure Key Vault or AWS Secrets Manager
   - Load credentials at application startup via dependency injection

3. **Monitoring & Logging**:
   - Send subscription events to Application Insights
   - Track subscription state changes and API errors

4. **Payment Methods** (if needed):
   - The current sandbox doesn't require payment methods
   - For production, consider collecting and validating payment profiles

### Feature Enhancements

1. **Subscription Management**:
   - Cancel/pause subscriptions
   - Update billing information
   - Handle subscription lifecycle events (renewal, expiration, failure)

2. **Webhooks**:
   - Receive real-time updates from Maxio (subscription state changes, payment events)
   - Update local database automatically

3. **Metered Usage** (if applicable):
   - Report usage via the metered component (`api-call`)
   - Bill based on actual usage

4. **Admin Console**:
   - View all subscriptions across users
   - Manage customer records
   - Handle disputes/chargebacks
