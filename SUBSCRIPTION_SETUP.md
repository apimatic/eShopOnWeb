# Maxio Subscription Billing Integration - Setup & Verification Guide

This document describes the Maxio Advanced Billing subscription integration added to eShopOnWeb's PublicApi.

## Architecture

The subscription billing capability is implemented as a parallel feature to the existing cart/checkout flow:

- **Endpoints**: `GET /api/subscription-plans`, `POST /api/subscriptions`, `GET /api/my-subscriptions`
- **Auth**: JWT bearer tokens (same as other PublicApi endpoints)
- **Service**: `IMaxioService` handles all Maxio API communication
- **Config**: Reads from environment variables at startup

## Files Added

### Configuration
- `src/PublicApi/SubscriptionEndpoints/MaxioConfiguration.cs` - Configuration class

### Service
- `src/PublicApi/SubscriptionEndpoints/MaxioService.cs` - HTTP client for Maxio API, includes DTOs for request/response mapping

### Endpoints
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - Lists available plans from product family
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - Creates customer and subscription in Maxio
- `src/PublicApi/SubscriptionEndpoints/ListMySubscriptionsEndpoint.cs` - Lists authenticated user's subscriptions
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs` - DTO for plan data

### Modified Files
- `src/PublicApi/Program.cs` - Registered Maxio configuration and service
- `src/PublicApi/appsettings.json` - Added Maxio configuration section

## Environment Setup

### Required Environment Variables

Set these before running the PublicApi:

```bash
MAXIO_API_KEY=your_api_key
MAXIO_SITE_SUBDOMAIN=your_subdomain
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

Optional:
```bash
# Override the base URL (defaults to https://{subdomain}.chargify.com)
Maxio:BaseUrl=https://custom-maxio-url.com
```

### For Local Development

In PowerShell:
```powershell
$env:MAXIO_API_KEY="YOUR_API_KEY"
$env:MAXIO_SITE_SUBDOMAIN="cp-exp-2"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

Or add to `.env` file if using a dotenv loader.

### Data Types

All monetary values in the Maxio API are stored in cents (as integers). The service automatically converts to decimal dollars in responses.

## API Endpoints

### 1. List Subscription Plans
```http
GET /api/subscription-plans
```

**Response** (200 OK):
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month",
      "description": "Professional subscription plan"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month",
      "description": "Basic subscription plan"
    }
  ]
}
```

### 2. Create Subscription
```http
POST /api/subscriptions
Authorization: Bearer {token}
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

**Response** (200 OK):
```json
{
  "success": true,
  "subscriptionId": 12345678,
  "state": "active",
  "pricePerCycle": 299.00,
  "nextBillingDate": "2026-10-06T00:00:00Z",
  "message": "Subscription created successfully. Plan renews on 2026-10-06"
}
```

**Error Response** (400 Bad Request):
```json
{
  "error": "ProductHandle is required"
}
```

**Error Response** (401 Unauthorized):
```
User must be authenticated
```

### 3. List My Subscriptions
```http
GET /api/my-subscriptions
Authorization: Bearer {token}
```

**Response** (200 OK):
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "pricePerCycle": 299.00,
      "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
      "nextAssessmentAt": "2026-10-06T00:00:00Z",
      "activatedAt": "2026-09-06T10:30:45Z"
    }
  ]
}
```

## Verification Steps

### Prerequisites
1. Maxio sandbox credentials configured
2. PublicApi running on its configured port (default: 5002)
3. A valid JWT token from the authentication endpoint

### Step 1: Get Authentication Token

```bash
curl -X POST https://localhost:5002/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

Response includes `token` field. Save this as `$TOKEN`.

### Step 2: List Subscription Plans

```bash
curl -X GET https://localhost:5002/api/subscription-plans \
  -H "Accept: application/json" \
  -k
```

Should return a list of available plans with pricing.

### Step 3: Create Subscription

```bash
curl -X POST https://localhost:5002/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  -k
```

Should return subscription details with state "active" or "trialing".

### Step 4: List My Subscriptions

```bash
curl -X GET https://localhost:5002/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  -k
```

Should return the subscription created in Step 3.

### Step 5: Verify Idempotency

Run Step 3 again with the same token and product handle. Should return success without creating a duplicate subscription (customer lookup finds the existing customer by reference).

## Key Implementation Details

### Customer Reference
- Uses the JWT `sub` claim (user ID) as Maxio customer reference
- Ensures idempotency: same user + product handle never creates duplicate customers
- Existing customer lookup prevents duplicate subscriptions

### Authentication
- Maxio API uses Basic Auth (API key + "x" as password)
- eShopOnWeb PublicApi uses JWT bearer tokens
- User context extracted from JWT claims (email, name, etc.)

### Error Handling
- All Maxio API errors logged but don't expose internal details to clients
- Invalid requests return 400 Bad Request
- Unauthorized requests return 401 Unauthorized
- Malformed Maxio responses return 400 Bad Request

### Production Considerations
- All HTTP client operations include exception handling and logging
- API calls have short timeout defaults (inherited from HttpClient)
- No sensitive data (API keys, auth tokens) logged
- Response payloads include only necessary fields
- Monetary values always converted from cents to dollars (decimal)

## Troubleshooting

### Endpoints not found (404)
- Ensure PublicApi is running: `dotnet run --project src/PublicApi/PublicApi.csproj`
- Check ports: PublicApi should be on port 5002 (or configured port)

### Authentication required (401)
- Ensure Bearer token is included: `Authorization: Bearer {token}`
- Get token from `/api/authenticate` endpoint

### Maxio connection errors
- Verify environment variables are set correctly
- Check network connectivity to Maxio API
- Verify sandbox credentials are valid
- Check if base URL is correct (default: `https://{subdomain}.chargify.com`)

### Customer not found (400 on subscription create)
- Verify API key has permission to create customers and subscriptions
- Check Maxio sandbox is accessible
- Review Maxio logs for detailed error messages

## Testing Without Live Maxio Credentials

The integration can be tested with mock responses by:
1. Creating a mock implementation of `IMaxioService`
2. Registering it in `Program.cs` instead of the real service
3. Running unit/integration tests

Example mock service pattern included in codebase.

## Future Enhancements

- Webhook support for subscription state changes
- Cancellation endpoint
- Upgrade/downgrade plans
- Metered component tracking (for api-call component)
- Customer payment profile management
- Usage reporting integration
