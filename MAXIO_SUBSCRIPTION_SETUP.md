# Maxio Subscription Billing Integration Setup Guide

This guide walks through setting up and testing the Maxio subscription billing integration added to eShopOnWeb.

## Overview

The integration adds recurring subscription functionality to eShopOnWeb via Maxio Advanced Billing. Users can:
- Browse available subscription plans
- Subscribe to a plan  
- View their active subscriptions

This is an additive feature alongside the existing cart/checkout flow.

## Configuration

### 1. Set Maxio Credentials in User Secrets

The integration reads Maxio credentials from .NET user-secrets. Set the following secrets:

```bash
cd src/PublicApi

dotnet user-secrets set "Maxio:ApiKey" "<your-maxio-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "<your-maxio-subdomain>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<your-product-family-handle>"
dotnet user-secrets set "Maxio:BaseUrl" ""  # Optional: leave empty to use subdomain-based URL
```

**For the sandbox setup described in the task:**
- API Key: From `MAXIO_API_KEY` env var
- Subdomain: From `MAXIO_SITE_SUBDOMAIN` env var  
- ProductFamilyHandle: From `MAXIO_DEFAULT_PRODUCT_FAMILY` env var (should be `eshop-subscribe`)
- BaseUrl: Leave empty (unless you need a custom override)

### 2. Environment Setup

The project expects certain environment variables/configuration:

```powershell
# For in-memory database (default for development)
$env:UseOnlyInMemoryDatabase = "true"

# Or for real SQL Server (optional)
$env:UseOnlyInMemoryDatabase = "false"
```

### 3. Build and Run

```bash
# Build the solution
dotnet build

# Run the PublicApi service
cd src/PublicApi
dotnet run
```

The PublicApi will start on `https://localhost:28683`. The in-memory database will be seeded automatically.

## API Endpoints

All subscription endpoints require JWT authentication. Get a token first:

### Authenticate
```http
POST /api/authenticate
Content-Type: application/json

{
  "username": "demouser@microsoft.com",
  "password": "Pass@word1"
}
```

Response includes `token` field. Use in subsequent requests:
```http
Authorization: Bearer <token>
```

### Get Subscription Plans
```http
GET /api/subscription-plans
Authorization: Bearer <token>
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
      "interval": "1",
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price": 29.00,
      "interval": "1",
      "intervalUnit": "month"
    }
  ]
}
```

### Create Subscription
```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

Response:
```json
{
  "id": 1,
  "maxioSubscriptionId": 12345678,
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "state": "active",
  "price": 299.00,
  "currentPeriodEndsAt": "2026-10-07T00:00:00",
  "createdAt": "2026-09-07T12:00:00"
}
```

### List User's Subscriptions
```http
GET /api/my-subscriptions
Authorization: Bearer <token>
```

Response:
```json
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 12345678,
      "productHandle": "eshop-pro",
      "productName": "eshop-pro",
      "state": "active",
      "price": 299.00,
      "currentPeriodEndsAt": "2026-10-07T00:00:00",
      "createdAt": "2026-09-07T12:00:00"
    }
  ]
}
```

## Verification Steps

### 1. Build Verification
```bash
dotnet build
# Should complete with 0 errors, 8 warnings (about System.Text.Json vulnerability and Microsoft.Extensions.Http version)
```

### 2. Database Migration
The Subscription entity includes a database migration at:
```
src/Infrastructure/Data/Migrations/20260907000000_AddSubscriptionEntity.cs
```

When using a real database, run migrations before first use:
```bash
dotnet ef database update -p src/Infrastructure/Infrastructure.csproj -s src/PublicApi/PublicApi.csproj
```

### 3. API Testing

**Via curl:**
```bash
# Get authentication token
TOKEN=$(curl -s -X POST https://localhost:28683/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username":"demouser@microsoft.com",
    "password":"Pass@word1"
  }' | jq -r '.token')

# List subscription plans
curl -s -H "Authorization: Bearer $TOKEN" \
  https://localhost:28683/api/subscription-plans | jq

# Create a subscription
curl -s -X POST https://localhost:28683/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' | jq

# List user's subscriptions
curl -s -H "Authorization: Bearer $TOKEN" \
  https://localhost:28683/api/my-subscriptions | jq
```

**Via Postman:**
1. Import PublicApi swagger from `https://localhost:28683/swagger/index.html`
2. Call `/api/authenticate` to get a token
3. Set Bearer token in Postman's Authorization tab
4. Test subscription endpoints

## Architecture

### Key Components

**Domain Model** (`ApplicationCore`)
- `Subscription` entity: Tracks user subscriptions across both eShopOnWeb and Maxio

**Service** (`Infrastructure`)
- `IMaxioBillingService`: Interface for Maxio API interaction
- `MaxioBillingService`: Implementation using HttpClient for Maxio API calls
  - Reads credentials from `Maxio:*` config section
  - Handles customer creation/lookup with idempotency (via reference field)
  - Creates subscriptions and retrieves their state

**API Endpoints** (`PublicApi`)
- `GET /api/subscription-plans`: List available plans from Maxio
- `POST /api/subscriptions`: Create subscription with customer auto-creation
- `GET /api/my-subscriptions`: List user's subscriptions from database

**Database**
- `CatalogContext` extended with `DbSet<Subscription>`
- Stores subscription ID mappings (eShopOnWeb user → Maxio customer/subscription)
- Migration handles schema for SQL Server or in-memory provider

### Design Decisions

1. **Idempotent Customer Creation**: Uses user ID as Maxio reference; duplicate subscriptions prevented at application level
2. **State Storage**: Subscription state cached in eShopOnWeb DB for quick access; Maxio is source of truth for billing
3. **No Payment Capture**: Maxio products configured to not require payment method (as per task)
4. **JWT Authentication**: Endpoints inherit PublicApi's JWT security model; user identity from token claims
5. **Error Handling**: Maxio API errors returned to client; details logged server-side

## Troubleshooting

### "Maxio:ApiKey not configured"
- Ensure user secrets are set (see Configuration section)
- User secrets take precedence over appsettings.json
- Check: `dotnet user-secrets list`

### "The subscription could not be created"
- Verify Maxio sandbox account has the product family/plans seeded
- Check Maxio API key is valid for the subdomain
- Review server logs for HTTP error from Maxio

### "User already has an active subscription"
- Expected when attempting to subscribe twice to same plan
- User must cancel existing subscription in Maxio before resubscribing

### Database Errors with SQL Server
- Run migrations: `dotnet ef database update`
- Verify connection string in appsettings.json/user secrets
- Reset database if needed: `dotnet ef database drop`

## Files Added/Modified

**New Files:**
- `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs` — Domain entity
- `src/ApplicationCore/Interfaces/IMaxioBillingService.cs` — Service interface + DTOs
- `src/Infrastructure/Services/MaxioBillingService.cs` — Maxio API client
- `src/Infrastructure/Data/Config/SubscriptionConfiguration.cs` — EF configuration
- `src/PublicApi/SubscriptionEndpoints/SubscriptionEndpoints.cs` — API endpoints
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs` — DTO
- `src/PublicApi/SubscriptionEndpoints/SubscriptionDto.cs` — DTO
- `src/Infrastructure/Data/Migrations/20260907000000_AddSubscriptionEntity.cs` — Migration

**Modified Files:**
- `src/Infrastructure/CatalogContext.cs` — Added `DbSet<Subscription>`
- `src/Infrastructure/Dependencies.cs` — Registered MaxioBillingService
- `src/PublicApi/Program.cs` — Added subscription endpoints
- `src/PublicApi/appsettings.json` — Added Maxio config section
- `Directory.Packages.props` — Added Microsoft.Extensions.Http
- `src/Infrastructure/Infrastructure.csproj` — Added Microsoft.Extensions.Http reference

## Notes

- In-memory database mode resets on restart; subscriptions are not persisted
- For production, use real SQL Server with connection string in user secrets
- Maxio API rate limits apply; no throttling implemented yet
- Payment method is not required per task (plans configured in Maxio)
- JWT token expiration handled by PublicApi's existing auth middleware
