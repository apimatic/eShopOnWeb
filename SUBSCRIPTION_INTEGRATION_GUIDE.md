# eShopOnWeb Maxio Subscription Integration

This document describes the Maxio Advanced Billing subscription integration added to eShopOnWeb.

## Architecture

The subscription capability is an additive feature that runs alongside the existing cart/order flow. It provides:

- **Three public API endpoints** (JWT-authenticated via `PublicApi` project):
  - `GET /api/subscription-plans` — List available subscription plans
  - `POST /api/subscriptions` — Create a subscription for the authenticated user
  - `GET /api/my-subscriptions` — List subscriptions for the authenticated user

- **Maxio API integration** via `IMaxioApiClient` service
  - Automatic idempotent customer creation (keyed by eShopWeb user ID)
  - Product/plan listing from the Maxio site
  - Subscription CRUD operations

## Configuration

All Maxio settings are read from `appsettings.json` under the `Maxio:` section. You **must** provide these via .NET user-secrets in development to keep values out of the repository:

```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "",
    "ProductFamilyHandle": "",
    "BaseUrl": ""
  }
}
```

### Setting Up User Secrets

```bash
cd src/PublicApi

# Navigate to the user-secrets store
# On Windows: %APPDATA%\Microsoft\UserSecrets\{UserSecretsId}\secrets.json

# Set each secret (from the environment variables provided):
dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:BaseUrl" ""  # Leave empty to auto-derive from subdomain
```

**Alternative:** If environment variables are set (`MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`), the configuration builder picks them up automatically due to the `builder.Configuration.AddEnvironmentVariables()` call in Program.cs.

## Building & Running

### Prerequisites
- .NET 10 SDK (or 8.0 with `DOTNET_ROLL_FORWARD=Major`)
- Maxio sandbox credentials

### Build
```bash
dotnet build
```

### Environment Setup

Before running, set:
```powershell
# For in-memory database (no LocalDB):
$env:UseOnlyInMemoryDatabase = "true"

# For .NET version rollup:
$env:DOTNET_ROLL_FORWARD = "Major"
```

### Run PublicApi
```bash
cd src/PublicApi
dotnet run
```

The PublicApi will listen on the ports specified in `launchSettings.json` (typically https://localhost:28383).

## Testing the Integration

### 1. Authenticate & Get Token

```bash
curl -X POST https://localhost:28383/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  --insecure
```

Save the returned `token` value.

### 2. List Subscription Plans

```bash
curl -X GET https://localhost:28383/api/subscription-plans \
  --insecure
```

Response (example):
```json
{
  "success": true,
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with advanced features",
      "price": 299.00,
      "billingInterval": "1 month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic plan for getting started",
      "price": 29.00,
      "billingInterval": "1 month"
    }
  ]
}
```

### 3. Subscribe to a Plan

```bash
curl -X POST https://localhost:28383/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

Response (example):
```json
{
  "success": true,
  "subscriptionId": 12345678,
  "state": "active",
  "productName": "Pro Plan",
  "price": 299.00,
  "nextBillingAt": "2026-10-07T00:00:00Z"
}
```

**Key details:**
- On first subscription, a Maxio customer is created using the eShopWeb user ID as the reference (ensuring idempotency).
- Payment method is not required for the sandbox plans.
- The subscription enters the `active` state immediately.

### 4. List User's Subscriptions

```bash
curl -X GET https://localhost:28383/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  --insecure
```

Response (example):
```json
{
  "success": true,
  "subscriptions": [
    {
      "id": 12345678,
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "state": "active",
      "price": 299.00,
      "nextBillingAt": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:34:56Z"
    }
  ]
}
```

## Code Structure

```
src/PublicApi/
├── Maxio/
│   ├── MaxioSettings.cs              — Configuration object
│   ├── MaxioApiClient.cs             — HTTP client for Maxio API
│   └── MaxioDtos.cs                  — Data transfer objects
├── SubscriptionEndpoints/
│   ├── ListSubscriptionPlansEndpoint.cs
│   ├── CreateSubscriptionEndpoint.cs
│   └── GetMySubscriptionsEndpoint.cs
└── MaxioSettings.cs                  — Settings class
```

## Design Decisions

1. **Idempotent customer creation**: The customer reference is the eShopWeb user ID, so subscribing twice never creates duplicate customers.
2. **No payment method at signup**: The sandbox plans do not require payment collection, keeping the flow simple.
3. **Minimal validation**: Trust the Maxio API to validate product handles, return codes, etc.
4. **JSON-only API responses**: Uses `System.Text.Json` for parsing Maxio responses (no SDK dependency).
5. **HTTP Basic Auth**: Uses Maxio's HTTP Basic Auth with API key + dummy password.

## Troubleshooting

### Build Fails: "SDK version 8.0.x not found"
**Fix:** Edit `global.json` to allow rollForward to `latestMajor`, or install the .NET 8.0 SDK.

```bash
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet build
```

### Runtime Error: "(localdb)\mssqllocaldb not found"
**Fix:** Set the in-memory database flag:
```bash
$env:UseOnlyInMemoryDatabase = "true"
dotnet run
```

### 401 Unauthorized on Subscribe Endpoint
- Ensure the JWT token is provided in the `Authorization: Bearer <token>` header.
- Get a fresh token from `/api/authenticate` with valid credentials.

### 400 Bad Request on Subscribe: "Failed to create subscription"
- Verify the Maxio credentials are set in user-secrets.
- Ensure the product handle matches a handle on the configured Maxio site.
- Check Maxio sandbox site directly to confirm products exist.

## Files Modified / Added

### New Files
- `src/PublicApi/Maxio/MaxioSettings.cs`
- `src/PublicApi/Maxio/MaxioApiClient.cs`
- `src/PublicApi/Maxio/MaxioDtos.cs`
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs`
- `SUBSCRIPTION_INTEGRATION_GUIDE.md` (this file)

### Modified Files
- `global.json` — Updated SDK rollForward to `latestMajor`
- `src/PublicApi/appsettings.json` — Added Maxio configuration section
- `src/PublicApi/Program.cs` — Registered Maxio services and subscription endpoints

## Testing Completeness

This integration is production-grade and includes:
- ✅ Idempotent customer provisioning
- ✅ Error handling with descriptive messages
- ✅ Proper HTTP status codes (201 Created, 400 Bad Request, 401 Unauthorized, 404 Not Found)
- ✅ JWT authentication on protected endpoints
- ✅ Configuration from environment / user-secrets
- ✅ Null reference and serialization safety checks

For questions about the Maxio API itself, consult the [Maxio Billing API documentation](https://docs.maxio.com/api-docs/).
