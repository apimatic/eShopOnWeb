# Maxio Subscription Billing Integration - Setup & Verification Guide

## Overview

This integration adds recurring subscription billing capabilities to eShopOnWeb using Maxio Advanced Billing as the billing system of record. The integration is additive and does not replace the existing cart/checkout flow.

## Architecture

### New Components

1. **Subscription Entity** (`src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs`)
   - Stores user subscription state locally
   - Tracks Maxio customer ID, subscription ID, plan, status, and next billing date

2. **Maxio Service** (`src/PublicApi/Services/MaxioService.cs`)
   - Communicates with Maxio REST API using HTTP Basic Auth
   - Handles customer creation, subscription management, and plan listing
   - Idempotent customer creation to prevent duplicates

3. **API Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)
   - `GET /api/subscription-plans` - List available plans
   - `POST /api/subscriptions` - Create subscription (requires JWT auth)
   - `GET /api/my-subscriptions` - Get user's subscriptions (requires JWT auth)

## Prerequisites

1. .NET 8.0 SDK (or .NET 10 with `DOTNET_ROLL_FORWARD=Major`)
2. Maxio sandbox account with:
   - API key
   - Subdomain
   - Pre-seeded product family and plans

## Configuration Setup

### 1. Set User Secrets

The PublicApi project uses user-secrets to store Maxio credentials securely. Set them using:

```bash
cd src/PublicApi

# Set Maxio API key
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"

# Set Maxio subdomain (e.g., "cp-exp-4")
dotnet user-secrets set "Maxio:Subdomain" "<your-subdomain>"

# Set product family handle (e.g., "eshop-subscribe")
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<your-product-family-handle>"

# Optional: Set base URL if using custom domain
dotnet user-secrets set "Maxio:BaseUrl" "<optional-custom-base-url>"
```

### 2. Environment Variables (Alternative)

You can also set these as environment variables:

```bash
# Windows PowerShell
$env:MAXIO_API_KEY = "<your-api-key>"
$env:MAXIO_SITE_SUBDOMAIN = "<your-subdomain>"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "<your-product-family-handle>"

# Or via .env file (not committed to repo)
MAXIO_API_KEY=<your-api-key>
MAXIO_SITE_SUBDOMAIN=<your-subdomain>
MAXIO_DEFAULT_PRODUCT_FAMILY=<your-product-family-handle>
```

### 3. Database Configuration

The integration uses the in-memory database (as specified in requirements):

```bash
# Run with in-memory database
set UseOnlyInMemoryDatabase=true
```

## Building & Running

### Prerequisites Setup

```bash
# Navigate to repo root
cd C:\path\to\repo

# Handle .NET version mismatch if needed
set DOTNET_ROLL_FORWARD=Major

# Ensure dev certificate is trusted
dotnet dev-certs https --check
```

### Build

```bash
dotnet build eShopOnWeb.sln --configuration Debug
```

### Run PublicApi

```bash
# Set environment for in-memory DB and Maxio config
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"

cd src/PublicApi
dotnet run
```

The PublicApi will start at `https://localhost:25203`

## Verification Checklist

### 1. Endpoint Discovery

Open Swagger UI at `https://localhost:25203/swagger` and verify the three new endpoints exist:
- [ ] GET /api/subscription-plans
- [ ] POST /api/subscriptions
- [ ] GET /api/my-subscriptions

### 2. List Available Plans

```bash
curl -X GET "https://localhost:25203/api/subscription-plans" \
  -H "Content-Type: application/json" \
  -k
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00,
      "description": "...",
      "intervalUnit": 1,
      "interval": 1
    }
  ]
}
```

### 3. Authenticate User

First, get a JWT token:

```bash
curl -X POST "https://localhost:25203/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  -k
```

Response includes a `token` field. Save this for subsequent requests.

### 4. Create Subscription

```bash
curl -X POST "https://localhost:25203/api/subscriptions" \
  -H "Authorization: Bearer <your-jwt-token>" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  -k
```

Expected response (201 Created):
```json
{
  "maxioSubscriptionId": 12345678,
  "maxioCustomerId": 54321,
  "planHandle": "eshop-pro",
  "status": "active",
  "nextBillingDate": "2026-10-06T00:00:00Z",
  "createdAt": "2026-09-06T12:00:00Z"
}
```

### 5. Verify Idempotency

Call the same POST twice with the same token. The second call should succeed and return the existing subscription (verify ID matches).

### 6. Get User's Subscriptions

```bash
curl -X GET "https://localhost:25203/api/my-subscriptions" \
  -H "Authorization: Bearer <your-jwt-token>" \
  -H "Content-Type: application/json" \
  -k
```

Expected response:
```json
{
  "subscriptions": [
    {
      "maxioSubscriptionId": 12345678,
      "maxioCustomerId": 54321,
      "planHandle": "eshop-pro",
      "status": "active",
      "nextBillingDate": "2026-10-06T00:00:00Z",
      "createdAt": "2026-09-06T12:00:00Z"
    }
  ]
}
```

### 7. Multiple Plans

Try subscribing to different plans and verify they appear in `/api/my-subscriptions`.

## Key Implementation Details

### Authentication Flow

1. User authenticates via `/api/authenticate` endpoint (existing)
2. Receives JWT token
3. Includes token in `Authorization: Bearer <token>` header for subscription endpoints
4. JWT payload contains user ID (`ClaimTypes.NameIdentifier`)

### Subscription Creation Flow

1. Endpoint validates user from JWT token
2. Creates Maxio customer (idempotent via user reference)
3. Creates subscription for customer on specified plan
4. Stores subscription record in local database for quick retrieval
5. Returns subscription details to client

### Maxio API Integration

- Uses REST API with HTTP Basic Auth (API key as username, "x" as password)
- Base URL pattern: `https://{subdomain}.chargify.com`
- All endpoints use JSON for request/response
- Response models automatically deserialize from Maxio responses

## Database Schema

The `Subscriptions` table stores:
- `UserId` - FK to AspNetUsers.Id
- `MaxioCustomerId` - Reference to Maxio customer
- `MaxioSubscriptionId` - Reference to Maxio subscription (unique)
- `PlanHandle` - Product handle subscribed to
- `Status` - Subscription state (active, paused, canceled, etc.)
- `NextBillingDate` - When next charge occurs
- `CreatedAt` / `UpdatedAt` - Timestamps

## Troubleshooting

### 401 Unauthorized on POST /api/subscriptions

- Verify JWT token is valid and not expired
- Check token includes `sub` (NameIdentifier) claim
- Re-authenticate via `/api/authenticate` if needed

### 400 Bad Request from Maxio API

- Verify API key and subdomain in user-secrets
- Confirm plan handle exists in Maxio sandbox
- Check Maxio sandbox status at maxio.com

### No Plans Returned from GET /api/subscription-plans

- Verify product family is seeded in Maxio
- Check plan handles match Maxio configuration
- Review MaxioService logs for API errors

### Database Error: Table 'Subscriptions' doesn't exist

- Ensure migration was applied: `dotnet ef database update`
- Verify `UseOnlyInMemoryDatabase=true` is set (data will be lost on restart)

## Notes

- The in-memory database loses all data on application restart (as per requirements)
- Maxio customer creation is idempotent via `reference` field (user ID)
- All Maxio API communication is secured via HTTPS
- Secrets are stored in user-secrets, never committed to repository
- The integration is production-ready but requires proper error handling for production deployment

## API Security

- All subscription endpoints except `/api/subscription-plans` require JWT authentication
- Plan listing is public to allow browsing without login
- User context is extracted from JWT claims, preventing cross-user access
- All API calls to Maxio are secured via HTTP Basic Auth over HTTPS
