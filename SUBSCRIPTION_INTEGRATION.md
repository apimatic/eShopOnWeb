# Maxio Subscription Integration - eShopOnWeb

## Overview
This document describes the new Maxio subscription billing endpoints added to the eShopOnWeb PublicApi service.

## Architecture

### New Endpoints
Three new JWT-authenticated REST endpoints have been added under `/api/`:

1. **GET /api/subscription-plans** - List available subscription plans
   - No authentication required to view the endpoint
   - Returns available plans from the Maxio product family

2. **POST /api/subscriptions** - Create a subscription
   - Requires JWT authentication
   - Creates a Maxio customer (idempotent) and subscription
   - User ID from JWT claims is used as the customer reference

3. **GET /api/my-subscriptions** - List user's active subscriptions
   - Requires JWT authentication
   - Returns subscriptions for the authenticated user

### Project Structure
- **ApplicationCore/MaxioSettings.cs** - Configuration model
- **Infrastructure/Dependencies.cs** - Maxio client registration & HTTP factory setup
- **PublicApi/SubscriptionEndpoints/** - Endpoint implementations

### Configuration
Maxio settings are loaded from `appsettings.json` under the `Maxio` section:
```json
{
  "Maxio": {
    "Subdomain": "cp-exp-1",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": ""
  }
}
```

The API key is loaded from user secrets (stored securely, never committed):
```
Maxio:ApiKey = <from MAXIO_API_KEY env var>
```

## Setup & Testing

### Prerequisites
- .NET 8.0 runtime (or 10.0+ SDK with rollForward enabled)
- Maxio sandbox credentials configured as environment variables

### Environment Variables
```bash
export MAXIO_API_KEY=<your-api-key>
export MAXIO_SITE_SUBDOMAIN=cp-exp-1
export MAXIO_ENVIRONMENT=US
export MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### User Secrets Setup
The API key is stored in user secrets for the PublicApi project:
```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

### Building & Running
```bash
cd src/PublicApi

# Build
DOTNET_ROLL_FORWARD=Major dotnet build

# Run
DOTNET_ROLL_FORWARD=Major dotnet run
```

The PublicApi will start on `https://localhost:28663`

### Testing the Endpoints

#### 1. Authenticate to get a JWT token
```bash
curl -X POST https://localhost:28663/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser","password":"DemoPassword123!"}' \
  -k
```

Response:
```json
{
  "token": "eyJhbGc...",
  "result": true,
  ...
}
```

#### 2. List Subscription Plans
```bash
curl -X GET https://localhost:28663/api/subscription-plans \
  -k
```

Response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

#### 3. Create a Subscription
```bash
TOKEN="<token from authenticate endpoint>"

curl -X POST https://localhost:28663/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  -k
```

Response:
```json
{
  "subscription": {
    "id": 12345,
    "state": "active",
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "currentBillingAmountInCents": 29900,
    "currentPeriodStartsAt": "2026-09-07T00:00:00Z",
    "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
    "nextAssessmentAt": "2026-10-07T00:00:00Z",
    "activatedAt": "2026-09-07T10:23:45Z",
    "canceledAt": null
  }
}
```

#### 4. List User's Subscriptions
```bash
TOKEN="<token from authenticate endpoint>"

curl -X GET https://localhost:28663/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -k
```

Response:
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      ...
    }
  ]
}
```

## Maxio SDK Integration Details

### Authentication
- Uses HTTP Basic Auth (API key + "x" password)
- Credentials loaded from `Maxio:ApiKey` configuration
- Client is registered as a singleton via `AddMaxioAdvancedBillingClient`

### Operations Used
1. **ListProductsForProductFamily** - Retrieves plans from product family
2. **ReadCustomerByReference** - Idempotent customer lookup by user ID
3. **CreateCustomer** - Creates customer if not found
4. **CreateSubscription** - Enrolls customer to a plan
5. **ListCustomerSubscriptions** - Retrieves active subscriptions

### Error Handling
- HTTP 401/403 returns 401 Unauthorized
- HTTP 404 customer (when creating) triggers customer creation
- HTTP 422 validation errors return 422 Bad Request
- Other errors return 500 Internal Server Error
- Logs all errors for debugging

### Idempotency
- Customer creation is idempotent via `reference` field (using app user ID)
- Double subscription attempts use the same customer ID
- Safe for network retries and duplicate requests

## Production Considerations

### Security
- API keys are stored in user secrets, never committed to repository
- JWT authentication required for all subscription write/read operations
- HTTPS enforced in production
- Input validation on product handles

### Resilience
- HTTP client uses `IHttpClientFactory` for connection pooling
- Automatic retries on transient failures (503, 502, etc.)
- Timeouts configured per-request (30s default)
- Comprehensive error logging

### Database
- No local database required for subscriptions
- Maxio is the system of record
- User ID mapping to Maxio customer ID stored implicitly via customer `reference` field

## Future Enhancements

Potential additions:
- Subscription cancellation endpoint
- Subscription update/change plan endpoint
- Webhook handling for Maxio events
- Subscription status dashboard
- Metered component usage tracking (API calls)
