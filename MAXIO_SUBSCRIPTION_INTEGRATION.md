# Maxio Subscription Integration for eShopOnWeb

This document describes the subscription billing integration with Maxio Advanced Billing for the eShopOnWeb reference application.

## Overview

The integration adds a parallel subscription capability to eShopOnWeb, separate from the existing one-time commerce flow (Catalog → Basket → Order). Users can now:

1. Browse available subscription plans
2. Subscribe to a plan
3. View their active subscriptions

## Architecture

### Components

- **PublicApi Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)
  - `ListSubscriptionPlansEndpoint` - GET `/api/subscription-plans` - List available plans
  - `CreateSubscriptionEndpoint` - POST `/api/subscriptions` - Subscribe to a plan
  - `ListCustomerSubscriptionsEndpoint` - GET `/api/my-subscriptions` - View user's subscriptions

- **Maxio Service** (`src/Infrastructure/Services/MaxioSubscriptionService.cs`)
  - Handles all Maxio API interactions
  - Manages customer/subscription lifecycle
  - Idempotent customer creation

- **Data Model** (`src/ApplicationCore/Entities/UserMaxioCustomer.cs`)
  - Tracks mapping between eShopOnWeb users and Maxio customers
  - Stored in database for persistence

### Configuration

Maxio credentials are loaded from environment variables with fallback to `appsettings.json`:

| Environment Variable | Config Key | Description |
|----------------------|-----------|-------------|
| `MAXIO_API_KEY` | `Maxio:ApiKey` | Maxio API key for sandbox |
| `MAXIO_SITE_SUBDOMAIN` | `Maxio:Subdomain` | Maxio site subdomain (e.g., `cp-exp-3`) |
| `MAXIO_DEFAULT_PRODUCT_FAMILY` | `Maxio:ProductFamilyHandle` | Product family handle (e.g., `eshop-subscribe`) |
| `MAXIO_BASE_URL` | `Maxio:BaseUrl` | Optional URL override (defaults to `https://{subdomain}.chargify.com`) |

## Setup

### 1. Configure Environment Variables

```bash
# Linux/Mac
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-3"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
export DOTNET_ROLL_FORWARD="Major"

# PowerShell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-3"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

### 2. Run the Application

```bash
cd src/PublicApi
dotnet run
```

The API runs on `https://localhost:28543`.

## Testing

### 1. Get Authentication Token

First, authenticate to get a JWT token:

```bash
curl -X POST https://localhost:5001/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  -k
```

Note: The Web application listens on `https://localhost:5001` for the auth endpoint.

### 2. List Available Plans

```bash
curl -X GET https://localhost:28543/api/subscription-plans \
  -H "Accept: application/json" \
  -k
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with API access",
      "price": 299.00,
      "priceFormatted": "$299.00",
      "intervalUnit": 1,
      "intervalUnitText": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic plan for small businesses",
      "price": 29.00,
      "priceFormatted": "$29.00",
      "intervalUnit": 1,
      "intervalUnitText": "month"
    }
  ]
}
```

### 3. Create a Subscription

Using the token from step 1:

```bash
curl -X POST https://localhost:28543/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer {TOKEN}" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  -k
```

Expected response (201 Created):
```json
{
  "subscription": {
    "id": 123456,
    "customerId": 789,
    "state": "active",
    "productHandle": "eshop-pro",
    "productName": "Pro Plan",
    "nextAssessmentAt": "2026-10-07T12:34:56Z",
    "createdAt": "2026-09-07T12:34:56Z",
    "updatedAt": "2026-09-07T12:34:56Z"
  }
}
```

### 4. List User's Subscriptions

Using the token from step 1:

```bash
curl -X GET https://localhost:28543/api/my-subscriptions \
  -H "Authorization: Bearer {TOKEN}" \
  -k
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 123456,
      "customerId": 789,
      "state": "active",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "nextAssessmentAt": "2026-10-07T12:34:56Z",
      "createdAt": "2026-09-07T12:34:56Z",
      "updatedAt": "2026-09-07T12:34:56Z"
    }
  ]
}
```

## Idempotency

The integration ensures idempotent operations:

- **Customer Creation**: When a user subscribes, the service checks if a Maxio customer already exists for that user. If it does, it reuses the existing customer; otherwise, it creates a new one.
- **Subscription Creation**: Each user can have multiple subscriptions, but subscribing to the same plan twice will create separate subscription records in Maxio.

## Error Handling

### Common Errors

- **401 Unauthorized**: Missing or invalid JWT token
- **400 Bad Request**: Missing required fields (e.g., `planHandle`)
- **500 Internal Server Error**: Maxio API communication failure - check credentials and network connectivity

Errors include descriptive messages in the response:

```json
{
  "error": "Failed to create subscription: [error details]"
}
```

## Database Schema

### UserMaxioCustomer Table

Tracks the mapping between eShopOnWeb users and Maxio customers:

```sql
CREATE TABLE [UserMaxioCustomers] (
    [Id] int NOT NULL IDENTITY,
    [ApplicationUserId] nvarchar(450) NOT NULL,
    [MaxioCustomerId] int NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NOT NULL,
    PRIMARY KEY ([Id]),
    UNIQUE ([ApplicationUserId])
);
```

## Sandbox Entities

The demo catalog is pre-seeded on the Maxio sandbox (`cp-exp-3`):

| Entity | Handle | ID | Notes |
|--------|--------|----|----|
| Product Family | `eshop-subscribe` | 3023074 | Container for plans |
| Pro Plan | `eshop-pro` | 7126957 | $299/month - default target |
| Basic Plan | `basic-plan` | 7126958 | $29/month - alternate option |
| Metered Component | `api-call` | 3057195 | $0.01/unit - included on family |

**Note**: Numeric IDs may change after Maxio re-seeds. Always reference by handle.

## Production Considerations

### Security

- **Credentials**: Never hardcode API keys. Always use environment variables or secure secret management (Azure Key Vault, AWS Secrets Manager).
- **HTTPS**: The integration uses HTTPS for all API communication. Ensure dev certificates are trusted.
- **JWT Validation**: The endpoints enforce JWT authentication. Tokens are validated using the configured secret key.

### Reliability

- **Retries**: The current implementation does not include retry logic. Production deployments should add exponential backoff for transient Maxio API failures.
- **Logging**: All major operations are logged via `ILogger<MaxioSubscriptionService>`. Monitor logs for API failures.
- **Customer Sync**: If a Maxio customer is deleted externally, the next subscription attempt will create a new customer. Consider adding periodic sync jobs for production use.

### Rate Limiting

Maxio API has rate limits. The current implementation does not include rate limiting logic. Production deployments should:
- Implement request queuing
- Add exponential backoff for 429 (Too Many Requests) responses
- Monitor rate limit headers

## Troubleshooting

### "Failed to create customer"

- Verify `MAXIO_API_KEY` is correct and has sufficient permissions
- Check that `MAXIO_SITE_SUBDOMAIN` matches the correct Maxio site
- Ensure network connectivity to Maxio API

### "Plan not found"

- Verify `MAXIO_DEFAULT_PRODUCT_FAMILY` matches the correct product family handle
- Check that the product family exists and is seeded in the Maxio sandbox

### "No subscriptions found"

- Ensure the user has authenticated (valid JWT token)
- Check that at least one subscription has been created for the user
- Verify the user's Maxio customer ID exists in the `UserMaxioCustomers` table

## API Endpoints Summary

### GET /api/subscription-plans
**Authentication**: None required
**Description**: Lists all available subscription plans from the configured product family
**Response**: JSON array of subscription plans with pricing and details

### POST /api/subscriptions
**Authentication**: JWT Bearer token required
**Description**: Subscribes the authenticated user to a plan
**Request Body**:
```json
{
  "planHandle": "eshop-pro"
}
```
**Response**: JSON object containing the created subscription details (201 Created)

### GET /api/my-subscriptions
**Authentication**: JWT Bearer token required
**Description**: Lists all subscriptions for the authenticated user
**Response**: JSON array of subscription objects with state and billing information

## Next Steps

1. Configure Maxio credentials as described in the Setup section
2. Run the PublicApi and test the endpoints as described in Testing
3. Integrate subscription plans into your storefront UI
4. Monitor logs and Maxio dashboard for production issues

## Support

For issues with the Maxio API integration, refer to the Maxio API documentation or the integration code in `src/Infrastructure/Services/MaxioSubscriptionService.cs` and `src/PublicApi/SubscriptionEndpoints/`.
