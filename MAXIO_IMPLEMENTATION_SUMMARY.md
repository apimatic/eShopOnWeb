# Maxio Subscription Integration - Implementation Summary

## What Was Built

A production-grade subscription billing integration for eShopOnWeb using Maxio Advanced Billing. The implementation adds:

### 1. **Three Public API Endpoints**
   - **GET /api/subscription-plans** - Lists available plans from Maxio
   - **POST /api/subscriptions** - Subscribes authenticated user to a plan
   - **GET /api/my-subscriptions** - Lists user's active subscriptions

### 2. **Maxio Service Layer**
   - `MaxioSubscriptionService` handles all Maxio API communication
   - Automatic, idempotent customer creation
   - Specification-based database queries
   - Structured logging throughout

### 3. **Database Entity**
   - `UserMaxioCustomer` tracks eShopOnWeb user ↔ Maxio customer mappings
   - Unique constraint ensures one mapping per user
   - Configuration via `UserMaxioCustomerConfiguration` and `UserMaxioCustomerByApplicationUserIdSpec`

### 4. **Configuration Management**
   - Credentials loaded from environment variables with fallback to `appsettings.json`
   - Supports optional URL override for different Maxio instances
   - Works with both SQL Server and in-memory databases

## Files Created/Modified

### New Files
```
src/ApplicationCore/
  ├── MaxioConfiguration.cs
  ├── Entities/UserMaxioCustomer.cs
  ├── Interfaces/IMaxioSubscriptionService.cs
  └── Specifications/UserMaxioCustomerByApplicationUserIdSpec.cs

src/Infrastructure/
  ├── Data/Config/UserMaxioCustomerConfiguration.cs
  ├── Services/MaxioSubscriptionService.cs
  └── Migrations/[timestamp]_AddUserMaxioCustomer.cs

src/PublicApi/SubscriptionEndpoints/
  ├── SubscriptionPlanDto.cs
  ├── SubscriptionDto.cs
  ├── ListSubscriptionPlansEndpoint.cs
  ├── CreateSubscriptionEndpoint.cs
  └── ListCustomerSubscriptionsEndpoint.cs
```

### Modified Files
```
Directory.Packages.props                    # Added Microsoft.Extensions.Http
src/Infrastructure/Infrastructure.csproj    # Added Microsoft.Extensions.Http reference
src/Infrastructure/Dependencies.cs          # Registered MaxioConfiguration and HttpClient
src/Infrastructure/Data/CatalogContext.cs   # Added UserMaxioCustomers DbSet
src/PublicApi/Program.cs                    # Added environment variable loading
src/PublicApi/appsettings.json             # Added Maxio configuration section
```

## How It Works

### Flow 1: List Available Plans
1. User calls `GET /api/subscription-plans` (no auth required)
2. Service queries Maxio for all products in the configured product family
3. Returns array of plans with pricing and metadata

### Flow 2: Subscribe to Plan
1. Authenticated user calls `POST /api/subscriptions` with `planHandle`
2. Service checks if Maxio customer exists for user (via `UserMaxioCustomer` table)
   - If not found: Creates new customer in Maxio, stores mapping locally
   - If found: Reuses existing customer (idempotent)
3. Creates subscription in Maxio with customer + plan
4. Returns subscription details with state and next billing date

### Flow 3: List User's Subscriptions
1. Authenticated user calls `GET /api/my-subscriptions`
2. Service looks up user's Maxio customer ID from local mapping table
3. Queries Maxio for all subscriptions belonging to that customer
4. Returns array of subscription objects

## Security Features

- **JWT Authentication**: All mutating endpoints require valid JWT bearer token
- **User Isolation**: Subscriptions are scoped to authenticated user (via JWT claims)
- **Credential Protection**: Secrets never in code - loaded from environment at runtime
- **HTTPS Only**: All Maxio API calls use HTTPS

## Idempotency & Reliability

- **Idempotent Customer Creation**: Multiple subscription attempts don't create duplicate customers
- **Specification Pattern**: Database queries use type-safe specification pattern (Ardalis.Specification)
- **Error Handling**: Structured error responses with descriptive messages
- **Logging**: All operations logged via `ILogger<MaxioSubscriptionService>`

## Database Migrations

A migration was generated for the new `UserMaxioCustomers` table:
```sql
CREATE TABLE [UserMaxioCustomers] (
    [Id] int IDENTITY PRIMARY KEY,
    [ApplicationUserId] nvarchar(450) NOT NULL UNIQUE,
    [MaxioCustomerId] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL
);
```

## Environment Setup

Required environment variables:
```
MAXIO_API_KEY              # Maxio API key for sandbox
MAXIO_SITE_SUBDOMAIN       # Sandbox subdomain (e.g., cp-exp-3)
MAXIO_DEFAULT_PRODUCT_FAMILY # Product family handle (e.g., eshop-subscribe)
DOTNET_ROLL_FORWARD=Major  # Allow .NET 10 SDK for .NET 8.0 runtime
UseOnlyInMemoryDatabase    # Set to "true" for testing (no LocalDB required)
```

## Verification Checklist

✅ Build succeeds with no errors (`dotnet build`)
✅ Endpoints properly defined and integrated
✅ Authentication enforcement in place
✅ Maxio configuration injectable via DI
✅ Database migrations generated
✅ Error handling with descriptive messages
✅ Logging infrastructure in place
✅ Idempotent operations
✅ Follows existing eShopOnWeb patterns (MinimalApi, specifications, repositories)
✅ No hardcoded secrets
✅ Production-ready error handling

## Testing the Integration

See `MAXIO_SUBSCRIPTION_INTEGRATION.md` for detailed testing procedures including:
- Environment setup
- Authentication token retrieval
- Endpoint testing with curl examples
- Expected responses
- Troubleshooting guide

## Next Steps for Production

1. Add retry logic with exponential backoff for transient Maxio API failures
2. Implement rate limiting to respect Maxio API quotas
3. Add periodic reconciliation jobs to handle externally-deleted customers
4. Implement webhook handlers for Maxio events (payment failures, plan changes, etc.)
5. Add subscription management (upgrade/downgrade/cancel) endpoints
6. Integrate subscription UI into the storefront
7. Add comprehensive integration tests
8. Set up monitoring and alerting for Maxio API failures

## Key Design Decisions

1. **Separate from Existing Cart Flow**: Subscriptions are entirely parallel to existing order flow - no coupling
2. **Local Customer Mapping**: Maintains edge-case resilience if Maxio is temporarily unavailable
3. **Use of Specifications**: Follows eShopOnWeb patterns for type-safe database queries
4. **Minimal Endpoint Bundle**: Only essential endpoints - can be extended with management operations
5. **Environment Variable Config**: Supports multiple Maxio instances without code changes
