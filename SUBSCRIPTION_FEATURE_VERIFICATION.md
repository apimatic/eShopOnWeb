# eShopOnWeb Subscription Billing - Verification Guide

## Overview

This guide walks through verifying the complete subscription billing integration with Maxio Advanced Billing.

## Prerequisites

### Environment Variables

Set these environment variables before running the application:

```bash
# Maxio Sandbox Credentials (contact Maxio or use provided test account)
set MAXIO_API_KEY=your_api_key_here
set MAXIO_SITE_SUBDOMAIN=cp-exp-1
set MAXIO_ENVIRONMENT=us
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### Sandbox Entities (Already Seeded on cp-exp-1)

| Entity | Handle | Notes |
|--------|--------|-------|
| Product Family | `eshop-subscribe` | Contains the plans and metered component |
| Pro Plan | `eshop-pro` | $299.00/mo (ID: ~7126957) |
| Basic Plan | `basic-plan` | $29.00/mo (ID: ~7126958) |
| Metered Component | `api-call` | $0.01/unit (optional) |

**Important**: Plan handles are stable, but numeric IDs may change on re-seed. Always use handles for API calls.

## Verification Steps

### 1. Build and Start the Application

```bash
cd C:\claude-runs\t1h45ali-maxio-sdk-haiku45high-049\repo

# Build the solution
dotnet build src/PublicApi/PublicApi.csproj

# Run the PublicApi (runs on https://localhost:28083 by default)
dotnet run --project src/PublicApi/PublicApi.csproj
```

### 2. Get an Authentication Token

The PublicApi uses JWT authentication. Get a token from the authenticate endpoint:

```bash
# Request a JWT token (replace test values)
curl -X POST https://localhost:28083/api/account/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "email": "testuser@example.com",
    "password": "password123"
  }'

# Response will include a token:
# {
#   "username": "testuser@example.com",
#   "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
# }

# Store the token for subsequent requests
export TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

### 3. Test Endpoint 1: List Subscription Plans

```bash
curl -X GET https://localhost:28083/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  --cacert path/to/dev-cert.pem
```

**Expected Response (200 OK)**:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00,
      "intervalDays": 30
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00,
      "intervalDays": 30
    }
  ]
}
```

**Troubleshooting**:
- If no plans return: verify `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN` are set correctly
- If 401 Unauthorized: ensure `TOKEN` is valid and included in header

### 4. Test Endpoint 2: Create Subscription

```bash
curl -X POST https://localhost:28083/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  --cacert path/to/dev-cert.pem
```

**Expected Response (201 Created)**:
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "productHandle": "eshop-pro",
    "balance": 0.00,
    "nextBillingDate": "2025-10-07T00:00:00Z",
    "createdAt": "2025-09-07T14:30:00Z"
  }
}
```

**What Happens Behind the Scenes**:
1. Application extracts user ID and email from JWT token
2. Checks if a Maxio customer exists for that email
3. If not, creates a new customer in Maxio
4. Creates a subscription for the customer on the specified plan
5. Returns subscription details with next billing date

**Troubleshooting**:
- If 400 Bad Request: verify `planHandle` is one of the seeded plans (eshop-pro or basic-plan)
- If 500 Internal Server Error: check that Maxio credentials are valid and Maxio API is reachable

### 5. Test Endpoint 3: Get User's Subscriptions

```bash
curl -X GET https://localhost:28083/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --cacert path/to/dev-cert.pem
```

**Expected Response (200 OK)**:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "balance": 0.00,
      "nextBillingDate": "2025-10-07T00:00:00Z",
      "createdAt": "2025-09-07T14:30:00Z"
    }
  ]
}
```

**Troubleshooting**:
- If empty list returns: the user hasn't created any subscriptions yet (run step 4 first)
- If 401 Unauthorized: JWT token is missing or invalid

### 6. Verify Idempotency

Create the same subscription twice with the same user:

```bash
# First creation (should succeed with 201)
curl -X POST https://localhost:28083/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}' \
  --cacert path/to/dev-cert.pem
# Returns subscription with ID X

# Second creation (should still succeed)
curl -X POST https://localhost:28083/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}' \
  --cacert path/to/dev-cert.pem
```

**Expected Behavior**:
- The same customer is reused (no duplicate customer created)
- A new subscription may be created or the existing one is returned (depending on Maxio's behavior)
- No errors occur

### 7. Verify Multiple Subscriptions

Create different plan subscriptions for the same user:

```bash
# Create Pro subscription
curl -X POST https://localhost:28083/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}' \
  --cacert path/to/dev-cert.pem

# Create Basic subscription
curl -X POST https://localhost:28083/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "basic-plan"}' \
  --cacert path/to/dev-cert.pem

# List all subscriptions - should show both
curl -X GET https://localhost:28083/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --cacert path/to/dev-cert.pem
```

## Common Issues and Solutions

### Issue: "Failed to fetch subscription plans: 401"
- **Cause**: Invalid API key or incorrect Maxio site subdomain
- **Solution**: Verify `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN` environment variables match your Maxio account

### Issue: "User email not found in token"
- **Cause**: JWT token doesn't contain email claim
- **Solution**: Ensure you're using a valid token from the authenticate endpoint with the correct claims

### Issue: HTTP 400 "Plan handle is required"
- **Cause**: POST body is missing `planHandle` or it's an empty string
- **Solution**: Include valid plan handle: "eshop-pro" or "basic-plan"

### Issue: Application won't start / SSL certificate errors
- **Cause**: Development certificate not trusted or .NET runtime mismatch
- **Solution**: Run `dotnet dev-certs https --check` and `dotnet dev-certs https --trust` if needed
- For .NET 10 SDK with .NET 8.0 requirement: Run with `DOTNET_ROLL_FORWARD=Major`

### Issue: "Database contains pending migrations"
- **Cause**: Entity Framework migrations need to run (if using SQL Server)
- **Solution**: This is OK with in-memory database. If using SQL Server, run `dotnet ef database update`

## Architecture Summary

### Request Flow

```
HTTP Request (POST /api/subscriptions)
  ↓
PublicApi Endpoint (CreateSubscriptionEndpoint)
  ├─ Extracts user ID and email from JWT token
  ├─ Calls ISubscriptionService.CreateSubscriptionAsync()
  │  ↓
  │  MaxioSubscriptionService
  │  ├─ Calls ListCustomers() to find existing customer
  │  ├─ If not found, calls CreateCustomer()
  │  ├─ Stores user→customer mapping in-memory (ConcurrentDictionary)
  │  ├─ Calls CreateSubscription() with customer ID + plan handle
  │  └─ Returns subscription details
  │
  └─ Returns 201 Created with subscription data
```

### Data Storage

- **User ↔ Maxio Customer Mapping**: `ConcurrentDictionary<string, int>` in MaxioSubscriptionService (in-memory, lost on restart)
- **Subscriptions**: Retrieved on-demand from Maxio API (no local cache)
- **Plans**: Retrieved on-demand from Maxio API (no local cache)

### Maxio SDK Usage

The integration uses the `AsadAli.AdvancedBilling.Sdk` NuGet package (v1.0.2):

- **Endpoints Used**:
  - `client.ProductFamilies.ListProductsForProductFamily()` — list plans
  - `client.Customers.ListCustomers()` — search for customer by email
  - `client.Customers.CreateCustomer()` — create a new customer
  - `client.Subscriptions.CreateSubscription()` — create subscription
  - `client.Subscriptions.ListSubscriptions()` — list customer subscriptions

- **Error Handling**: Typed error models for validation failures (422), RawError for other failures
- **Authentication**: HTTP Basic auth (username = API key, password = "x")
- **Timeout**: Per-attempt timeout of 30 seconds

## Files Modified/Created

### New Files
- `src/ApplicationCore/Configuration/MaxioSettings.cs` — Maxio configuration model
- `src/ApplicationCore/Entities/SubscriptionAggregate/MaxioCustomer.cs` — User→customer mapping entity
- `src/ApplicationCore/Interfaces/ISubscriptionService.cs` — Subscription service interface
- `src/Infrastructure/Services/MaxioSubscriptionService.cs` — Maxio integration implementation
- `src/PublicApi/SubscriptionEndpoints/*.cs` — HTTP endpoints (3 files)

### Modified Files
- `src/PublicApi/Program.cs` — Added Maxio client registration and DI setup
- `src/PublicApi/appsettings.json` — Added Maxio configuration section
- `src/PublicApi/PublicApi.csproj` — Added Maxio SDK NuGet package
- `src/Infrastructure/Infrastructure.csproj` — Added Maxio SDK NuGet package

## Next Steps (Post-Verification)

1. **Persistence**: Replace in-memory customer mapping with database-backed storage (via Entity Framework)
2. **Error Handling**: Enhance error messages and add retry logic for transient failures
3. **Webhooks**: Add Maxio webhook handlers for subscription lifecycle events (renewal, cancellation, etc.)
4. **UI Integration**: Build frontend components to display plans and manage subscriptions
5. **Testing**: Add integration tests using a Maxio sandbox account

## Support

- **Maxio SDK Reference**: See `maxio-plan.md` for contract sheet with exact API signatures
- **Maxio API Docs**: https://developers.maxio.com/
- **eShopOnWeb Repo**: https://github.com/dotnet-architecture/eShopOnWeb
