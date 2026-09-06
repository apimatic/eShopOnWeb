# Maxio Subscription Billing Integration Setup

## Prerequisites

- .NET 8.0 SDK or later (or run with `DOTNET_ROLL_FORWARD=Major`)
- ASP.NET Core 8.0 runtime (or use rollForward in global.json)
- Maxio Advanced Billing sandbox account with credentials
- Maxio sandbox site already seeded with demo catalog (product family `eshop-subscribe`, plans `eshop-pro` and `basic-plan`)

## Configuration

The integration uses the following configuration keys from the `Maxio:` section:

- `Maxio:ApiKey` - Maxio API key (from env var `MAXIO_API_KEY`)
- `Maxio:Subdomain` - Maxio site subdomain (from env var `MAXIO_SITE_SUBDOMAIN`)
- `Maxio:ProductFamilyHandle` - Default product family handle (from env var `MAXIO_DEFAULT_PRODUCT_FAMILY`)
- `Maxio:BaseUrl` (optional) - Override Maxio API base URL (uses subdomain by default)

## Setting Up Secrets

### Option 1: Environment Variables (Development)

Set environment variables before running:

```powershell
# PowerShell
$env:MAXIO_API_KEY = "your_api_key_here"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain_here"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"

cd src/PublicApi
dotnet run
```

### Option 2: User Secrets (.NET User Secrets)

Use the .NET user-secrets command to store secrets securely:

```powershell
cd src/PublicApi

# Set the Maxio API key
dotnet user-secrets set "Maxio:ApiKey" "your_api_key_here"

# Set the Maxio subdomain
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain_here"

# Set the product family handle
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Verify the secrets are set
dotnet user-secrets list
```

## Running the Application

### With In-Memory Database (Development/Testing)

```powershell
cd src/PublicApi
$env:UseOnlyInMemoryDatabase = "true"
$env:MAXIO_API_KEY = "your_api_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"

dotnet run
```

### With LocalDB (if SQL Server Express is installed)

```powershell
cd src/PublicApi
$env:MAXIO_API_KEY = "your_api_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"

# First time: apply migrations
dotnet ef database update

dotnet run
```

## Sandbox Seeded Entities

These are pre-configured on the `cp-exp-3` site:

- **Product Family**: `eshop-subscribe` (handle)
- **Plans**:
  - `eshop-pro` - $299.00/mo
  - `basic-plan` - $29.00/mo
- **Component**: `api-call` - $0.01/unit (metered)

Both plans have:
- No trial
- No setup fee
- No expiration
- Payment method not required (allows remittance collection)

## API Endpoints

All endpoints are JWT-authenticated. Obtain a token via the authentication endpoint first.

### Authentication
```
POST /api/authenticate
Content-Type: application/json

{
  "username": "user@example.com",
  "password": "password"
}

Response:
{
  "token": "eyJ...",
  "result": true,
  ...
}
```

### List Subscription Plans
```
GET /api/subscription-plans

Response:
{
  "plans": [
    {
      "id": 7126957,
      "name": "$299 Pro Plan",
      "handle": "eshop-pro",
      "description": "...",
      "pricePerMonth": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Create Subscription
```
POST /api/subscriptions
Authorization: Bearer {token}
Content-Type: application/json

{
  "planHandle": "eshop-pro"
}

Response (201 Created):
{
  "subscription": {
    "id": 12345,
    "state": "active",
    "customerId": 67890,
    "activatedAt": "2026-09-06T...",
    "canceledAt": null,
    "currentPeriodStartsAt": "2026-09-06T...",
    "currentPeriodEndsAt": "2026-10-06T...",
    "nextAssessmentAt": "2026-10-06T...",
    "productPricePerMonth": 299.00,
    "productName": "$299 Pro Plan",
    "productHandle": "eshop-pro"
  }
}
```

### Get User's Subscriptions
```
GET /api/my-subscriptions
Authorization: Bearer {token}

Response:
{
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      ...
    }
  ]
}
```

## How It Works

1. **Customer Idempotency**: When a user subscribes for the first time, the integration automatically:
   - Checks if a Maxio customer already exists for that user (by reference ID = user ID)
   - Creates one if it doesn't exist
   - Stores the mapping in `UserMaxioCustomerMapping` table (in-memory or database)

2. **Subscription Creation**: 
   - Uses the remittance collection method (no payment card required)
   - Creates subscription immediately with no trial period
   - Syncs next billing date and plan details back to the client

3. **Data Persistence**:
   - User ↔ Maxio Customer ID mappings stored in database/memory
   - All subscription state stored in Maxio (source of truth)

## Verification Steps

1. **Build succeeds**:
   ```powershell
   dotnet build eShopOnWeb.sln -c Debug
   ```

2. **Run PublicApi**:
   ```powershell
   cd src/PublicApi
   dotnet run
   ```

3. **Authenticate** (curl/Postman):
   ```
   POST https://localhost:25263/api/authenticate
   { "username": "demouser@microsoft.com", "password": "Pass@word1" }
   ```

4. **List plans**:
   ```
   GET https://localhost:25263/api/subscription-plans
   ```

5. **Subscribe** (replace token):
   ```
   POST https://localhost:25263/api/subscriptions
   Authorization: Bearer <token>
   { "planHandle": "eshop-pro" }
   ```

6. **Get user subscriptions**:
   ```
   GET https://localhost:25263/api/my-subscriptions
   Authorization: Bearer <token>
   ```

## Troubleshooting

### "MAXIO_API_KEY not set"
- Ensure environment variable or user-secret is configured
- Check Program.cs logs for configuration loading

### "Failed to create customer" / API 401/403
- Verify API key and subdomain are correct
- Check Maxio API docs for authentication

### "Plan not found" / API 422
- Ensure the plan handle matches a plan in your Maxio site
- Check if the plan is in the same product family

### In-Memory Database Lost on Restart
- In-memory DB loses all data (including customer mappings) on app restart
- Use a real database for persistence, or accept this limitation for dev/testing

## Architecture

- **MaxioConfiguration**: Settings bound from `Maxio:*` config section
- **MaxioHttpClient**: HTTP transport with Basic Auth (API key:x)
- **MaxioService**: Business logic for customers, subscriptions, plans
- **Endpoints**: REST API following eShopOnWeb conventions
- **UserMaxioCustomerMapping**: Entity to track user ↔ customer relationships
