# Maxio Subscription Billing Integration - Verification Guide

This guide provides step-by-step instructions to verify the Maxio subscription billing integration in eShopOnWeb.

## Prerequisites

- .NET 8 SDK installed (the project will roll forward from pinned 8.0.x)
- Maxio API credentials for the sandbox environment (site `cp-exp-2`)
- The ASP.NET Core 8.0 runtime (or use `DOTNET_ROLL_FORWARD=Major`)
- The development HTTPS certificate must be trusted (`dotnet dev-certs https --check`)

## Environment Setup

### 1. Set Environment Variables

```bash
# Linux/macOS
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"

# Windows (PowerShell)
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
```

### 2. Verify Credentials in User Secrets

User secrets are already configured during setup. To verify:

```bash
cd src/PublicApi
dotnet user-secrets list
```

Expected output should show Maxio:* keys with values loaded from environment variables.

## Running the Application

### Start the PublicApi Service

```bash
cd src/PublicApi
dotnet run
```

The service should start on `https://localhost:28143`.

### (Optional) Start the Web Service

In a separate terminal:

```bash
cd src/Web
dotnet run --launch-profile Web
```

The web app should be available at `https://localhost:5001`.

## Verification Steps

### Step 1: Get an Authentication Token

```bash
curl -X POST https://localhost:28143/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  --insecure
```

Expected response:
```json
{
  "result": true,
  "username": "demouser@microsoft.com",
  "token": "eyJhbGc...",
  "isLockedOut": false,
  "requiresTwoFactor": false
}
```

Save the `token` value for the next steps.

### Step 2: List Available Subscription Plans

```bash
curl -X GET https://localhost:28143/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN" \
  --insecure
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "Month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "Month"
    }
  ],
  "correlationId": "..."
}
```

### Step 3: Create a Subscription

```bash
curl -X POST https://localhost:28143/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

Expected response (on success):
```json
{
  "subscription": {
    "id": 123456,
    "state": "Active",
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "price": 299.00,
    "nextBillingDate": "2025-10-07T00:00:00+00:00",
    "activatedAt": "2025-09-07T12:34:56+00:00"
  },
  "correlationId": "..."
}
```

**Note:** HTTP 201 Created response indicates successful subscription creation.

### Step 4: List User's Subscriptions

```bash
curl -X GET https://localhost:28143/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  --insecure
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 123456,
      "state": "Active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "price": 299.00,
      "nextBillingDate": "2025-10-07T00:00:00+00:00",
      "activatedAt": "2025-09-07T12:34:56+00:00"
    }
  ],
  "correlationId": "..."
}
```

## Testing Edge Cases

### Test 1: Unauthorized Access

Attempt to call an endpoint without a token:

```bash
curl -X GET https://localhost:28143/api/subscription-plans \
  --insecure
```

Expected: HTTP 401 Unauthorized

### Test 2: Invalid Product Handle

Try to subscribe to a non-existent plan:

```bash
curl -X POST https://localhost:28143/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"non-existent-plan"}' \
  --insecure
```

Expected: HTTP 400 Bad Request with error details

### Test 3: Duplicate Customer Idempotence

Create a subscription twice with the same user:

```bash
# First subscription
curl -X POST https://localhost:28143/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure

# Second subscription (same user)
curl -X POST https://localhost:28143/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"basic-plan"}' \
  --insecure
```

Expected: Both succeed. The second request reuses the same Maxio customer record (idempotent lookup).

### Test 4: List Empty Subscriptions

For a user with no subscriptions:

```bash
curl -X GET https://localhost:28143/api/my-subscriptions \
  -H "Authorization: Bearer NEW_USER_TOKEN" \
  --insecure
```

Expected: HTTP 200 OK with empty subscriptions array

## Troubleshooting

### HTTPS Certificate Issues

If you get SSL certificate errors:

```bash
# Verify the dev cert is trusted
dotnet dev-certs https --check

# If not, clean and generate a new cert
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

### Maxio Connection Failures

If you get 503 errors:

1. Verify environment variables are set correctly
2. Check that `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN` are correct for sandbox
3. Verify network connectivity to `https://cp-exp-2.advanced-billing.chargify.com`
4. Check PublicApi logs for detailed error messages

### Missing Credentials

If you get InvalidOperationException about missing Maxio config:

1. Run `dotnet user-secrets list` in `src/PublicApi` to verify secrets are set
2. Re-run the user secrets setup:
   ```bash
   cd src/PublicApi
   dotnet user-secrets set "Maxio:ApiKey" "your-key"
   dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"
   dotnet user-secrets set "Maxio:Environment" "sandbox"
   dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
   ```

## Architecture Notes

### Endpoints

- **GET /api/subscription-plans**: List available subscription plans from Maxio
- **POST /api/subscriptions**: Create a subscription for the authenticated user
- **GET /api/my-subscriptions**: List subscriptions for the authenticated user

### Key Features

- **JWT Authentication**: All endpoints require Bearer token from `/api/authenticate`
- **Idempotent Customer Lookup**: Customers are created once per email and reused
- **Error Handling**: Comprehensive error handling with descriptive error messages
- **In-Memory Database**: For development/testing, uses in-memory EF Core provider

### Integration Points

- **PublicApi/Program.cs**: Maxio client DI registration
- **SubscriptionEndpoints/**: Endpoint implementations
- **SubscriptionEndpoints/SubscriptionCreation*.cs**: Request/response DTOs and supporting classes

## Next Steps

Once verified, consider:

1. **Database Persistence**: Replace in-memory database with SQL Server for production scenarios
2. **Subscription Synchronization**: Implement webhook handlers for Maxio subscription state changes
3. **Additional Features**: Trial periods, metered component usage tracking, plan upgrades/downgrades
4. **Testing**: Add integration tests using the provided test infrastructure in `tests/PublicApiIntegrationTests`
