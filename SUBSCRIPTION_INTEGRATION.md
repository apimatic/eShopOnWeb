# Maxio Subscription Billing Integration - eShopOnWeb

This document describes the Maxio Advanced Billing subscription feature added to eShopOnWeb.

## Overview

The integration adds recurring subscription billing to eShopOnWeb using Maxio Advanced Billing as the billing system of record. This is an **additive capability** alongside the existing one-time commerce flow.

### Key Features

1. **List Available Plans** - GET `/api/subscription-plans`
2. **Subscribe to a Plan** - POST `/api/subscriptions`
3. **View User Subscriptions** - GET `/api/my-subscriptions`

## Architecture

### Components

- **MaxioSubscriptionService** (`PublicApi/SubscriptionEndpoints/MaxioSubscriptionService.cs`)
  - Handles all Maxio API interactions
  - Manages customer creation/lookup
  - Manages subscription operations

- **Endpoints**
  - `ListSubscriptionPlansEndpoint` - Fetches available plans by handle
  - `CreateSubscriptionEndpoint` - Enrolls user in a plan
  - `GetUserSubscriptionsEndpoint` - Lists user's subscriptions

- **DTOs**
  - `SubscriptionPlanDto` - Plan information
  - `SubscriptionDto` - Subscription details
  - Request/response models

### Configuration

Configuration is loaded from the `Maxio:` section in `appsettings.json`:

```json
{
  "Maxio": {
    "ApiKey": "",           // Loaded from user-secrets or env var MAXIO_API_KEY
    "Subdomain": "",        // Loaded from user-secrets or env var MAXIO_SITE_SUBDOMAIN
    "ProductFamilyHandle": "", // From env var MAXIO_DEFAULT_PRODUCT_FAMILY
    "BaseUrl": ""           // Optional override for API base URL
  }
}
```

Credentials are stored securely in user-secrets:

```bash
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "<your-subdomain>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<your-product-family>"
```

## Idempotency & Correctness

- **Customer Creation**: Uses the app's user ID as the Maxio `Reference` field for idempotent customer lookup. A second call for the same user returns the existing customer ID.
- **Subscription Creation**: Fails appropriately if the user is already subscribed to the same plan.
- **Database**: Uses in-memory database (by default); subscription mappings are lost on restart.

## Testing Guide

### Prerequisites

1. Maxio sandbox credentials configured via user-secrets
2. PublicApi running on `https://localhost:25303`
3. Active user account in the app (use the existing authentication)

### Step 1: Authenticate

```bash
# Get authentication token
curl -X POST https://localhost:25303/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"user@example.com","password":"Pass@word1"}'
```

Response:
```json
{
  "token": "eyJhbGc...",
  "result": true,
  "isLockedOut": false,
  ...
}
```

Save the `token` value for the next steps.

### Step 2: List Available Plans

```bash
curl -X GET https://localhost:25303/api/subscription-plans \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json"
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan",
      "price": 299.00,
      "intervalDays": 30
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic plan",
      "price": 29.00,
      "intervalDays": 30
    }
  ]
}
```

### Step 3: Subscribe to a Plan

```bash
curl -X POST https://localhost:25303/api/subscriptions \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

Expected response:
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "balanceInCents": 0,
    "productPriceInCents": 29900,
    "currentPeriodEndsAt": "2026-10-06T...",
    "nextAssessmentAt": "2026-10-06T...",
    "productHandle": "eshop-pro",
    "productName": "Pro Plan",
    "createdAt": "2026-09-06T..."
  }
}
```

### Step 4: View User Subscriptions

```bash
curl -X GET https://localhost:25303/api/my-subscriptions \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json"
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "balanceInCents": 0,
      "productPriceInCents": 29900,
      "currentPeriodEndsAt": "2026-10-06T...",
      "nextAssessmentAt": "2026-10-06T...",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "createdAt": "2026-09-06T..."
    }
  ]
}
```

## Error Handling

The integration handles several error scenarios:

- **404 on plan lookup** - Plan not found in Maxio
- **Duplicate customer creation** - Customer already exists (idempotent, returns existing ID)
- **Missing payment method** - Subscription requires a payment method (not configured)
- **Invalid subscription state** - User already has an active subscription for that plan

All errors return appropriate HTTP status codes and error messages.

## Production Considerations

1. **Persistent Storage**: Store the Maxio customer ID in persistent storage (database, Redis, etc.) for production use
2. **Payment Methods**: Implement payment method collection (card capture, Maxio-hosted forms, etc.)
3. **Webhook Handling**: Implement Maxio webhooks for subscription state changes (cancellation, renewal, etc.)
4. **Audit Logging**: Log all subscription operations for compliance
5. **Rate Limiting**: Implement rate limiting on subscription endpoints
6. **Error Monitoring**: Set up proper error tracking and alerting

## Known Limitations

- **In-Memory Database**: Subscription mappings are lost on app restart
- **No Payment Collection**: Payment methods must be set up separately before subscription creation
- **No Webhook Integration**: Real-time subscription events are not processed
- **Single Product Family**: Currently configured for a single product family (can be extended)

## Cleanup

To reset for testing:

1. Delete user-secrets (subscription state will be forgotten)
2. Restart the app (in-memory database clears)
3. Authenticate again and re-test

Note: Subscriptions in Maxio will persist; customer IDs will need manual cleanup.
