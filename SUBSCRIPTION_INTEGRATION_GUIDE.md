# Maxio Subscription Billing Integration - Verification Guide

This guide walks through the complete Maxio Advanced Billing integration for eShopOnWeb subscription capabilities.

## Architecture Overview

The subscription integration adds three new HTTP API endpoints to the PublicApi project:
- `GET /api/subscription-plans` - List available subscription plans
- `POST /api/subscriptions` - Create a subscription for the authenticated user
- `GET /api/my-subscriptions` - Get subscriptions for the authenticated user

All endpoints are JWT-authenticated. The implementation follows the existing MinimalApi.Endpoint pattern in PublicApi.

## Setup Instructions

### 1. Configure Maxio Credentials

The integration requires Maxio sandbox credentials provided via environment variables. Configure these in .NET user-secrets:

```bash
cd src/PublicApi

# Set Maxio API Key
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"

# Set Maxio Site Subdomain (e.g., "cp-exp-2")
dotnet user-secrets set "Maxio:Subdomain" "<your-subdomain>"

# Set Product Family Handle (e.g., "eshop-subscribe")
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: Set explicit Base URL (overrides subdomain-derived URL)
# dotnet user-secrets set "Maxio:BaseUrl" "https://your-custom-url.chargify.com"
```

These values correspond to the environment variables mentioned in the task:
- `MAXIO_API_KEY` → `Maxio:ApiKey`
- `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
- `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`
- `MAXIO_ENVIRONMENT` and `MAXIO_BASE_URL` → optionally use custom `Maxio:BaseUrl`

### 2. Handle Environment Gotchas

**SDK/Runtime Mismatch:**
- The project pins to .NET 8.0.x but your machine has .NET 10 SDK with .NET 8.0 runtime missing
- Set `DOTNET_ROLL_FORWARD=Major` when running: `$env:DOTNET_ROLL_FORWARD='Major'`

**No LocalDB:**
- Default connection strings target `(localdb)\mssqllocaldb`, which doesn't exist
- Run with `UseOnlyInMemoryDatabase=true` to use in-memory database

**HTTPS Dev Cert:**
- Both PublicApi and Web use HTTPS redirects
- Ensure dev cert is trusted: `dotnet dev-certs https --check`
- If not trusted, install it: `dotnet dev-certs https --trust`

### 3. Database Migration

The integration adds two new tables (`MaxioCustomers` and `MaxioSubscriptions`) via Entity Framework Core migration:

```bash
# Apply the migration (required even with in-memory database for schema setup)
# The AppIdentityDbContext handles both authentication and subscription data
dotnet ef database update --project src/Infrastructure --startup-project src/PublicApi --context AppIdentityDbContext
```

With in-memory database, the migration's schema is applied in-memory at runtime.

## Testing the Integration

### Start the PublicApi Service

```powershell
# Set environment variables
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:UseOnlyInMemoryDatabase = 'true'

# Navigate to PublicApi and run
cd src/PublicApi
dotnet run
```

The service will start on `https://localhost:24523` (per launchSettings).

### 1. Authenticate and Get JWT Token

```bash
# Create a test user account
curl -X POST https://localhost:24523/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "testuser@example.com",
    "password": "P@ssw0rd!"
  }' \
  --insecure
```

You'll receive a response with a JWT `token`. Save this for subsequent calls:

```json
{
  "result": true,
  "token": "eyJhbGc...",
  "username": "testuser@example.com"
}
```

Export the token for convenience:
```bash
# PowerShell
$token = "eyJhbGc..."
```

### 2. Retrieve Available Subscription Plans

```bash
# Get all available plans from the Maxio sandbox catalog
curl -X GET https://localhost:24523/api/subscription-plans \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  --insecure
```

Expected response:
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "$299 Pro Plan",
      "description": "Professional subscription plan",
      "price": 299.00,
      "priceDisplay": "$299.00 per month",
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "handle": "basic-plan",
      "name": "$29 Basic Plan",
      "description": "Basic subscription plan",
      "price": 29.00,
      "priceDisplay": "$29.00 per month",
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 3. Create a Subscription

```bash
# Subscribe to the Pro plan
curl -X POST https://localhost:24523/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

Expected response (HTTP 201 Created):
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440001",
  "subscription": {
    "id": 1,
    "maxioSubscriptionId": 12345678,
    "productHandle": "eshop-pro",
    "state": "active",
    "price": 299.00,
    "priceDisplay": "$299.00",
    "currentPeriodEndsAt": "2026-10-06T12:34:56Z",
    "nextAssessmentAt": "2026-10-06T12:34:56Z",
    "activatedAt": "2026-09-06T12:34:56Z"
  }
}
```

**What happens internally:**
1. Creates or retrieves Maxio customer using eShopOnWeb user ID as reference
2. Calls Maxio API to create subscription (with `payment_collection_method: remittance` so no card required)
3. Stores local record in `MaxioSubscriptions` table for tracking

### 4. Retrieve User's Subscriptions

```bash
# Get all active subscriptions for the authenticated user
curl -X GET https://localhost:24523/api/my-subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  --insecure
```

Expected response:
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440002",
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 12345678,
      "productHandle": "eshop-pro",
      "state": "active",
      "price": 299.00,
      "priceDisplay": "$299.00",
      "currentPeriodEndsAt": "2026-10-06T12:34:56Z",
      "nextAssessmentAt": "2026-10-06T12:34:56Z",
      "activatedAt": "2026-09-06T12:34:56Z"
    }
  ]
}
```

### 5. Verify Idempotency

Try creating the same subscription again for the same user:

```bash
curl -X POST https://localhost:24523/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}' \
  --insecure
```

The integration is idempotent:
- First request creates a new Maxio customer (via `customer_reference` lookup)
- Second request with same user finds existing customer and creates a new subscription
- Subsequent subscriptions are separate records (multiple subscriptions per user are allowed)

### 6. Test Without Authentication

Verify endpoints require JWT authentication:

```bash
curl -X GET https://localhost:24523/api/subscription-plans \
  --insecure
```

Should return HTTP 401 Unauthorized (no Authorization header).

## Verify Against Maxio Spec

All Maxio API interactions conform to the OpenAPI specification in `maxio-spec/openapi.yaml`:

- **GET Products:** Uses `/products.json?product_family_handle=...` endpoint
- **Customer Lookup:** Uses `/customers/lookup.json?reference=...` endpoint  
- **Create Customer:** Uses `/customers.json` POST endpoint with `first_name`, `last_name`, `email`, `reference`
- **Create Subscription:** Uses `/subscriptions.json` POST endpoint with `customer_id`, `product_handle`, `payment_collection_method`
- **List Subscriptions:** Uses `/subscriptions.json?customer_id=...` endpoint

The implementation parses Maxio's JSON responses directly (no SDK) using JsonDocument for maximum spec compliance.

## Database Tables

### MaxioCustomers
- Stores mapping between eShopOnWeb `ApplicationUser.Id` and Maxio `customer_id`
- One-to-one mapping (unique index on `ApplicationUserId`)
- Idempotency: lookup by user ID before creating

### MaxioSubscriptions
- Stores individual subscription records for tracking
- References `ApplicationUserId` for quick user lookups
- Contains plan details, state, and period dates from Maxio
- Supports multiple subscriptions per user

## Code Structure

```
src/
├── ApplicationCore/
│   ├── Entities/
│   │   ├── MaxioCustomer.cs          # Entity model for customer mappings
│   │   └── MaxioSubscription.cs       # Entity model for subscriptions
│   └── Interfaces/
│       ├── IMaxioApiClient.cs         # Maxio API client interface
│       └── ISubscriptionService.cs    # Subscription business logic interface
├── Infrastructure/
│   ├── Services/
│   │   ├── MaxioApiClient.cs          # HTTP client for Maxio API
│   │   ├── SubscriptionService.cs     # Business logic implementation
│   │   └── StubSubscriptionService.cs # Stub for when Maxio not configured
│   └── Identity/
│       ├── MaxioCustomerEntityTypeConfiguration.cs
│       ├── MaxioSubscriptionEntityTypeConfiguration.cs
│       └── Migrations/
│           └── [timestamp]_AddMaxioSubscriptionTables.cs
└── PublicApi/
    ├── SubscriptionEndpoints/
    │   ├── GetSubscriptionPlansEndpoint.cs
    │   ├── CreateSubscriptionEndpoint.cs
    │   └── GetMySubscriptionsEndpoint.cs
    ├── MaxioConfiguration.cs
    ├── appsettings.json
    └── Program.cs
```

## Error Handling

- **Unauthorized (401):** Missing or invalid JWT token
- **Bad Request (400):** Missing required `ProductHandle` field
- **Service Unavailable (503):** Maxio API unreachable (Maxio client logs errors)
- **Configuration Error:** Maxio credentials not set; endpoints return stub responses (no error)

## Logging

The implementation logs key operations and errors:

- **MaxioApiClient:** Logs all Maxio API calls, HTTP errors, and JSON parsing failures
- **SubscriptionService:** Logs customer creation/retrieval and subscription operations

Enable debug logging to see detailed Maxio API interactions:

```json
"Logging": {
  "LogLevel": {
    "Microsoft.eShopWeb.Infrastructure.Services": "Debug"
  }
}
```

## Troubleshooting

**Issue:** "Maxio configuration is incomplete"
- **Cause:** User secrets not set
- **Solution:** Run the user-secrets commands from Setup section above

**Issue:** "Unable to resolve service for type 'IMaxioApiClient'"
- **Cause:** Maxio credentials not configured
- **Solution:** Endpoints still work with StubSubscriptionService; configure secrets to enable

**Issue:** JWT token errors
- **Cause:** Token expired or malformed
- **Solution:** Get a new token from `/api/authenticate`

**Issue:** Connection to Maxio fails
- **Cause:** Network issue or wrong subdomain/API key
- **Solution:** Verify Maxio credentials and network connectivity; check logs for details

## Next Steps

1. Integrate subscription plans UI into the eShopOnWeb storefront
2. Add subscription management (upgrade/downgrade, cancel)
3. Add webhook listeners for Maxio subscription events
4. Add metered usage tracking for billable components
5. Implement subscription-gated feature access in application
