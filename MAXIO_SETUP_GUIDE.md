# Maxio Subscription Billing Integration Setup Guide

## Overview
This guide walks through setting up and testing the Maxio Advanced Billing subscription integration for eShopOnWeb.

## Prerequisites

- .NET SDK 8.0 or higher (rollForward is set to latestMajor to support .NET 10)
- HTTPS dev certificate installed: `dotnet dev-certs https --check`
- Access to Maxio sandbox site `cp-exp-3` with valid API credentials

## Step 1: Configure Credentials (User Secrets)

Set up Maxio credentials in user-secrets for the PublicApi project:

```powershell
cd src\PublicApi

# Set the Maxio API credentials
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-3"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: Override base URL if using non-standard Maxio instance
# dotnet user-secrets set "Maxio:BaseUrl" "https://custom.chargify.com"
```

**Important**: Never commit these secrets to version control. They are stored in the user-secrets store and loaded automatically during development.

## Step 2: Configure Database

For local development, use the in-memory database:

```powershell
$env:UseOnlyInMemoryDatabase = "true"
```

Or set it in your shell permanently in PowerShell profile.

**Note**: The in-memory database loses all data on restart. This is fine for testing/development.

## Step 3: Run the Application

From the solution root:

```powershell
# Ensure SDK rollForward is enabled
$env:DOTNET_ROLL_FORWARD = "Major"

# Run PublicApi
cd src\PublicApi
dotnet run
```

The API will start on:
- **HTTPS**: `https://localhost:25023`
- **API Base**: `https://localhost:25023/api/`

Swagger/OpenAPI documentation available at: `https://localhost:25023/swagger/ui`

## Step 4: Authenticate

All subscription endpoints require JWT authentication. First, get a token:

### Authenticate Request
```bash
POST https://localhost:25023/api/authenticate
Content-Type: application/json

{
  "username": "test@example.com",
  "password": "Pass@word1"
}
```

**Note**: Default test users are seeded in the database. Use the credentials from the database seed.

### Response
```json
{
  "result": true,
  "token": "eyJhbGc..."
}
```

Save the token for subsequent requests.

## Step 5: Test the Subscription Endpoints

### 1. List Available Plans

```bash
GET https://localhost:25023/api/subscription-plans
Authorization: Bearer {token}
Accept: application/json
```

**Expected Response** (200 OK):
```json
{
  "subscriptionPlans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": "$299.00",
      "description": "Professional plan with full features",
      "billingCycle": "per month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": "$29.00",
      "description": "Basic plan for individuals",
      "billingCycle": "per month"
    }
  ]
}
```

### 2. Create a Subscription

```bash
POST https://localhost:25023/api/subscriptions
Authorization: Bearer {token}
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

**Expected Response** (201 Created):
```json
{
  "subscriptionId": 12345678,
  "customerId": 98765432,
  "state": "active",
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "price": "$299.00",
  "nextBillingDate": "2025-10-06T12:30:00-05:00",
  "activatedAt": "2026-09-06T12:30:00-05:00",
  "createdAt": "2026-09-06T12:30:00-05:00"
}
```

### 3. Get Current User's Subscriptions

```bash
GET https://localhost:25023/api/my-subscriptions
Authorization: Bearer {token}
Accept: application/json
```

**Expected Response** (200 OK):
```json
{
  "customerId": 98765432,
  "customerName": "John Doe",
  "email": "john@example.com",
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "price": "$299.00",
      "nextBillingDate": "2025-10-06T12:30:00-05:00",
      "activatedAt": "2026-09-06T12:30:00-05:00",
      "createdAt": "2026-09-06T12:30:00-05:00"
    }
  ]
}
```

## Testing Idempotency

The integration ensures subscriptions are created idempotently:

1. Call `POST /api/subscriptions` with the same token/user ID twice
2. Both calls should succeed
3. The second call should return the same subscription details (no duplicate created)
4. Call `GET /api/my-subscriptions` - should only show one subscription for the user

This is guaranteed because:
- Customer creation uses the user ID as the Maxio `reference` field
- Maxio's customer lookup by reference ensures uniqueness
- If customer already exists, no duplicate is created

## Troubleshooting

### "ApiKey and Subdomain must be provided"
- Verify user-secrets are set: `dotnet user-secrets list`
- Ensure ASPNETCORE_ENVIRONMENT is correct (Development for user-secrets)

### "401 Unauthorized" from Maxio API
- Verify API key is valid and not expired
- Check Maxio site `cp-exp-3` is accessible
- Verify IP whitelist allows your machine

### "404 Product not found"
- Verify product handles exist in Maxio site `cp-exp-3`
- Expected handles: `eshop-pro`, `basic-plan`
- Verify `Maxio:ProductFamilyHandle` is set to `eshop-subscribe`

### "Unauthorized" on subscription endpoints
- Verify JWT token is valid and not expired
- Ensure Authorization header format is `Bearer {token}`
- Check user exists in database and has NameIdentifier claim

## Architecture

### Key Classes and Interfaces

- **`IMaxioApiService`** (ApplicationCore/Interfaces) - Main service interface for all Maxio operations
- **`MaxioApiService`** (ApplicationCore/Services) - HTTP client implementation using Maxio OpenAPI spec
- **`MaxioConfiguration`** (ApplicationCore/Constants) - Configuration object bound from settings
- **Subscription Endpoints** (PublicApi/SubscriptionEndpoints)
  - `ListSubscriptionPlansEndpoint` - GET /api/subscription-plans
  - `CreateSubscriptionEndpoint` - POST /api/subscriptions
  - `MySubscriptionsEndpoint` - GET /api/my-subscriptions

### Configuration Binding

Settings are bound from configuration sources (appsettings.json + user-secrets):

```csharp
builder.Services.Configure<MaxioConfiguration>(maxioConfigSection);
builder.Services.AddHttpClient<IMaxioApiService, MaxioApiService>();
```

## Implementation Details

### Maxio API Contract
All API calls follow the Maxio OpenAPI specification (located in `maxio-spec/openapi.yaml`):
- Customer creation: POST /customers.json
- Customer lookup by reference: GET /customers/lookup.json?reference={ref}
- Subscription creation: POST /subscriptions.json
- Customer subscriptions: GET /customers/{id}/subscriptions.json
- Product listing: GET /products.json

### Authentication to Maxio
Uses HTTP Basic Auth with API Key:
- Username: API Key
- Password: "x" (literal)

### Error Handling
- All endpoints return descriptive error messages in response body
- HTTP status codes follow REST conventions
- Maxio API errors are logged and forwarded to client

## Security Considerations

1. **Secrets Management**: API credentials are never stored in code or configuration files, only in user-secrets during development
2. **JWT Authentication**: All subscription endpoints verify JWT token and extract user identity from claims
3. **Customer Isolation**: Subscriptions are tied to the authenticated user's ID
4. **HTTPS Only**: All endpoints require HTTPS (configured in launchSettings.json)
5. **No Credit Card Capture**: Plans configured with `payment_collection_method: remittance` - no payment required at signup

## Next Steps

1. Test the full flow with actual Maxio credentials
2. Integrate with frontend to expose subscription UI
3. Add webhook handling for subscription lifecycle events
4. Implement subscription management endpoints (cancel, upgrade, etc.)
5. Add database persistence for subscription mappings (currently in-memory only)
