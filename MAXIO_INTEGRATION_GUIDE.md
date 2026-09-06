# Maxio Advanced Billing Integration for eShopOnWeb

## Overview

This integration adds recurring subscription billing to eShopOnWeb using Maxio Advanced Billing as the system of record. The subscription capability is **additive** to the existing one-time commerce flow and does not replace it.

## Architecture

### Components Added

1. **Application Layer** (`src/ApplicationCore/Interfaces/IMaxioSubscriptionService.cs`):
   - `IMaxioSubscriptionService` interface defining subscription operations
   - DTOs for `MaxioSubscriptionPlan`, `MaxioSubscription`

2. **Infrastructure Layer** (`src/Infrastructure/Services/`):
   - `MaxioSubscriptionService`: Core business logic for subscription management
   - `MaxioClientFactory`: SDK client initialization with configuration
   - DI registration in `Dependencies.cs`

3. **API Layer** (`src/PublicApi/SubscriptionEndpoints/`):
   - `ListSubscriptionPlansEndpoint`: GET `/api/subscription-plans`
   - `CreateSubscriptionEndpoint`: POST `/api/subscriptions`
   - `ListUserSubscriptionsEndpoint`: GET `/api/my-subscriptions`

### Configuration

Add the following to your appsettings or as environment variables:

```json
"Maxio": {
  "ApiKey": "YOUR_MAXIO_API_KEY",
  "Subdomain": "cp-exp-1",
  "ProductFamilyHandle": "eshop-subscribe",
  "BaseUrl": null
}
```

Or via environment variables:
- `MAXIO_API_KEY` - API key for Maxio sandbox
- `MAXIO_SITE_SUBDOMAIN` - Site subdomain (e.g., `cp-exp-1`)
- `MAXIO_ENVIRONMENT` - Environment (default: `us`)
- `MAXIO_DEFAULT_PRODUCT_FAMILY` - Product family handle

## Pre-Requisites

### Maxio Setup (Sandbox)

The following entities must exist on your Maxio sandbox site:

| Entity | Handle | Purpose |
|--------|--------|---------|
| Product Family | `eshop-subscribe` | Container for subscription plans |
| Pro Plan | `eshop-pro` | $299.00/mo - primary subscription |
| Basic Plan | `eshop-basic` | $29.00/mo - alternate option |
| Metered Component (opt) | `api-call` | $0.01/unit for usage tracking |

**Configuration**:
- No trial period
- No setup fee
- Payment method: NOT required (sandbox)
- Taxation: disabled
- Expiration: never

### Environment Setup

```bash
# .NET SDK
dotnet --version  # Should be 10.0.x

# Database: Use in-memory (no LocalDB needed)
export UseOnlyInMemoryDatabase=true

# Maxio Credentials (set before running)
export MAXIO_API_KEY="your_api_key_here"
export MAXIO_SITE_SUBDOMAIN="cp-exp-1"
export MAXIO_ENVIRONMENT="us"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

## Running the Integration

### 1. Build

```bash
cd repo
dotnet build eShopOnWeb.sln -c Release
```

### 2. Run PublicApi

```bash
cd src/PublicApi
dotnet run --configuration Release
```

Server will start on `https://localhost:25623`

### 3. Testing the Endpoints

#### Prerequisites

You'll need a JWT token. Get one by authenticating first:

```bash
# Authenticate
curl -X POST "https://localhost:25623/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' \
  --insecure

# Save the token from response.token
export TOKEN="eyJ0eXAiOiJKV1QiLCJhbGc..."
```

#### List Subscription Plans

```bash
curl -X GET "https://localhost:25623/api/subscription-plans" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": null,
      "price": 299.00
    },
    {
      "id": 7126958,
      "handle": "eshop-basic",
      "name": "Basic Plan",
      "description": null,
      "price": 29.00
    }
  ]
}
```

#### Create a Subscription

```bash
curl -X POST "https://localhost:25623/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

**Expected Response (HTTP 200):**
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "nextBillingAt": "2024-10-06T00:00:00",
    "balance": 0.00,
    "productHandle": "eshop-pro"
  }
}
```

#### Get User's Subscriptions

```bash
curl -X GET "https://localhost:25623/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "nextBillingAt": "2024-10-06T00:00:00",
      "balance": 0.00,
      "productHandle": "eshop-pro"
    }
  ]
}
```

## Idempotency Guarantees

### Customer Creation

- Each eShopOnWeb user is mapped to a Maxio customer via the user's ID (NameIdentifier claim)
- On first subscription, a customer is created automatically
- Subsequent calls for the same user reference return the existing customer (no duplicate creation)
- Email format: `{userId}@eshop.local`

### Subscription Creation

- Each subscription is tagged with a unique reference: `sub-{userId}-{productHandle}-{timestamp}`
- Duplicate creates for the same user+plan are detected and return the existing subscription
- If a 422 (conflict) is encountered, the service attempts to retrieve the existing subscription

## Error Handling

### API Response Codes

| Code | Meaning | Action |
|------|---------|--------|
| 200 | Success | Use the returned data |
| 400 | Bad Request | Check your request body (e.g., missing productHandle) |
| 401 | Unauthorized | Ensure JWT token is valid and not expired |
| 422 | Unprocessable Entity | Validation failed (e.g., invalid plan handle) |
| 500 | Server Error | Check logs; may indicate Maxio connectivity issue |

### Common Issues

**"Failed to retrieve subscription plans"**
- Check Maxio credentials in configuration
- Verify product family and plan handles exist in sandbox
- Confirm network connectivity to Maxio sandbox

**"User identification failed"**
- Ensure JWT token includes `NameIdentifier` claim (or `sub`/`user_id` fallback)
- Token may have expired; re-authenticate

**"Subscription creation failed: 422"**
- Plan handle may be invalid
- Customer already has an active subscription for that product
- Re-run the endpoint to retrieve existing subscription

## Implementation Details

### Service Workflow

1. **Customer Idempotency**
   - On subscription creation, check if customer exists via user reference
   - If 404, create new customer with minimal fields (name, email, reference)
   - If 422 (reference conflict), customer already exists—retrieve and use it

2. **Plan Retrieval**
   - Fetch product by handle (e.g., `eshop-pro`, `eshop-basic`)
   - Extract current pricing from `PriceInCents` field (divide by 100 for display)

3. **Subscription Creation**
   - Upsert customer (ensure exists)
   - Create subscription with customer reference + product handle
   - Return subscription state + next billing date

4. **Listing User Subscriptions**
   - Fetch customer by user reference
   - If not found (404), return empty array
   - List all subscriptions for that customer ID

### Database Notes

- **Subscriptions are NOT stored locally** — they live entirely in Maxio
- User ID → Maxio Customer ID mapping is derived on-demand from Maxio (no cache)
- In-memory database only stores transient session/request data
- Data persists only within a single application run; restart loses in-memory state

## Testing Checklist

- [ ] Build completes without errors
- [ ] PublicApi starts without exceptions
- [ ] Authentication endpoint returns JWT token
- [ ] GET `/api/subscription-plans` returns 200 with plan list
- [ ] POST `/api/subscriptions` with valid token + plan handle returns 200
- [ ] POST `/api/subscriptions` repeated with same token returns same subscription (idempotent)
- [ ] GET `/api/my-subscriptions` returns list including created subscription
- [ ] Unauthenticated requests to subscription endpoints return 401
- [ ] Invalid plan handle returns 422
- [ ] Maxio customer ID is stable across multiple API calls for the same user

## Next Steps

1. Wire subscription endpoints into the Web frontend (Blazor/React)
2. Add subscription management UI (view, cancel, upgrade plans)
3. Integrate billing event webhooks from Maxio (payment success, renewal, etc.)
4. Persist customer IDs locally for performance (cache with TTL)
5. Add subscription filtering/search to list endpoints

## Architecture Decisions

- **No Local Persistence**: Subscriptions are fetched on-demand from Maxio. This simplifies schema and avoids sync issues but requires Maxio connectivity for every read.
- **User ID as Reference**: Leverages JWT claims to uniquely identify users and creates idempotent customer records.
- **Minimal Customer Data**: Only name, email, reference are required for customer creation. Maxio fills in defaults for other fields.
- **HTTP Status Codes**: Following REST conventions; 422 for validation/conflict, 401 for auth, 500 for server errors.

## Support

For SDK-specific issues, consult the `maxio-plan.md` file, which contains exact SDK signatures and contract details.

For Maxio documentation: https://chargify.readme.io/ (note: web searches for Maxio docs are blocked; use the plan sheet instead)
