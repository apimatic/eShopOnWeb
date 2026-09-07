# Maxio Advanced Billing Integration Setup

This guide explains how to set up and test the Maxio Advanced Billing subscription integration for eShopOnWeb.

## Prerequisites

- .NET SDK 8.0 or later (the repo pins to 8.0.x with `rollForward: latestMajor`)
- Maxio Advanced Billing sandbox account
- eShopOnWeb repository cloned locally

## Environment Setup

### 1. Configure Maxio Credentials

The integration reads Maxio credentials from environment variables or .NET user-secrets. **Never hardcode credentials in the repository.**

#### Option A: User Secrets (Recommended for development)

```bash
cd src/PublicApi

# Initialize user-secrets if not already done
dotnet user-secrets init

# Set Maxio credentials
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-subdomain"
dotnet user-secrets set "Maxio:Environment" "Us"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: if you have a custom base URL (for testing/mocking)
dotnet user-secrets set "Maxio:BaseUrl" "https://your-custom-url"
```

#### Option B: Environment Variables

```powershell
# PowerShell
$env:MAXIO_API_KEY="your-api-key"
$env:MAXIO_SITE_SUBDOMAIN="your-subdomain"
$env:MAXIO_ENVIRONMENT="Us"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Then run the application
dotnet run --project src/PublicApi
```

### 2. Configuration Bindings

The `Maxio:` configuration section maps to these keys:
- `Maxio:ApiKey` — Maxio API Key (from `MAXIO_API_KEY` env var)
- `Maxio:Subdomain` — Maxio sandbox subdomain (from `MAXIO_SITE_SUBDOMAIN` env var)
- `Maxio:Environment` — "Us" or "Eu" (from `MAXIO_ENVIRONMENT` env var)
- `Maxio:ProductFamilyHandle` — Product family handle (from `MAXIO_DEFAULT_PRODUCT_FAMILY` env var)
- `Maxio:BaseUrl` — Optional override for API base URL

All values can be overridden by environment variables using the `Configuration:<Key>` pattern.

### 3. Database Setup

The PublicApi uses Entity Framework Core with an in-memory database by default. If using SQL Server LocalDB, set the connection string in `appsettings.json` or use the `UseOnlyInMemoryDatabase` flag.

For in-memory database (default):
```bash
DOTNET_ROLL_FORWARD=Major dotnet run --project src/PublicApi
```

For SQL Server:
```bash
DOTNET_ROLL_FORWARD=Major dotnet run --project src/PublicApi -- --use-real-db
```

## Running the Application

### Start PublicApi

```bash
cd repo
$env:DOTNET_ROLL_FORWARD="Major"
dotnet run --project src/PublicApi
```

The PublicApi will start on `https://localhost:7200` (or similar, check the output).

### JWT Authentication

All subscription endpoints require JWT authentication. First, authenticate to get a bearer token:

```bash
# Get a token (requires credentials seeded in the database)
curl -X POST "https://localhost:7200/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@example.com",
    "password": "password"
  }' --insecure

# Response contains a token field
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

Then use the token for subsequent calls:

```bash
# Bearer token
export TOKEN="<token from authenticate>"
```

## API Endpoints

### 1. List Subscription Plans

**GET** `/api/subscription-plans`

Lists all available subscription plans from the configured Maxio product family.

```bash
curl -X GET "https://localhost:7200/api/subscription-plans" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 2. Create a Subscription

**POST** `/api/subscriptions`

Subscribe the logged-in user to a plan. Automatically creates a Maxio customer if one doesn't exist (idempotent via `Reference` field).

**Request:**
```bash
curl -X POST "https://localhost:7200/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

**Response:**
```json
{
  "subscription": {
    "id": 12345,
    "customerId": 67890,
    "productHandle": "eshop-pro",
    "state": "active",
    "createdAt": "2026-09-07T10:30:00Z",
    "nextBillingAt": null
  }
}
```

### 3. List User's Subscriptions

**GET** `/api/my-subscriptions`

List all subscriptions for the authenticated user.

```bash
curl -X GET "https://localhost:7200/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "customerId": 67890,
      "productHandle": "eshop-pro",
      "state": "active",
      "createdAt": "2026-09-07T10:30:00Z",
      "nextBillingAt": null
    }
  ]
}
```

## Error Handling

The integration converts Maxio SDK errors into HTTP status codes:

- **400** — Bad request (invalid input)
- **401** — Unauthorized (authentication failure)
- **404** — Not found (customer/plan doesn't exist)
- **422** — Unprocessable entity (validation error from Maxio)
- **500** — Internal server error (connection failure or unexpected error)

Example error response:
```json
{
  "error": "Failed to create subscription"
}
```

## Testing the Integration

### Smoke Test

1. Start the PublicApi:
   ```bash
   $env:DOTNET_ROLL_FORWARD="Major"
   dotnet run --project src/PublicApi
   ```

2. Authenticate:
   ```bash
   curl -X POST "https://localhost:7200/api/authenticate" \
     -H "Content-Type: application/json" \
     -d '{"username":"demouser@example.com","password":"password"}' \
     --insecure
   ```

3. List plans:
   ```bash
   curl -X GET "https://localhost:7200/api/subscription-plans" \
     -H "Authorization: Bearer YOUR_TOKEN" \
     --insecure
   ```

4. Create a subscription:
   ```bash
   curl -X POST "https://localhost:7200/api/subscriptions" \
     -H "Authorization: Bearer YOUR_TOKEN" \
     -H "Content-Type: application/json" \
     -d '{"productHandle":"eshop-pro"}' \
     --insecure
   ```

5. List user's subscriptions:
   ```bash
   curl -X GET "https://localhost:7200/api/my-subscriptions" \
     -H "Authorization: Bearer YOUR_TOKEN" \
     --insecure
   ```

### Expected Behavior

- ✅ Listing plans returns the configured product family's plans
- ✅ Creating a subscription creates a Maxio customer (first time only) and a subscription
- ✅ Multiple subscriptions for the same user don't create duplicate customers
- ✅ Listing subscriptions returns the user's active subscriptions
- ✅ All endpoints require authentication

## Architecture Overview

### Components

- **SubscriptionService** (`src/PublicApi/SubscriptionEndpoints/SubscriptionService.cs`) — Core service wrapping Maxio SDK calls with error handling
- **Endpoints** — Three RESTful endpoints for subscription management
- **MaxioOptions** — Configuration POCO for Maxio settings
- **SubscriptionException** — Custom exception type for subscription-specific errors

### Error Handling

The integration implements a boundary for Maxio SDK errors per the `dotnet-error-handling` skill:

- **Case A (typed errors)** — `CreateCustomer` and `CreateSubscription` throw `SdkException<CreateCustomerError>` and `SdkException<CreateSubscriptionError>`, with typed `TryGet*` accessors for different HTTP status codes
- **Case B (raw errors)** — `ReadCustomerByReference` and `ListCustomerSubscriptions` throw `SdkException<RawError>`, with direct status/body access
- **JSON exceptions** — Both 2xx deserialization failures and non-2xx schema mismatches are caught and converted to `SubscriptionException`
- **Transport failures** — `HttpRequestException` is caught and converted to `SubscriptionException` for a consistent error type at the boundary

## Next Steps

- Integrate the subscription endpoints into the eShopOnWeb frontend (Blazor/React)
- Add webhook handling for Maxio lifecycle events (subscription state changes, renewals, cancellations)
- Implement invoice/billing history views
- Add subscription management UI (cancel, update plan, pause)

## Troubleshooting

### "Unable to find package AsadAli.AdvancedBilling.Sdk"

Ensure NuGet can reach nuget.org. Check:
```bash
dotnet nuget list source
```

### Maxio API Returns 401

The API key or subdomain is incorrect. Verify:
- API Key is correct (check Maxio dashboard → Settings → API → API Keys)
- Subdomain matches your Maxio sandbox (e.g., if your Maxio URL is `https://myshop.chargify.com`, the subdomain is `myshop`)
- Credentials are loaded from environment or user-secrets (not hardcoded)

### "Customer not found" on Subscribe

This is expected if the customer doesn't have a Maxio record yet. The integration creates one automatically on first subscribe using the `Reference` field to link it to the eShopOnWeb user.

### In-Memory Database Loses Data on Restart

By default, PublicApi uses `Microsoft.EntityFrameworkCore.InMemory`, which is ephemeral. All data is lost on restart. This is intentional for development. To persist data, configure SQL Server or another database provider in `appsettings.json`.

## References

- [Maxio Advanced Billing API Documentation](https://maxio-chargify.gitbook.io/billable/api-reference)
- [eShopOnWeb Repository](https://github.com/dotnet-architecture/eShopOnWeb)
- [ASP.NET Core Identity Documentation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity)
