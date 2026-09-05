# Maxio Subscription Billing Setup Guide

## Overview

The eShopOnWeb PublicApi now includes Maxio Advanced Billing integration for subscription management. This guide explains how to set up and test the feature.

## Prerequisites

- Maxio Advanced Billing sandbox account (site: `cp-exp-2`)
- Maxio API credentials (available from your Maxio account settings)
- Environment with .NET 8.0+ SDK

## Sandbox Entities

The following entities are pre-configured in the Maxio sandbox:

| Entity | Handle | ID | Details |
|--------|--------|-----|---------|
| Product Family | `eshop-subscribe` | 3023074 | Container for subscription plans |
| Pro Plan | `eshop-pro` | 7126957 | $299.00/mo |
| Basic Plan | `basic-plan` | 7126958 | $29.00/mo |
| Metered component | `api-call` | 3057195 | $0.01/unit |

## Configuration

### Step 1: Set Environment Variables

Set these environment variables on your system:

```bash
# Linux/macOS
export MAXIO_API_KEY="your-api-key-here"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export MAXIO_ENVIRONMENT="sandbox"

# Windows PowerShell
$env:MAXIO_API_KEY="your-api-key-here"
$env:MAXIO_SITE_SUBDOMAIN="cp-exp-2"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
$env:MAXIO_ENVIRONMENT="sandbox"
```

### Step 2: Build and Run

The application reads configuration from `appsettings.json` and environment variables:

```bash
cd src/PublicApi
dotnet run
```

**Important environment notes:**
- If LocalDB is not available, add `UseOnlyInMemoryDatabase=true` to use in-memory storage
- The app runs on HTTPS by default - ensure your dev certificate is trusted

## API Endpoints

All endpoints require JWT authentication. First, authenticate to get a token:

### Authenticate

```bash
curl -X POST https://localhost:24363/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }'
```

Response includes a `token` field - use this for subsequent requests.

### List Subscription Plans

```bash
curl https://localhost:24363/api/subscription-plans \
  -H "Authorization: Bearer <TOKEN>"
```

Response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "$299.00 Plan",
      "handle": "eshop-pro",
      "description": "Professional plan",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "$29.00 Plan",
      "handle": "basic-plan",
      "description": "Basic plan",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Create Subscription

```bash
curl -X POST https://localhost:24363/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <TOKEN>" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

Response:
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "productHandle": "eshop-pro",
    "customerId": 98765432,
    "currentPeriodEndsAt": "2024-10-06T14:30:00Z",
    "nextAssessmentAt": "2024-10-06T14:30:00Z",
    "activatedAt": "2024-09-06T14:30:00Z",
    "createdAt": "2024-09-06T14:30:00Z"
  }
}
```

### Get My Subscriptions

```bash
curl https://localhost:24363/api/my-subscriptions \
  -H "Authorization: Bearer <TOKEN>"
```

Response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "customerId": 98765432,
      "currentPeriodEndsAt": "2024-10-06T14:30:00Z",
      "nextAssessmentAt": "2024-10-06T14:30:00Z",
      "activatedAt": "2024-09-06T14:30:00Z",
      "createdAt": "2024-09-06T14:30:00Z"
    }
  ]
}
```

## Testing Flow

1. **Authenticate** - Get a JWT token for subsequent requests
2. **List Plans** - View available subscription plans
3. **Create Subscription** - Subscribe to a plan (creates customer and subscription in Maxio)
4. **Get Subscriptions** - Retrieve all subscriptions for the authenticated user
5. **Verify in Maxio** - Log into Maxio dashboard to confirm customer and subscription creation

## Integration Details

### Architecture

- **MaxioSettings**: Configuration container (ApiKey, Subdomain, ProductFamilyHandle, BaseUrl)
- **MaxioClient**: HTTP client for Maxio API communication with Basic auth
- **SubscriptionService**: Business logic for subscription operations
- **Endpoints**: Three public endpoints under `/api/subscription-*`

### Key Behaviors

- **Customer Management**: Users are mapped to Maxio customers via their userId as reference. The first API call creates the customer; subsequent calls find the existing customer.
- **Payment Method**: Subscriptions use `remittance` payment collection (no immediate payment required) to avoid card capture in the sandbox
- **Idempotency**: The customer lookup prevents duplicate customer creation on multiple subscribe attempts

### Error Handling

- All endpoints validate JWT tokens and return 401 Unauthorized if missing or invalid
- Invalid product handles return 400 Bad Request
- Maxio API errors are logged and returned as 400 Bad Request to the client

## Configuration Files

- `appsettings.json`: Contains empty Maxio configuration section; actual values come from environment variables
- `Program.cs`: Registers Maxio services and reads environment configuration

**Note**: API credentials are **never** stored in the repository. They must be provided via environment variables at runtime.

## Troubleshooting

### "Failed to load subscription plans"
- Verify `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN` are set correctly
- Confirm the product family handle matches entities in your Maxio sandbox
- Check that the Maxio API is accessible from your network

### "Unauthorized" on endpoints
- Ensure you're including the JWT Bearer token in the `Authorization` header
- Verify the token is valid by checking its expiration

### "Failed to create customer"
- Check that the user's email is set correctly in the authentication token claims
- Verify Maxio sandbox site is active and accessible

### In-memory database data loss
- If using `UseOnlyInMemoryDatabase=true`, all data is lost when the app restarts
- Use a real SQL Server for production-like persistence

## Next Steps

1. Install Maxio SDK (optional) for advanced features
2. Add metered component tracking for API usage billing
3. Implement subscription cancellation endpoints
4. Add webhook handlers for Maxio events
