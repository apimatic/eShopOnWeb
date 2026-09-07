# Maxio Subscription Billing Integration - Verification Guide

## Prerequisites

- .NET 8.0+ SDK installed (ASP.NET Core 8.0 runtime recommended)
  - Note: .NET 10 rollforward may have dependency conflicts with the Maxio SDK (Microsoft.Bcl.AsyncInterfaces version mismatch). Use .NET 8.0 SDK for optimal compatibility.
- eShopOnWeb solution cloned and built
- Maxio sandbox account with API credentials
- Maxio sandbox environment with seeded product family `eshop-subscribe` containing plans `eshop-pro` and `basic-plan`

## Setup Steps

### Step 1: Configure Maxio Credentials in User-Secrets

Set your Maxio sandbox API credentials using the .NET user-secrets manager. This keeps secrets out of the repository.

```powershell
cd src/PublicApi

# Set your Maxio API credentials
dotnet user-secrets set "Maxio:ApiKey" "your-api-key-here"
dotnet user-secrets set "Maxio:Subdomain" "your-sandbox-subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:Environment" "Us"

# Optionally set a custom base URL (leave empty to use default https://{subdomain}.chargify.com)
# dotnet user-secrets set "Maxio:BaseUrl" "https://custom-url.example.com"
```

### Step 2: Configure Database

The solution uses an in-memory database for development. No additional setup required.

### Step 3: Run the PublicApi Application

```powershell
cd src/PublicApi

# Set environment variables for .NET 10 compatibility
$env:DOTNET_ROLL_FORWARD = "Major"

# Run the application
dotnet run
```

The API will start on HTTPS (default: `https://localhost:28343`). Swagger documentation will be available at `/swagger/ui/index.html`.

## Testing the Integration

### Test 1: Authenticate and Get JWT Token

First, obtain a JWT bearer token for authenticated endpoints.

```bash
curl -X POST "https://localhost:28343/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  --insecure
```

Response will include a `token` field. Copy this value for use in subsequent requests.

```json
{
  "result": true,
  "token": "eyJhbGc...",
  "username": "demouser@microsoft.com",
  ...
}
```

### Test 2: Get Available Subscription Plans (Unauthenticated)

```bash
curl -X GET "https://localhost:28343/api/subscription-plans" \
  --insecure
```

Expected response:
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month",
      "description": "..."
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month",
      "description": "..."
    }
  ],
  "correlationId": "..."
}
```

### Test 3: Subscribe to a Plan (Authenticated)

Replace `{TOKEN}` with the JWT token from Test 1.

```bash
curl -X POST "https://localhost:28343/api/subscriptions" \
  -H "Authorization: Bearer {TOKEN}" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure
```

Expected response:
```json
{
  "subscription": {
    "id": 12345,
    "state": "active",
    "productHandle": "eshop-pro",
    "activatedAt": "2026-09-07T...",
    "nextBillingAt": "2026-10-07T...",
    "currentPeriodEndsAt": "2026-10-07T..."
  },
  "correlationId": "..."
}
```

### Test 4: List User Subscriptions (Authenticated)

Replace `{TOKEN}` with the JWT token from Test 1.

```bash
curl -X GET "https://localhost:28343/api/my-subscriptions" \
  -H "Authorization: Bearer {TOKEN}" \
  --insecure
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "productHandle": "eshop-pro",
      "activatedAt": "2026-09-07T...",
      "nextBillingAt": "2026-10-07T...",
      "currentPeriodEndsAt": "2026-10-07T..."
    }
  ],
  "correlationId": "..."
}
```

### Test 5: Verify Idempotency

Subscribe to the same plan again (Test 3). Maxio should return the existing subscription instead of creating a duplicate, due to the `reference` field in the create request.

Alternatively, check the Maxio dashboard for the created customer and subscription records.

## Troubleshooting

### 401 Unauthorized on Authenticated Endpoints

- Verify the JWT token is valid and not expired
- Ensure the `Authorization` header is formatted as `Bearer {token}`
- Check that the user account exists in the database

### 422 Unprocessable Entity on Subscription Creation

- Verify the plan handle is correct (e.g., `eshop-pro`, `basic-plan`)
- Check that the product family exists in Maxio sandbox
- Verify that the Maxio API credentials are correct

### Cannot Connect to Maxio API

- Verify the `Maxio:Subdomain` is set correctly
- Verify the `Maxio:ApiKey` is valid
- Check that the Maxio sandbox environment is accessible
- If using a custom `Maxio:BaseUrl`, verify the URL is correct

### 500 Internal Server Error

- Check the application logs for detailed error messages
- Verify all user-secrets are set correctly
- Ensure the in-memory database is properly initialized

## Implementation Details

### Architecture

The integration consists of:

1. **SubscriptionsService** (`src/PublicApi/SubscriptionsService.cs`)
   - Core service for interacting with Maxio API
   - Handles customer lookup/creation (idempotent)
   - Handles subscription creation and retrieval
   - Includes error handling and caching for plans

2. **Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)
   - `GetSubscriptionPlansEndpoint` - Lists available subscription plans
   - `CreateSubscriptionEndpoint` - Subscribes user to a plan
   - `GetMySubscriptionsEndpoint` - Lists user's active subscriptions

3. **Configuration**
   - `MaxioConfiguration` class binds settings from `Maxio:` section
   - Settings loaded from `appsettings.json` and user-secrets
   - Client registered via built-in `AddMaxioAdvancedBillingClient()` DI extension

### Error Handling

- **JsonException**: Malformed API responses logged and returned as 500
- **SdkException<CreateCustomerError>**: Validation errors from Maxio returned as 400
- **SdkException<CreateSubscriptionError>**: Subscription errors returned as 400
- **SdkException<RawError>**: Generic read/list operation failures returned as 500
- **Other exceptions**: Unhandled errors logged and returned as 500

### Caching

Subscription plans are cached in-memory for 1 hour. To refresh the cache, either wait for the TTL to expire or restart the application.

### Idempotency

- **Customer creation**: Uses the user's email as the `reference` field. Repeated calls with the same user will reuse the existing Maxio customer.
- **Subscription creation**: Uses a timestamp-based `reference` field. Each subscription request creates a unique reference, allowing multiple subscriptions per user.

## Known Limitations

1. **Product Family Filtering**: `ListProducts` fetches all products; filtering by family is done in-memory
2. **Subscription Filtering**: `ListSubscriptions` fetches all subscriptions; filtering by customer is done in-memory
3. **No Payment Collection**: Plans are configured without requiring payment methods
4. **No Coupon Support**: Coupon functionality not yet implemented
5. **No Component Metering**: Metered components not yet implemented

These limitations can be addressed in future iterations based on requirements.

## Next Steps

To extend the integration:

1. Add coupon/promo code support to subscription creation
2. Implement subscription cancellation endpoint
3. Add metered component usage tracking
4. Implement webhook handlers for Maxio events (subscription state changes, renewals, etc.)
5. Add persistent database storage for customer/subscription mappings
6. Implement subscription modification (plan changes, prorations)
