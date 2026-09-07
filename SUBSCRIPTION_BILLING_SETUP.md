# Maxio Subscription Billing Integration Guide

This guide explains the subscription billing feature added to eShopOnWeb using Maxio Advanced Billing.

## Architecture Overview

The subscription billing system is an additive capability to eShopOnWeb's existing one-time commerce flow. It includes:

- **Maxio Client** (`src/PublicApi/Maxio/`): HTTP client for communicating with Maxio API
- **Configuration** (`MaxioConfiguration.cs`): Settings management for Maxio credentials
- **Endpoints** (`src/PublicApi/SubscriptionEndpoints/`):
  - `ListSubscriptionPlansEndpoint`: Browse available subscription plans
  - `CreateSubscriptionEndpoint`: Subscribe to a plan
  - `ListMySubscriptionsEndpoint`: View your active subscriptions

## Configuration

### Environment Variables Required

Set these environment variables before running (or use user-secrets during development):

```bash
MAXIO_API_KEY=<your-api-key>
MAXIO_SITE_SUBDOMAIN=<sandbox-subdomain>
MAXIO_ENVIRONMENT=sandbox
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### User-Secrets for Development

Secrets have been pre-configured with placeholder values. Update them:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "<your-sandbox-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "<your-sandbox-subdomain>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:BaseUrl" ""  # Leave empty to use https://{subdomain}.chargify.com
```

## Running the Application

### Prerequisites

1. **Resolve SDK/Runtime Mismatch**: 
   - Option A - Use rollForward:
     ```bash
     dotnet build --no-restore
     DOTNET_ROLL_FORWARD=Major dotnet run --project src/PublicApi/PublicApi.csproj
     ```
   - Option B - Install ASP.NET Core 8.0 runtime (x64)

2. **Database**: The configuration uses in-memory database by default (set via `UseOnlyInMemoryDatabase=true`)

3. **HTTPS**: Ensure dev certificate is trusted:
   ```bash
   dotnet dev-certs https --check
   ```

### Start the API

```bash
cd repo
DOTNET_ROLL_FORWARD=Major dotnet run --project src/PublicApi/PublicApi.csproj
```

The PublicApi will start on ports defined in `launchSettings.json` (typically `https://localhost:28223`).

## API Endpoints

All endpoints require JWT Bearer token authentication. First authenticate using the existing `/api/authenticate` endpoint.

### 1. List Subscription Plans

**Endpoint**: `GET /api/subscription-plans`

**Request**:
```bash
curl -H "Authorization: Bearer <token>" \
  https://localhost:28223/api/subscription-plans
```

**Response**:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "description": "Professional plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month",
      "handle": "eshop-pro",
      "requiresPaymentMethod": false
    }
  ],
  "success": true,
  "message": ""
}
```

### 2. Create a Subscription

**Endpoint**: `POST /api/subscriptions`

**Request**:
```bash
curl -X POST \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}' \
  https://localhost:28223/api/subscriptions
```

**Response**:
```json
{
  "success": true,
  "message": "",
  "subscriptionId": 12345,
  "state": "active",
  "currentPeriodEndsAt": "2026-10-07T00:00:00",
  "product": {
    "id": 7126957,
    "name": "Pro Plan",
    "priceInCents": 29900,
    "interval": 1,
    "intervalUnit": "month"
  }
}
```

**Behavior**:
- Creates a Maxio customer if the user doesn't exist (using userId as reference)
- Subscribes the customer to the selected plan
- Returns subscription details including state and next billing date
- Idempotent: calling twice with same user doesn't create duplicate subscriptions (looks up existing customer)

### 3. List My Subscriptions

**Endpoint**: `GET /api/my-subscriptions`

**Request**:
```bash
curl -H "Authorization: Bearer <token>" \
  https://localhost:28223/api/my-subscriptions
```

**Response**:
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "customerId": 54321,
      "state": "active",
      "activatedAt": "2026-09-07T00:00:00",
      "currentPeriodEndsAt": "2026-10-07T00:00:00",
      "plan": {
        "id": 7126957,
        "name": "Pro Plan",
        "priceInCents": 29900,
        "interval": 1,
        "intervalUnit": "month"
      },
      "createdAt": "2026-09-07T00:00:00",
      "updatedAt": "2026-09-07T00:00:00"
    }
  ],
  "success": true,
  "message": ""
}
```

## How to Test the Integration

### Step 1: Authenticate

First, get a JWT token from the existing auth endpoint:

```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -d '{"username": "demouser@microsoft.com", "password": "Pass123$"}' \
  https://localhost:5001/api/authenticate
```

Copy the `token` value from the response.

### Step 2: List Available Plans

```bash
curl -H "Authorization: Bearer <your-token>" \
  https://localhost:28223/api/subscription-plans
```

This should show available subscription plans from your Maxio sandbox account.

### Step 3: Subscribe to a Plan

```bash
curl -X POST \
  -H "Authorization: Bearer <your-token>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}' \
  https://localhost:28223/api/subscriptions
```

This should create a subscription for the logged-in user. Check the Maxio dashboard to verify the customer and subscription were created.

### Step 4: List Your Subscriptions

```bash
curl -H "Authorization: Bearer <your-token>" \
  https://localhost:28223/api/my-subscriptions
```

This should show your active subscription from Step 3.

## Key Design Decisions

### Authentication
- Uses JWT Bearer tokens for API authentication
- Extracts user ID (NameIdentifier claim) from JWT to link subscriptions to Maxio customers
- Falls back to email from claims if available

### Customer Management (Idempotent)
- Customers are identified by their eShopOnWeb userId (stored as Maxio customer reference)
- On first subscription attempt, creates a Maxio customer if it doesn't exist
- Subsequent subscriptions for same user reuse existing customer (no duplicates)

### Error Handling
- All endpoints return structured responses with success flag and message
- HTTP status codes: 200 (OK), 201 (Created), 400 (BadRequest), 401 (Unauthorized)
- API errors from Maxio are logged and returned to client with descriptive messages

### Configuration
- All Maxio settings loaded from `Maxio:*` configuration section
- Supports optional override of base URL for testing against different environments
- Uses Basic Auth with API key against Maxio API (key:x format)

## Sandbox Testing Details

### Pre-Configured Plans (on site `cp-exp-3`)
- **Pro Plan** (`eshop-pro`): $299.00/month
- **Basic Plan** (`basic-plan`): $29.00/month
- **Product Family**: `eshop-subscribe`

### Important Notes
- Plans are configured with **payment method not required** (payment_method_required=false)
- This allows subscription without credit card (testing-friendly)
- Subscriptions have no trial period and never expire
- IDs are reassigned on re-seed, only handles are stable

## Troubleshooting

### "Maxio configuration is missing"
- Ensure environment variables or user-secrets are set
- Check: `dotnet user-secrets list` in PublicApi directory

### "Failed to create customer in billing system"
- Verify API key and subdomain are correct
- Check Maxio sandbox site is accessible
- Look for auth errors in application logs

### "Failed to retrieve subscription plans"
- Confirm ProductFamilyHandle matches your Maxio product family
- Check that plans exist in the product family
- Verify API credentials have permission to read products

### JWT Authentication Failed
- Ensure you're using the token from step 1 (from `/api/authenticate`)
- Token must be passed in `Authorization: Bearer <token>` header
- Token is valid for limited time, refresh if needed

## Production Considerations

When deploying to production:

1. **Secrets Management**: Use secure secret storage (Azure Key Vault, etc.), never commit secrets
2. **HTTPS**: Ensure all Maxio API calls use HTTPS
3. **Error Logging**: Implement detailed logging for all Maxio API interactions
4. **Rate Limiting**: Implement rate limiting for subscription creation endpoint
5. **Customer Verification**: Add email verification before enabling subscriptions
6. **Payment Methods**: For real subscriptions, require valid payment method configuration
7. **Webhooks**: Implement Maxio webhooks for subscription state changes
8. **Auditing**: Log all subscription creation/modification for compliance

## API Implementation Details

### Maxio Client Initialization
- Configured with HttpClient for dependency injection
- Uses Basic Auth: `Authorization: Basic base64(key:x)`
- Accepts JSON responses, serializes using System.Text.Json

### Endpoint Pattern
- Implements `IEndpoint<IResult, TRequest>` from MinimalApi.Endpoint library
- Uses MapGet/MapPost minimal APIs with Authorize attribute
- User context injected via HttpContext claims during request handling
