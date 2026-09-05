# Maxio Subscription Billing Integration for eShopOnWeb

## Overview

This integration adds recurring subscription billing capabilities to the eShopOnWeb reference application using Maxio Advanced Billing as the billing system of record. The feature is fully additive and does not modify the existing cart/checkout flow.

## Architecture

### New Components

1. **MaxioSubscriptionService** (`src/PublicApi/Services/MaxioSubscriptionService.cs`)
   - Handles all Maxio API interactions
   - Implements idempotent customer creation
   - Manages subscription lifecycle operations

2. **REST Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)
   - `GET /api/subscription-plans` - List available plans
   - `POST /api/subscriptions` - Subscribe user to a plan
   - `GET /api/my-subscriptions` - Retrieve user's subscriptions
   - All endpoints require JWT Bearer authentication

3. **Data Transfer Objects** (`src/PublicApi/SubscriptionEndpoints/`)
   - SubscriptionPlanDto - Plan information
   - SubscriptionDto - Subscription details
   - Request/Response models following existing API patterns

## Configuration

### Environment Variables

Set these environment variables before running:
```
MAXIO_API_KEY=<your-api-key>
MAXIO_SITE_SUBDOMAIN=<your-subdomain>
MAXIO_ENVIRONMENT=<US|EU>  # Optional, defaults to US
MAXIO_BASE_URL=<override-url>  # Optional, for custom endpoints
DOTNET_ROLL_FORWARD=Major  # Required if running with .NET 10 SDK
```

### appsettings.json Configuration

The `Maxio:` section is already configured:
```json
{
  "Maxio": {
    "ApiKey": "your-api-key-here",
    "Subdomain": "your-site",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": null,
    "Environment": "US"
  },
  "UseOnlyInMemoryDatabase": true
}
```

Values from environment variables take precedence over appsettings.json.

## Running the Application

### Build

```bash
dotnet build eShopOnWeb.sln -c Release
```

### Run (with in-memory database)

```bash
$env:DOTNET_ROLL_FORWARD="Major"
$env:MAXIO_API_KEY="your-key"
$env:MAXIO_SITE_SUBDOMAIN="cp-exp-1"

dotnet run --project src/PublicApi/PublicApi.csproj
```

The PublicApi will start on `https://localhost:24663` by default.

## Testing the Integration

### Step 1: Get Authentication Token

Use the existing authenticate endpoint to get a JWT token:

```bash
curl -X POST https://localhost:24663/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"user@example.com","password":"Pass123$"}' \
  --insecure
```

Extract the `token` from the response.

### Step 2: List Available Plans

```bash
curl -X GET https://localhost:24663/api/subscription-plans \
  -H "Authorization: Bearer <token>" \
  --insecure
```

Response includes plan handle, name, price, and interval information.

### Step 3: Subscribe to a Plan

```bash
curl -X POST https://localhost:24663/api/subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

Response includes:
- Subscription ID
- State (e.g., "active", "pending")
- Product price in cents
- Next assessment/billing date

### Step 4: Retrieve User's Subscriptions

```bash
curl -X GET https://localhost:24663/api/my-subscriptions \
  -H "Authorization: Bearer <token>" \
  --insecure
```

Returns all subscriptions for the authenticated user.

## Sandbox Entities

The following are already seeded on the Maxio sandbox (`cp-exp-1` site):

| Entity | Handle | Type | Value |
|--------|--------|------|-------|
| Product Family | `eshop-subscribe` | Container | N/A |
| Pro Plan | `eshop-pro` | Monthly | $299.00/mo |
| Basic Plan | `basic-plan` | Monthly | $29.00/mo |
| Metered Component | `api-call` | Per Unit | $0.01/unit |

All plans: No trial, no setup fee, never expires, no tax, payment method not required.

## Key Implementation Details

### Idempotent Customer Creation

The integration uses email as the unique customer identifier:

1. Attempt `ReadCustomerByReference(userEmail)`
2. If 404, create customer with `CreateCustomer`
3. Store the Maxio customer ID for future calls

This ensures no duplicate customers are created on retries.

### Idempotent Subscriptions

Each subscription request includes a unique reference:
```
{userId}-{planHandle}-{timestamp}
```

Combined with customer idempotence, this prevents duplicate subscriptions.

### Error Handling

The service handles two categories of SDK errors:

1. **Typed Errors (Case A)** - CreateCustomerError, CreateSubscriptionError
   - Use `TryGet*` accessors to read typed error responses
   - Field-level validation errors are extracted and returned

2. **Raw Errors (Case B)** - Generic HTTP errors
   - Read status code and error body directly
   - Wrap in InvalidOperationException for API response

### JWT Authentication

- All endpoints require JWT Bearer token in the Authorization header
- Token is obtained from the existing `/api/authenticate` endpoint
- User identity is extracted from token claims:
  - `email` claim (preferred)
  - `sub` claim (fallback)

## File Structure

```
src/PublicApi/
├── MaxioOptions.cs                          # Configuration model
├── Services/
│   └── MaxioSubscriptionService.cs          # Business logic
└── SubscriptionEndpoints/
    ├── ListSubscriptionPlansEndpoint.cs     # GET /api/subscription-plans
    ├── CreateSubscriptionEndpoint.cs        # POST /api/subscriptions
    ├── ListMySubscriptionsEndpoint.cs       # GET /api/my-subscriptions
    ├── SubscriptionPlanDto.cs
    ├── SubscriptionDto.cs
    └── Request/Response models
```

## Database Considerations

In this implementation:
- Uses in-memory database (per environment constraints)
- Maxio customer IDs are fetched on-demand
- Subscriptions are managed entirely by Maxio
- No local subscription state is persisted

For production use with persistent storage:
1. Add tables to track user ↔ Maxio customer mapping
2. Cache subscription state and sync periodically
3. Implement webhook handlers for subscription state changes

## Maxio SDK Reference

The integration uses the Maxio Advanced Billing .NET SDK (`AsadAli.AdvancedBilling.Sdk` v1.0.2) with the namespace `MaxioAdvancedBilling`.

### Key SDK Classes

- `MaxioAdvancedBillingClient` - Main client (injected via DI)
- `ServerEnvironment.Us` / `ServerEnvironment.Eu` - Environment selection
- `BasicAuthCredentials` - Authentication (username=API key, password="x")

### Operations Called

1. `client.Products.ListProducts(...)` - List plans by product family
2. `client.Customers.ReadCustomerByReference(...)` - Lookup customer
3. `client.Customers.CreateCustomer(...)` - Create customer
4. `client.Subscriptions.CreateSubscription(...)` - Create subscription
5. `client.Customers.ListCustomerSubscriptions(...)` - List user subscriptions

## Production Deployment Checklist

- [ ] Configure real Maxio API key and site subdomain
- [ ] Enable HTTPS certificate validation (remove `--insecure` from tests)
- [ ] Set up persistent database with customer mapping table
- [ ] Implement subscription state webhook handlers
- [ ] Configure error logging and monitoring
- [ ] Add rate limiting to prevent API abuse
- [ ] Set up payment method collection UI (if desired)
- [ ] Test with real payment methods in staging
- [ ] Document billing reconciliation process

## Support & Troubleshooting

### Common Issues

**Issue**: `AdvancedBillingClient not found`
- **Fix**: Ensure using statement is `using MaxioAdvancedBilling;` (not the package name)

**Issue**: Customer creation returns 422 with duplicate reference error
- **Cause**: Previous request succeeded but client didn't receive response
- **Fix**: Implement idempotent key tracking with TTL cache

**Issue**: Subscription state shows as "pending"
- **Cause**: Subscription not yet activated by Maxio
- **Fix**: Poll `ListCustomerSubscriptions` until state changes to "active"

**Issue**: Missing payment method required error
- **Cause**: Plans configured on Maxio require payment method
- **Fix**: Verify sandbox plans have "payment method not required" setting

## Next Steps

1. Replace environment variables with real Maxio credentials
2. Add persistent database for customer mappings
3. Implement webhook handlers for subscription state changes
4. Add subscription management UI (update/cancel)
5. Integrate metered billing for the `api-call` component
6. Add retry logic for transient failures
7. Monitor and alert on subscription errors
