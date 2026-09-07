# Maxio Subscription Billing Integration Setup Guide

This guide walks you through setting up and testing the Maxio subscription billing integration added to eShopOnWeb.

## Prerequisites

- .NET 8.0 SDK (or .NET 10 with rollforward enabled)
- HTTPS dev certificate configured (`dotnet dev-certs https --check`)
- Maxio Advanced Billing sandbox credentials
- In-memory database configuration (no LocalDB required)

## Configuration

### 1. Environment Variables

Set these environment variables before running the application:

```bash
# Required
set MAXIO_API_KEY=<your-api-key>
set MAXIO_SITE_SUBDOMAIN=cp-exp-4
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe

# Optional
set MAXIO_BASE_URL=<override-base-url>  # e.g., https://cp-exp-4.chargify.com
set UseOnlyInMemoryDatabase=true
set DOTNET_ROLL_FORWARD=Major  # If using .NET 10 with 8.0 pinned
```

### 2. Verify Configuration

The PublicApi launchSettings.json includes placeholder values for MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY. You only need to provide MAXIO_API_KEY via:
- Environment variables (recommended for production)
- .NET user-secrets (for local development)

### 3. Set Up User Secrets (Development Only)

For local development without environment variables:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-4"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

To list stored secrets:
```bash
dotnet user-secrets list
```

## Running the Application

### PublicApi (Subscription Endpoints)

```bash
cd src/PublicApi
dotnet run
```

The API will be available at `https://localhost:28483`

Swagger documentation: `https://localhost:28483/swagger`

### Database Migrations

The migration `AddSubscriptionSupport` is included. To apply it manually:

```bash
cd src/Infrastructure
dotnet ef database update -s ../PublicApi/PublicApi.csproj --context CatalogContext
```

Note: With in-memory database, migrations don't apply, but schema is created on startup.

## API Endpoints

### 1. List Subscription Plans

```bash
GET /api/subscription-plans
```

Response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription",
      "price": 299.00,
      "priceFormatted": "$299.00/month"
    }
  ],
  "correlationId": "..."
}
```

### 2. Create Subscription

```bash
POST /api/subscriptions
Authorization: Bearer <jwt-token>
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

Response:
```json
{
  "subscription": {
    "id": 123456789,
    "state": "active",
    "productHandle": "eshop-pro",
    "currentPrice": 299.00,
    "priceFormatted": "$299.00/month",
    "nextBillingAt": "2026-10-07",
    "nextBillingAtFormatted": "2026-10-07",
    "billingPeriodLength": 1,
    "billingPeriodUnit": "month"
  },
  "correlationId": "..."
}
```

### 3. List User Subscriptions

```bash
GET /api/my-subscriptions
Authorization: Bearer <jwt-token>
```

Response:
```json
{
  "subscriptions": [
    {
      "id": 123456789,
      "state": "active",
      "productHandle": "eshop-pro",
      "currentPrice": 299.00,
      "priceFormatted": "$299.00/month",
      "nextBillingAt": "2026-10-07",
      "billingPeriodLength": 1,
      "billingPeriodUnit": "month"
    }
  ],
  "correlationId": "..."
}
```

## Obtaining JWT Token

To test authenticated endpoints, get a token from the auth endpoint:

```bash
POST /api/account/authenticate
Content-Type: application/json

{
  "email": "user@example.com",
  "password": "password123"
}
```

Response contains `accessToken` - use this as the Bearer token.

## Testing Flow

### Step 1: Get Available Plans
```bash
curl -k https://localhost:28483/api/subscription-plans
```

### Step 2: Authenticate
```bash
curl -k -X POST https://localhost:28483/api/account/authenticate \
  -H "Content-Type: application/json" \
  -d '{"email": "user@example.com", "password": "password"}'
```

Store the returned `accessToken`.

### Step 3: Create Subscription
```bash
curl -k -X POST https://localhost:28483/api/subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}'
```

### Step 4: List Subscriptions
```bash
curl -k https://localhost:28483/api/my-subscriptions \
  -H "Authorization: Bearer <token>"
```

## Architecture

### Components

1. **MaxioService** (`src/ApplicationCore/Services/MaxioService.cs`)
   - HTTP client for Maxio API calls
   - Handles authentication (Basic Auth with API key)
   - Manages customer and subscription lifecycle

2. **Entities** (`src/ApplicationCore/Entities/SubscriptionAggregate/`)
   - `MaxioCustomer`: Maps eShopOnWeb user to Maxio customer
   - `Subscription`: Stores subscription metadata for offline access

3. **Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)
   - `ListSubscriptionPlansEndpoint`: GET /api/subscription-plans
   - `CreateSubscriptionEndpoint`: POST /api/subscriptions
   - `ListMySubscriptionsEndpoint`: GET /api/my-subscriptions

4. **Database** (`src/Infrastructure/Data/`)
   - `MaxioCustomerConfiguration`: EF Core mapping
   - `SubscriptionConfiguration`: EF Core mapping
   - Migration: `AddSubscriptionSupport`

### Maxio Integration Details

- **Authentication**: HTTP Basic Auth (API key + 'x' as password)
- **Base URL**: `https://{subdomain}.chargify.com` (configurable via env var)
- **API Format**: JSON request/response
- **Idempotency**: Customer creation is idempotent (checked by reference/userId)

### Database Schema (In-Memory)

```
MaxioCustomers
├── Id (int, PK)
├── UserId (string, unique)
├── MaxioCustomerId (long, unique)
├── CreatedAt (DateTime)
└── UpdatedAt (DateTime)

Subscriptions
├── Id (int, PK)
├── UserId (string, indexed)
├── MaxioSubscriptionId (long, unique)
├── ProductHandle (string)
├── State (string)
├── CurrentPrice (decimal)
├── NextBillingAt (DateTime, nullable)
├── BillingPeriodUnit (string)
├── BillingPeriodLength (int)
├── CreatedAt (DateTime)
└── UpdatedAt (DateTime)
```

## Troubleshooting

### "Maxio configuration is missing"
- Ensure MAXIO_API_KEY is set in environment variables or user-secrets
- Check MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY are configured

### "401 Unauthorized" from Maxio
- Verify API key is correct
- Check that Maxio site is in sandbox mode
- Ensure credentials haven't expired

### "No subscription found"
- User must first create a subscription for the same product handle
- Check that user exists in the system

### Database errors with LocalDB
- Ensure UseOnlyInMemoryDatabase=true is set
- Or install ASP.NET Core 8.0 runtime

## Notes

- The integration is **additive** - existing cart/checkout flow is unaffected
- **No payment method required** - Sandbox plans have payment optional enabled
- **Idempotent customer creation** - Safe to call multiple times
- **User-scoped access** - Each user can only see their own subscriptions
- **In-memory storage** - Data resets on application restart (development)

## Files Added

- `src/ApplicationCore/Entities/SubscriptionAggregate/MaxioCustomer.cs`
- `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs`
- `src/ApplicationCore/Configurations/MaxioConfiguration.cs`
- `src/ApplicationCore/Services/MaxioService.cs`
- `src/ApplicationCore/Specifications/MaxioCustomerByUserIdSpecification.cs`
- `src/Infrastructure/Data/Config/MaxioCustomerConfiguration.cs`
- `src/Infrastructure/Data/Config/SubscriptionConfiguration.cs`
- `src/Infrastructure/Data/Migrations/[timestamp]_AddSubscriptionSupport.cs`
- `src/PublicApi/EmptyRequest.cs`
- `src/PublicApi/SubscriptionEndpoints/*` (5 endpoint files + DTOs)

## Files Modified

- `src/Infrastructure/Data/CatalogContext.cs` (added DbSets)
- `src/PublicApi/Program.cs` (registered Maxio service + configuration)
- `src/PublicApi/appsettings.json` (added Maxio configuration section)
- `src/PublicApi/Properties/launchSettings.json` (added environment variables)
