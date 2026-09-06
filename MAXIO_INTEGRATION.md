# Maxio Subscription Billing Integration

This document describes the Maxio subscription billing integration added to eShopOnWeb, including setup, configuration, and verification steps.

## Overview

The eShopOnWeb application now includes recurring subscription billing through Maxio Advanced Billing. This is an **additive** capability alongside the existing cart/checkout flow. Users can browse subscription plans, subscribe to one, and view their active subscriptions.

## Configuration

### 1. Set Up User Secrets

The Maxio API credentials are configured via .NET User Secrets (not committed to the repository).

From the repository root, run the setup script:

```powershell
.\setup-maxio-secrets.ps1 -ApiKey <your-api-key> -Subdomain <your-subdomain> -ProductFamilyHandle <your-product-family-handle>
```

**Example for Maxio sandbox (cp-exp-2):**
```powershell
.\setup-maxio-secrets.ps1 -ApiKey your_maxio_api_key -Subdomain cp-exp-2 -ProductFamilyHandle eshop-subscribe
```

Optional: Override the base URL if using a non-standard Maxio endpoint:
```powershell
.\setup-maxio-secrets.ps1 -ApiKey <key> -Subdomain <subdomain> -ProductFamilyHandle <handle> -BaseUrl https://custom.maxio.com
```

### 2. Environment Configuration

Maxio settings are bound from the `Maxio:*` configuration section:
- `Maxio:ApiKey` - From `MAXIO_API_KEY` env var (via user-secrets)
- `Maxio:Subdomain` - From `MAXIO_SITE_SUBDOMAIN` env var (via user-secrets)
- `Maxio:ProductFamilyHandle` - From `MAXIO_DEFAULT_PRODUCT_FAMILY` env var (via user-secrets)
- `Maxio:BaseUrl` - (Optional) Override the computed Maxio API URL

All values are configured via user-secrets and NOT stored in appsettings.json.

### 3. Database Setup

**In-Memory Database:**
The application uses an in-memory database by default. Run with:
```bash
DOTNET_ROLL_FORWARD=Major dotnet run
```

Or from `src/PublicApi`:
```bash
dotnet run
```

**Note:** In-memory database loses all data on restart. For persistent testing, switch to SQL Server LocalDB if the runtime is available.

## API Endpoints

All subscription endpoints are under `/api/` and require JWT authentication via Bearer token.

### 1. Get Available Subscription Plans
```
GET /api/subscription-plans
```

**Request:**
- Authentication: Bearer token (JWT)

**Response:**
```json
{
  "correlationId": "00000000-0000-0000-0000-000000000000",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": null,
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": null,
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 2. Create a Subscription
```
POST /api/subscriptions
```

**Request:**
- Authentication: Bearer token (JWT)
- Content-Type: application/json

```json
{
  "correlationId": "00000000-0000-0000-0000-000000000000",
  "productHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "correlationId": "00000000-0000-0000-0000-000000000000",
  "subscription": {
    "id": 12345,
    "state": "active",
    "productHandle": "eshop-pro",
    "productName": "Pro Plan",
    "productPrice": 299.00,
    "currentPeriodEndsAt": "2026-10-06T12:34:56-05:00",
    "nextAssessmentAt": "2026-10-06T12:34:56-05:00",
    "activatedAt": "2026-09-06T12:34:56-05:00"
  }
}
```

**Behavior:**
- Creates a Maxio customer for the user (idempotent via email reference)
- Creates a subscription to the specified product
- Subscription starts immediately (no trial on these plans)
- No payment method required (configured on Maxio sandbox)

### 3. List User Subscriptions
```
GET /api/my-subscriptions
```

**Request:**
- Authentication: Bearer token (JWT)

**Response:**
```json
{
  "correlationId": "00000000-0000-0000-0000-000000000000",
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "productPrice": 299.00,
      "currentPeriodEndsAt": "2026-10-06T12:34:56-05:00",
      "nextAssessmentAt": "2026-10-06T12:34:56-05:00",
      "activatedAt": "2026-09-06T12:34:56-05:00"
    }
  ]
}
```

## Verification Steps

### Prerequisites
1. .NET 8.0+ SDK and ASP.NET Core 8.0 runtime (or use `DOTNET_ROLL_FORWARD=Major`)
2. Maxio sandbox credentials (API key, subdomain, product family handle)
3. User secrets configured (see Configuration section above)

### Running the Application

1. From the repository root, navigate to PublicApi:
```bash
cd src/PublicApi
```

2. Run the application:
```bash
DOTNET_ROLL_FORWARD=Major dotnet run
```

3. The PublicApi should start on `https://localhost:25003`

### Manual Testing with curl

#### 1. Authenticate and Get JWT Token

First, authenticate with the PublicApi to get a JWT bearer token:

```bash
curl -X POST https://localhost:25003/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@123"
  }' \
  -k
```

Look for the `token` field in the response. Use this value in subsequent requests as `<JWT_TOKEN>`.

#### 2. Get Subscription Plans

```bash
curl https://localhost:25003/api/subscription-plans \
  -H "Authorization: Bearer <JWT_TOKEN>" \
  -k
```

Expected response: Array of subscription plans (Pro Plan, Basic Plan, etc.)

#### 3. Create a Subscription

```bash
curl -X POST https://localhost:25003/api/subscriptions \
  -H "Authorization: Bearer <JWT_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  -k
```

Expected response: Subscription details including state ("active"), product name, and billing dates.

**Note:** This is idempotent. Calling it again with the same user email will likely create a new subscription (each call creates a new request to Maxio), but the user's Maxio customer will be reused.

#### 4. Get User's Subscriptions

```bash
curl https://localhost:25003/api/my-subscriptions \
  -H "Authorization: Bearer <JWT_TOKEN>" \
  -k
```

Expected response: Array containing the subscription(s) created in the previous step.

### Testing with Postman or API Client

1. **Authenticate:** POST to `https://localhost:25003/api/authenticate`
   - Body: `{"username":"demouser@microsoft.com","password":"Pass@123"}`
   - Copy the `token` value

2. **Set up authorization:** Add header `Authorization: Bearer <token>`

3. **Test GET /api/subscription-plans**

4. **Test POST /api/subscriptions** with body:
   ```json
   {"productHandle":"eshop-pro"}
   ```

5. **Test GET /api/my-subscriptions**

## Sandbox Plans (cp-exp-2)

The demo Maxio sandbox includes:

| Plan | Handle | Price | Interval |
|------|--------|-------|----------|
| Pro Plan | `eshop-pro` | $299/month | Monthly |
| Basic Plan | `basic-plan` | $29/month | Monthly |

- No trial period
- No setup fee
- Payment not required (sandbox config)
- Auto-renews indefinitely

## Architecture

### Service Layer
- **MaxioSubscriptionService** (`src/Infrastructure/Services/MaxioSubscriptionService.cs`)
  - Handles all Maxio API communication
  - Uses HTTP Basic authentication (API key + "X")
  - Manages customer creation and subscription lifecycle
  - Customer lookup by email reference for idempotency

### Interfaces
- **IMaxioSubscriptionService** (`src/ApplicationCore/Interfaces/IMaxioSubscriptionService.cs`)
  - Abstraction for subscription operations
  - DTOs for subscription plans and subscriptions

### Endpoints
- **GetSubscriptionPlansEndpoint** - Lists available plans
- **CreateSubscriptionEndpoint** - Creates a subscription for logged-in user
- **GetUserSubscriptionsEndpoint** - Lists user's active subscriptions

### Configuration
- **MaxioConfiguration** - Settings class for Maxio API details
- Bound from `Maxio:*` configuration section
- Values loaded from user-secrets at runtime

## Production Considerations

- **Secrets Management:** In production, use Azure Key Vault or equivalent
- **Error Handling:** The service logs warnings on failures; consider enhanced monitoring
- **Customer Lifecycle:** Customer email is used as the unique reference for idempotency
- **Payment Profiles:** The sandbox is configured for payment-not-required; production requires payment method integration
- **Subscription State:** Check subscription states for active/cancelled/failed-to-create scenarios
- **Rate Limiting:** Maxio API has rate limits; consider caching subscription plans
- **Audit Logging:** Consider logging all subscription changes for compliance

## Known Limitations

1. **In-Memory Database:** User-subscription mappings are not persisted across restarts
2. **No Payment Method UI:** UI for adding payment methods not implemented (Maxio sandbox doesn't require it)
3. **No Subscription Management UI:** Cancellation, pause, plan changes require Maxio API calls
4. **Customer Lookup:** Uses email reference; relies on correct email in JWT claims
5. **No Event Webhooks:** Maxio webhooks for subscription state changes not integrated

## Troubleshooting

### "Unauthorized" on subscription endpoints
- Verify JWT token is valid and includes email claim
- Check token is passed in `Authorization: Bearer <token>` header

### "Maxio API error" in logs
- Verify API key and subdomain are correct
- Verify product family handle exists in Maxio sandbox
- Check Maxio sandbox is accessible from this machine
- Verify user-secrets are set correctly

### Subscriptions not persisting after restart
- This is expected with in-memory database
- Switch to SQL Server for persistent storage

### "Customer not found" or duplicate subscriptions
- In-memory database doesn't persist customer lookups
- Each restart starts fresh; recreate subscriptions as needed

## Development Notes

- All Maxio API interactions use HTTP Basic auth: `Authorization: Basic <base64(apikey:X)>`
- API base URL: `https://{subdomain}.chargify.com`
- Endpoints are registered using MinimalApi.Endpoint pattern
- JWT claims used: `NameIdentifier` (user ID) and `Email` (for Maxio customer reference)

