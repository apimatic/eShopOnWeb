# Maxio Subscription Billing Integration Guide

This guide explains how to set up, configure, and verify the Maxio subscription billing integration for eShopOnWeb.

## Overview

The eShopOnWeb PublicApi now includes three new REST endpoints for managing subscription billing via Maxio Advanced Billing:

- `GET /api/subscription-plans` - List available subscription plans
- `POST /api/subscriptions` - Create a new subscription for the authenticated user
- `GET /api/my-subscriptions` - List subscriptions for the authenticated user

## Prerequisites

- .NET 8 SDK
- Maxio sandbox credentials
- A running instance of the eShopOnWeb PublicApi

## Setup Instructions

### 1. Configure Maxio Credentials

The integration reads credentials from the following environment variables:

- `MAXIO_API_KEY` - Your Maxio API key
- `MAXIO_SITE_SUBDOMAIN` - Your Maxio site subdomain (e.g., "cp-exp-2")
- `MAXIO_ENVIRONMENT` - The environment (e.g., "US" for sandbox)
- `MAXIO_DEFAULT_PRODUCT_FAMILY` - The product family handle (e.g., "eshop-subscribe")

These are automatically loaded into .NET User Secrets for secure storage.

### 2. Set User Secrets (One-Time Setup)

From the `src/PublicApi` directory:

```bash
dotnet user-secrets set "Maxio:ApiKey" "<your_api_key>"
dotnet user-secrets set "Maxio:Subdomain" "<your_site_subdomain>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<your_product_family_handle>"
```

Alternatively, if using environment variables (e.g., CI/CD):

```bash
export MAXIO_API_KEY="<your_api_key>"
export MAXIO_SITE_SUBDOMAIN="<your_site_subdomain>"
export MAXIO_DEFAULT_PRODUCT_FAMILY="<your_product_family_handle>"
```

### 3. Run the Application

Set the in-memory database flag and run:

```bash
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_ENVIRONMENT=Development

dotnet run --project src/PublicApi/PublicApi.csproj
```

The application will start on `https://localhost:24763`.

## API Endpoints

### 1. List Subscription Plans

**Endpoint:** `GET /api/subscription-plans`

**Authentication:** Not required (public endpoint)

**Response:**
```json
{
  "plans": [
    {
      "id": 7130995,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "billingPeriod": "1 month(s)"
    },
    {
      "id": 7130996,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price": 29.00,
      "billingPeriod": "1 month(s)"
    }
  ]
}
```

### 2. Create Subscription

**Endpoint:** `POST /api/subscriptions`

**Authentication:** Required (JWT Bearer token)

**Request Body:**
```json
{
  "planHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "success": true,
  "message": "Subscription created successfully",
  "subscriptionId": 12345678,
  "state": "active",
  "productName": "Pro Plan",
  "nextBillingDate": "2026-10-05T10:54:07.000Z"
}
```

### 3. Get My Subscriptions

**Endpoint:** `GET /api/my-subscriptions`

**Authentication:** Required (JWT Bearer token)

**Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productName": "Pro Plan",
      "activatedAt": "2026-09-05T10:54:07.000Z",
      "nextBillingDate": "2026-10-05T10:54:07.000Z",
      "canceledAt": null
    }
  ]
}
```

## Testing the Integration

### Step 1: Authenticate

```bash
curl -X POST "https://localhost:24763/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  -k

# Store the returned token for subsequent requests
TOKEN=$(curl ... | jq -r '.token')
```

### Step 2: List Plans

```bash
curl -X GET "https://localhost:24763/api/subscription-plans" \
  -H "Content-Type: application/json" \
  -k
```

### Step 3: Create Subscription

```bash
curl -X POST "https://localhost:24763/api/subscriptions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"planHandle":"eshop-pro"}' \
  -k
```

### Step 4: List My Subscriptions

```bash
curl -X GET "https://localhost:24763/api/my-subscriptions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -k
```

## Technical Details

### Architecture

- **MaxioSubscriptionService** (`SubscriptionEndpoints/MaxioSubscriptionService.cs`) - Handles all Maxio API communication
- **Endpoints** - Three HTTP endpoints in `SubscriptionEndpoints/`
- **Configuration** - Settings loaded from `appsettings.json` and User Secrets/Environment Variables

### Authentication Flow

1. User authenticates via `/api/authenticate` to obtain a JWT token
2. Token is included in subsequent requests via the `Authorization: Bearer <token>` header
3. The token contains the user's identity, which is extracted server-side
4. A Maxio customer is created/looked up using the user's ID as the reference
5. Subscriptions are created/listed for that customer in Maxio

### Idempotency

The integration is idempotent at the customer level - if a subscription endpoint is called multiple times for the same user:

1. The first call creates a new Maxio customer (using the user ID as reference) and subscription
2. Subsequent calls look up the existing customer by reference and return their subscriptions
3. No duplicate customers or subscriptions are created

## Implementation Files

### New Files Created

- `src/PublicApi/SubscriptionEndpoints/MaxioSettings.cs` - Configuration class
- `src/PublicApi/SubscriptionEndpoints/MaxioSubscriptionService.cs` - Service layer
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - Plans listing endpoint
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - Subscription creation endpoint
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs` - Subscriptions listing endpoint

### Modified Files

- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/PublicApi/Program.cs` - Added Maxio service registration and HttpClient configuration

## Production Considerations

### Security

- ✓ API keys are never stored in source code (loaded from User Secrets/Environment Variables)
- ✓ All subscription endpoints require JWT authentication
- ✓ HTTPS is required for all API calls
- ✓ Credentials are encoded using HTTP Basic Auth for Maxio API calls

### Database

- Uses in-memory database for development
- For production, configure a real SQL Server or PostgreSQL database
- The integration only creates data in Maxio, not in the local database (optional enhancement: persist subscription references)

### Error Handling

- Comprehensive logging of Maxio API errors
- Proper HTTP status codes returned to clients
- User-friendly error messages in responses

### Rate Limiting

- No built-in rate limiting (implement at API gateway level if needed)
- Maxio API has rate limits - monitor logs for throttling

## Troubleshooting

### Issue: 401 Unauthorized

**Cause:** Invalid or missing JWT token

**Solution:** Ensure you've authenticated first and are including the token in the Authorization header

### Issue: 422 Unprocessable Entity from Maxio

**Cause:** Missing required subscription fields or invalid customer

**Solution:** Check logs for details. Ensure customer was created successfully before subscription creation.

### Issue: 500 Internal Server Error

**Cause:** Check application logs for exceptions

**Solution:** Verify Maxio configuration, API key, and network connectivity

## Future Enhancements

1. **Subscription Management** - Add endpoints to cancel, pause, or modify subscriptions
2. **Payment Profiles** - Integrate payment method management
3. **Webhooks** - Handle Maxio webhook events (subscription renewal, cancellation, etc.)
4. **Billing History** - Expose invoice and billing history endpoints
5. **Usage-Based Billing** - Implement metered component tracking
6. **Admin Dashboard** - Add subscription management UI to admin portal
