# Maxio Subscription Integration — Verification Guide

This guide walks you through setting up and verifying the Maxio subscription billing integration in eShopOnWeb.

## Prerequisites

- .NET 8.0 runtime (or later with `DOTNET_ROLL_FORWARD=Major`)
- Maxio Advanced Billing sandbox account with:
  - Product Family: `eshop-subscribe`
  - Plans: `eshop-pro` ($299/mo) and `basic-plan` ($29/mo)
  - API key for authentication

## Step 1: Set Up Maxio Credentials

Store Maxio credentials in .NET user-secrets (never commit to repo):

```powershell
cd src/PublicApi

# Set credentials from your Maxio sandbox
dotnet user-secrets set "Maxio:ApiKey" "YOUR_SANDBOX_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:Environment" "us"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: custom base URL for development/mocking
# dotnet user-secrets set "Maxio:BaseUrl" "https://custom.chargify.com"
```

Verify secrets are stored:
```powershell
dotnet user-secrets list
```

## Step 2: Start the PublicApi Application

```powershell
cd src/PublicApi

# Set environment for in-memory database
$env:DOTNET_ROLL_FORWARD="Major"
$env:UseOnlyInMemoryDatabase="true"

# Run the application
dotnet run

# The app will start on https://localhost:5003
# Swagger UI available at: https://localhost:5003/swagger
```

## Step 3: Authenticate and Get JWT Token

The PublicApi uses JWT authentication. Get a token by authenticating with a demo user:

```bash
curl -X POST https://localhost:5003/api/authenticate \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "DemoPassword123!"
  }'
```

**Expected response:**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com",
  "result": true,
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "correlationId": "..."
}
```

Save the `token` value. You'll use it for all subscription endpoints.

## Step 4: Test Subscription Endpoints

### 4a. List Available Subscription Plans

```bash
curl -X GET https://localhost:5003/api/subscription-plans \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  --insecure
```

**Expected response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "$299/month subscription",
      "priceInCents": 29900,
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "$29/month subscription",
      "priceInCents": 2900,
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "errors": []
}
```

### 4b. Create a Subscription

Subscribe the authenticated user to the Pro Plan:

```bash
curl -X POST https://localhost:5003/api/subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

**Expected response (success):**
```json
{
  "subscription": {
    "id": 12345,
    "customerId": 9876543,
    "state": "active",
    "productHandle": "eshop-pro",
    "productPriceInCents": 29900,
    "productPrice": 299.00,
    "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
    "nextAssessmentAt": "2026-10-07T00:00:00Z",
    "createdAt": "2026-09-07T12:00:00Z",
    "updatedAt": "2026-09-07T12:00:00Z"
  },
  "errors": []
}
```

### 4c. Get User's Subscriptions

Retrieve all subscriptions for the authenticated user:

```bash
curl -X GET https://localhost:5003/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  --insecure
```

**Expected response:**
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "customerId": 9876543,
      "state": "active",
      "productHandle": "eshop-pro",
      "productPriceInCents": 29900,
      "productPrice": 299.00,
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
      "nextAssessmentAt": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:00:00Z",
      "updatedAt": "2026-09-07T12:00:00Z"
    }
  ],
  "errors": []
}
```

## Step 5: Verify Key Features

### Idempotent Customer Creation

Run Step 4b (create subscription) twice with the same user token. The second call should succeed and return the existing subscription (not create a duplicate).

Verify in Maxio dashboard: only one customer and one subscription should exist for the user.

### Error Handling

Try to create a subscription with an invalid product handle:

```bash
curl -X POST https://localhost:5003/api/subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "productHandle": "invalid-product"
  }'
```

**Expected:** 400 error with error details in response.

### JWT Authentication

Call an endpoint without the Authorization header:

```bash
curl -X GET https://localhost:5003/api/subscription-plans --insecure
```

**Expected:** 401 Unauthorized response.

## Architecture Overview

```
PublicApi Endpoints (JWT-authenticated minimal APIs)
    ↓
IMaxioService Interface (ApplicationCore)
    ↓
MaxioService (Infrastructure)
    ├─→ MaxioAdvancedBillingClient (SDK v1.0.2)
    │   ├─→ Products.ListProducts() — Fetch available plans
    │   ├─→ Customers.CreateCustomer() — Create Maxio customer (idempotent by reference)
    │   ├─→ Customers.ReadCustomerByReference() — Lookup existing customer
    │   └─→ Subscriptions.CreateSubscription() — Enroll user in plan
    └─→ Configuration (from user-secrets/environment)
        ├─ Maxio:ApiKey
        ├─ Maxio:Subdomain
        ├─ Maxio:Environment
        └─ Maxio:ProductFamilyHandle

Database (CatalogContext)
    └─→ UserSubscription table (optional, for audit/caching)
```

## Configuration Keys

All keys are loaded from user-secrets, environment variables, or `appsettings.json`:

| Key | Source | Example | Required |
|-----|--------|---------|----------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` env var | `test_...` | ✓ |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` env var | `eshop-demo` | ✓ |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` env var | `us` | ✓ |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` env var | `eshop-subscribe` | ✓ |
| `Maxio:BaseUrl` | None (optional override) | `https://custom.chargify.com` | ✗ |

## Troubleshooting

### Build Errors
If you get SDK compilation errors:
1. Verify `AsadAli.AdvancedBilling.Sdk v1.0.2` is installed
2. Check that Infrastructure.csproj has `<PackageReference Include="AsadAli.AdvancedBilling.Sdk" />`
3. Run `dotnet clean && dotnet build`

### Runtime Errors

**"Maxio:ApiKey configuration is required"**
- Check user-secrets are set: `dotnet user-secrets list`
- Or set environment variable: `$env:Maxio__ApiKey="your_key"`

**401 Unauthorized on subscription endpoints**
- Verify JWT token is in Authorization header with "Bearer " prefix
- Token may have expired; get a new one via `/api/authenticate`

**400 error from Maxio**
- Check product handle matches sandbox: should be `eshop-pro` or `basic-plan`
- Verify API key has access to product family `eshop-subscribe`
- Check Maxio sandbox status page

**In-memory database loses subscriptions on restart**
- This is expected behavior with `UseOnlyInMemoryDatabase=true`
- Use SQL Server for persistent storage in production
- Set `UseOnlyInMemoryDatabase=false` and configure SQL connection string

## Files Changed

### New Files
- `src/ApplicationCore/MaxioSettings.cs`
- `src/ApplicationCore/Entities/SubscriptionAggregate/UserSubscription.cs`
- `src/ApplicationCore/Interfaces/IMaxioService.cs`
- `src/Infrastructure/Services/MaxioService.cs`
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/GetUserSubscriptionsEndpoint.cs`

### Modified Files
- `Directory.Packages.props` — Added `AsadAli.AdvancedBilling.Sdk v1.0.2`
- `src/Infrastructure/Infrastructure.csproj` — Added SDK package reference
- `src/PublicApi/PublicApi.csproj` — Added SDK package reference
- `src/Infrastructure/Data/CatalogContext.cs` — Added UserSubscription DbSet
- `src/PublicApi/Program.cs` — Registered IMaxioService in DI

## Production Considerations

1. **Secrets Management** — Use Azure Key Vault or similar for credentials in production
2. **Database** — Switch to SQL Server for persistence; migrations are auto-applied on startup
3. **Logging** — ILogger is injected into MaxioService for diagnostic logging
4. **Error Handling** — All endpoints return structured error responses; integrate with your monitoring
5. **Timeouts** — Configure SDK timeouts via MaxioSettings if needed
6. **Webhooks** — Consider subscribing to Maxio webhooks for subscription state changes (not implemented in this phase)
