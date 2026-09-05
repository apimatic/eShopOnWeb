# Maxio Subscription Billing Integration — Implementation Summary

## Overview

Maxio Advanced Billing has been successfully integrated into eShopOnWeb as an **additive capability** alongside the existing one-time commerce flow. Users can now subscribe to recurring plans directly through the PublicApi.

## What Was Built

### Three New REST Endpoints (JWT-Authenticated)

1. **GET `/api/subscription-plans`** (public)
   - Returns available subscription plans from Maxio
   - No authentication required
   - Filters by the configured product family

2. **POST `/api/subscriptions`** (authenticated)
   - Subscribes the authenticated user to a plan
   - Idempotent customer creation (no duplicate customers)
   - Returns subscription ID, state, and next billing date

3. **GET `/api/my-subscriptions`** (authenticated)
   - Lists the user's active subscriptions
   - Requires JWT token from `/api/authenticate`
   - Returns subscription details for each active subscription

### Domain Model

**Subscription Entity** (`ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs`)
- Stores the association between eShopOnWeb users and Maxio subscriptions
- Fields: UserId, MaxioCustomerId, MaxioSubscriptionId, PlanHandle, State, CreatedAt, CurrentPeriodEndsAt
- Implements IAggregateRoot and persists to the catalog database

### Integration Layer

**MaxioService** (`PublicApi/Services/MaxioService.cs`)
- Handles all communication with Maxio Advanced Billing API
- Uses HTTP Basic Auth with API key
- Key methods:
  - `GetSubscriptionPlansAsync()` — lists available plans by product family
  - `GetOrCreateCustomerAsync()` — idempotent customer lookup/creation using userId as reference
  - `CreateSubscriptionAsync()` — creates subscription with product/plan validation
  - `GetCustomerSubscriptionsAsync()` — retrieves active subscriptions
  - Private helpers for product family, price point, and component lookups

### Configuration

**MaxioConfiguration** (`PublicApi/MaxioConfiguration.cs`)
- Loads from `Maxio` section in `appsettings.json`
- Keys:
  - `Maxio:ApiKey` (env: `MAXIO_API_KEY`)
  - `Maxio:Subdomain` (env: `MAXIO_SITE_SUBDOMAIN`)
  - `Maxio:ProductFamilyHandle` (env: `MAXIO_DEFAULT_PRODUCT_FAMILY`)
  - `Maxio:BaseUrl` (optional override)
- Automatically constructs the Maxio API base URL: `https://{subdomain}.chargify.com`

### Database Schema

**Migration: `20260905230845_AddSubscriptions.cs`**
- Creates `Subscriptions` table with:
  - Primary key on Id
  - Composite index on (UserId, MaxioSubscriptionId)
  - Indexed UserId for fast user lookup
  - String columns configured with appropriate lengths

### Dependency Injection

**Program.cs Updates:**
- Registers `MaxioConfiguration` as a singleton
- Registers `IMaxioService` with `HttpClient` factory
- Loads Maxio credentials from environment variables (if not in appsettings)
- Binds configuration from `Maxio:` section

## Architecture

```
Client (Browser/Postman)
    ↓ JWT Token
PublicApi Endpoints (SubscriptionEndpoints/*)
    ↓ Extract UserId from token
MaxioService (IMaxioService)
    ↓ Basic Auth with API key
Maxio Advanced Billing API
    ↓ Returns subscription details
CatalogContext (EF Core)
    ↓ Persist local subscription record
Database (LocalDB or In-Memory)
```

## Key Design Decisions

### 1. Idempotent Customer Creation
- If a user subscribes twice (race condition or double-click), the same Maxio customer is used
- Uses userId as `reference` field in Maxio to prevent duplicates
- Avoids customer proliferation in Maxio

### 2. Local Database Persistence
- Subscriptions are stored locally in eShopOnWeb's database
- Enables fast queries without hitting Maxio API for every request
- Could be synced via webhooks if Maxio state changes

### 3. No Payment Method Required
- Both plans are configured with `payment_collection_method: automatic` but no card requirement
- Suitable for trial periods or internal deployments
- Can be extended to capture payment methods via Maxio.js

### 4. Separation of Concerns
- MaxioService handles all Maxio API logic (HTTP, JSON serialization, retries)
- Endpoints handle HTTP routing and authentication
- Specification pattern (UserSubscriptionsSpec) encapsulates query logic

### 5. Configuration via Environment Variables
- Secrets never stored in the repository
- Uses ASP.NET Core user-secrets for local development
- Supports environment variable binding for CI/CD and production

## Files Created/Modified

### New Files
- `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs`
- `src/PublicApi/MaxioConfiguration.cs`
- `src/PublicApi/Services/MaxioService.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs`
- `src/ApplicationCore/Specifications/UserSubscriptionsSpec.cs`
- `src/Infrastructure/Data/Config/SubscriptionConfiguration.cs`
- `src/Infrastructure/Data/Migrations/20260905230845_AddSubscriptions.cs` (auto-generated)
- `src/Infrastructure/Data/Migrations/20260905230845_AddSubscriptions.Designer.cs` (auto-generated)

### Modified Files
- `src/Infrastructure/Data/CatalogContext.cs` — added `DbSet<Subscription>`
- `src/PublicApi/Program.cs` — registered MaxioService, loaded Maxio config
- `src/PublicApi/appsettings.json` — added Maxio config section
- `src/PublicApi/Properties/launchSettings.json` — added env vars (empty, to be populated)

## Testing

### Prerequisites
1. Maxio Advanced Billing sandbox account with seeded catalog
2. Product family: `eshop-subscribe` (ID: 3023074)
3. Plans:
   - `eshop-pro` ($299/mo)
   - `basic-plan` ($29/mo)

### Test Scenarios

**Scenario 1: Browse Plans (No Auth)**
```bash
GET /api/subscription-plans
→ Returns Pro and Basic plans with pricing
```

**Scenario 2: Subscribe User**
```bash
POST /api/subscriptions (with JWT token)
Body: { "planHandle": "eshop-pro" }
→ Creates Maxio customer (if new)
→ Creates subscription
→ Stores local record
→ Returns subscription details with next billing date
```

**Scenario 3: View Subscriptions**
```bash
GET /api/my-subscriptions (with JWT token)
→ Returns user's active subscriptions from local database
```

**Scenario 4: Verify in Maxio**
- Log into Maxio sandbox
- Navigate to Customers
- Search by email
- Confirm subscription exists with correct plan and state

### Running Tests

See `MAXIO_INTEGRATION_VERIFICATION.md` for step-by-step testing guide with curl examples.

## Production Readiness

### ✅ Implemented
- [x] JWT authentication for subscription endpoints
- [x] Idempotent customer creation
- [x] Secure credential handling (no secrets in repo)
- [x] Error handling with logging
- [x] Database schema with migrations
- [x] Comprehensive verification guide

### Recommended for Production
- [ ] Webhook subscription for Maxio state changes
- [ ] Additional endpoints: upgrade/downgrade plan, cancel subscription, view invoices
- [ ] Payment method capture (requires Maxio.js integration)
- [ ] Subscription state sync job (periodic or webhook-driven)
- [ ] Metered component usage tracking (if applicable)
- [ ] Rate limiting on subscription endpoints
- [ ] Audit logging for subscription changes

## Rollback Plan

If the integration needs to be rolled back:

1. **Database**: 
   - Revert migration: `dotnet ef migrations remove -p src/Infrastructure/Infrastructure.csproj -s src/PublicApi/PublicApi.csproj --context CatalogContext`
   - This removes the `Subscriptions` table

2. **Code**:
   - Remove subscription endpoint files and services
   - Remove MaxioConfiguration
   - Remove MaxioService registration from Program.cs
   - Remove Subscription references from CatalogContext

3. **Configuration**:
   - Remove Maxio section from appsettings.json
   - Remove env vars from launchSettings.json

## Troubleshooting

**Build Errors After Checkout**
```bash
# Ensure global.json allows .NET 10
cd repo
dotnet build eShopOnWeb.sln --no-restore
```

**Runtime: "ASP.NET Core 8.0 runtime not found"**
```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --project src/PublicApi/PublicApi.csproj
```

**Database Migration Issues**
```bash
# If migration fails during startup
dotnet ef database update -p src/Infrastructure/Infrastructure.csproj -s src/PublicApi/PublicApi.csproj --context CatalogContext

# To see pending migrations
dotnet ef migrations list -p src/Infrastructure/Infrastructure.csproj -s src/PublicApi/PublicApi.csproj --context CatalogContext
```

**Maxio API Errors**
- Verify credentials are correct and not expired
- Check that product family handle matches Maxio sandbox config
- Ensure plan handles are exact matches (case-sensitive)
- Verify network connectivity to `https://{subdomain}.chargify.com`

## Summary

The Maxio subscription billing integration is **production-ready** for:
- Listing available subscription plans
- Creating new subscriptions with idempotent customer handling
- Viewing a user's active subscriptions
- Tracking subscriptions in the local database

The implementation follows eShopOnWeb's patterns (Ardalis.ApiEndpoints, DI, EF Core, JWT auth) and integrates cleanly with the existing codebase without modifying existing commerce flows.
