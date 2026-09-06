# Maxio Subscription Billing Integration

This document describes the Maxio Advanced Billing integration for eShopOnWeb that enables subscription-based billing capabilities.

## Overview

The subscription integration adds three new REST API endpoints to the PublicApi that allow:

1. **List Subscription Plans** - GET `/api/subscription-plans`
   - Returns all available subscription plans from the configured product family
   - No authentication required
   - Returns: List of plans with handle, name, price, and description

2. **Create Subscription** - POST `/api/subscriptions`
   - Subscribe an authenticated user to a plan
   - Requires JWT authentication (Bearer token)
   - Automatically creates a Maxio customer on first subscription (idempotent)
   - Returns: Subscription details including ID, state, and next billing date

3. **Get User Subscriptions** - GET `/api/my-subscriptions`
   - Returns all subscriptions for the authenticated user
   - Requires JWT authentication (Bearer token)
   - Returns: List of user's active subscriptions

## Architecture

### Components

- **MaxioClient** - HTTP client for Maxio API communication using Basic Auth
- **SubscriptionService** - Business logic orchestrating subscriptions and customer management
- **IUserMaxioCustomerMappingStore** - Maps eShopOnWeb users to Maxio customers (in-memory implementation)
- **Three Endpoints** - REST API endpoints with JWT authentication

### Configuration

Configuration is loaded from `appsettings.json` and/or .NET user-secrets:

```json
{
  "Maxio": {
    "ApiKey": "",         // From MAXIO_API_KEY env var or user-secrets
    "Subdomain": "",      // From MAXIO_SITE_SUBDOMAIN env var or user-secrets
    "ProductFamilyHandle": "", // From MAXIO_DEFAULT_PRODUCT_FAMILY env var or user-secrets
    "BaseUrl": ""         // Optional: Override API base URL
  }
}
```

## Setup Instructions

### 1. Configure Maxio Credentials

Set up user secrets with your Maxio sandbox credentials:

```bash
# Navigate to the PublicApi project
cd src/PublicApi

# Set the Maxio credentials
dotnet user-secrets set "Maxio:ApiKey" "your-maxio-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-maxio-subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "your-product-family-handle"

# Optional: Override the API base URL (if using a custom Maxio endpoint)
dotnet user-secrets set "Maxio:BaseUrl" "https://custom.maxio.com"
```

Or use environment variables:

```bash
export MAXIO_API_KEY="your-maxio-api-key"
export MAXIO_SITE_SUBDOMAIN="your-maxio-subdomain"
export MAXIO_DEFAULT_PRODUCT_FAMILY="your-product-family-handle"
```

### 2. Handle Environment Gotchas

**SDK/Runtime Mismatch:**
If you're on .NET 10 SDK but need .NET 8.0 runtime:

```bash
# Set environment variable to allow roll-forward
export DOTNET_ROLL_FORWARD=Major
```

Or install the ASP.NET Core 8.0 runtime.

**No LocalDB:**
If SQL Server LocalDB is not available, use the in-memory database:

```bash
export UseOnlyInMemoryDatabase=true
```

Note: In-memory database loses data on restart and ignores migrations.

**HTTPS Dev Certificate:**
Ensure the development certificate is trusted:

```bash
dotnet dev-certs https --check
dotnet dev-certs https --trust  # If not trusted
```

### 3. Run the PublicApi

```bash
cd src/PublicApi
dotnet run
```

The API will start at `https://localhost:25043` and automatically seed test data.

## Testing the Integration

### Step 1: Create a Test User

First, authenticate to get a JWT token. Use the test user or create one:

```bash
curl -X POST https://localhost:25043/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "user@example.com",
    "password": "Pass123!"
  }'
```

This returns a response with a JWT token. Store it as:

```bash
export TOKEN="<jwt-token-from-response>"
```

### Step 2: List Available Subscription Plans

```bash
curl https://localhost:25043/api/subscription-plans
```

Expected response:

```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "description": "eshop-subscribe - Pro Plan"
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "description": "eshop-subscribe - Basic Plan"
    }
  ],
  "correlationId": "..."
}
```

### Step 3: Create a Subscription

Subscribe the user to a plan:

```bash
curl -X POST https://localhost:25043/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

Expected response (201 Created):

```json
{
  "subscriptionId": 12345,
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "state": "active",
  "priceInCents": 29900,
  "nextBillingAt": "2026-10-06T...",
  "activatedAt": "2026-09-06T...",
  "correlationId": "..."
}
```

### Step 4: Get User Subscriptions

Retrieve all subscriptions for the authenticated user:

```bash
curl -H "Authorization: Bearer $TOKEN" \
  https://localhost:25043/api/my-subscriptions
```

Expected response:

```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "state": "active",
      "priceInCents": 29900,
      "nextBillingAt": "2026-10-06T...",
      "activatedAt": "2026-09-06T..."
    }
  ],
  "correlationId": "..."
}
```

### Step 5: Verify in Maxio

Log into your Maxio dashboard to verify:

1. A new customer was created with your email
2. The customer has the reference set to your eShopOnWeb user ID
3. The subscription is active with the correct plan
4. The next billing date is set correctly

## Error Handling

The API uses the global exception middleware to handle errors. Common error scenarios:

- **401 Unauthorized**: Missing or invalid JWT token
- **400 Bad Request**: Invalid subscription product handle
- **500 Internal Server Error**: Maxio API failure

Check the application logs for detailed error messages.

## API Security

- All authenticated endpoints require a valid JWT Bearer token
- JWT tokens expire after 7 days
- Maxio API credentials are loaded from user-secrets/environment variables only
- No credentials are ever committed to the repository

## Key Features

✅ **Idempotent Customer Creation** - Double-subscribing doesn't create duplicate customers
✅ **User Mapping** - Tracks the relationship between eShopOnWeb users and Maxio customers
✅ **Production-Grade** - Comprehensive error handling and logging
✅ **JWT Authentication** - Secure endpoint access for authenticated users
✅ **Flexible Configuration** - Support for custom Maxio endpoints

## Known Limitations

- In-memory customer mapping is lost on application restart
- No subscription cancellation endpoint yet
- No payment method management yet
- All subscriptions inherit default product family configuration

## Future Enhancements

Potential additions for future versions:

1. Cancel subscription endpoint
2. Update subscription endpoint (change plans)
3. Webhook handling for Maxio events
4. Persistent customer mapping (database store)
5. Payment method management UI
6. Subscription usage/metering
7. Invoice management

## Troubleshooting

### "No Maxio configuration found"

Ensure user-secrets are properly set:

```bash
dotnet user-secrets list --project src/PublicApi
```

### "Failed to authenticate with Maxio"

- Verify API key is correct
- Check subdomain matches your Maxio account
- Ensure Maxio API key has proper permissions

### "Customer creation failed"

- Check email is valid
- Verify no duplicate customer with same reference
- Check Maxio sandbox account is active

### "Subscription creation failed"

- Verify product handle exists in your product family
- Ensure plan is not archived
- Check for Maxio API rate limiting

## References

- Maxio API Documentation: https://developers.maxio.com/
- Maxio Help Center: https://docs.maxio.com/hc/en-us/
- eShopOnWeb Documentation: https://github.com/dotnet-architecture/eShopOnWeb
