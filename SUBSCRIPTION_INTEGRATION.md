# Maxio Subscription Billing Integration

This document describes the Maxio Advanced Billing integration added to eShopOnWeb, enabling recurring subscription capabilities alongside the existing one-time commerce flow.

## Architecture Overview

The integration follows a clean architecture pattern:

- **Domain Entities** (`ApplicationCore/Entities/SubscriptionAggregate/`):
  - `Subscription` - tracks user subscriptions linked to Maxio
  - `UserSubscriptionMapping` - idempotent mapping of eShopWeb users to Maxio customers

- **Infrastructure Services** (`Infrastructure/Services/`):
  - `IMaxioService` / `MaxioService` - HTTP client abstraction over the Maxio API
  - `MaxioSettings` - configuration binding for Maxio credentials

- **PublicApi Endpoints** (`PublicApi/SubscriptionEndpoints/`):
  - `ListSubscriptionPlansEndpoint` - GET /api/subscription-plans
  - `CreateSubscriptionEndpoint` - POST /api/subscriptions
  - `GetMySubscriptionsEndpoint` - GET /api/my-subscriptions

All endpoints require JWT bearer token authentication. The endpoints extract the user ID from the token's `NameIdentifier` claim.

## Configuration

The integration reads Maxio credentials from environment variables and binds them to the configuration section `Maxio:` with these keys:

| Key | Env Var | Required | Notes |
|-----|---------|----------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Yes | Maxio API key for sandbox |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | No | Sandbox site subdomain (default: `cp-exp-3`) |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | No | Product family handle to filter plans (default: `eshop-subscribe`) |
| `Maxio:BaseUrl` | `MAXIO_BASE_URL` | No | Override base URL (normally derived from subdomain) |

### Setting Up Credentials

Never commit credential values to the repository. Instead:

1. Set environment variables on your system:
   ```bash
   export MAXIO_API_KEY="your_sandbox_api_key"
   export MAXIO_SITE_SUBDOMAIN="cp-exp-3"
   ```

2. The application loads these on startup and injects them into the configuration

3. The `MaxioService` authenticates API calls using HTTP Basic Auth: `Base64(api_key:x)`

## Maxio Sandbox Setup

The demo catalog is pre-seeded on `cp-exp-3` (Maxio sandbox) with:

| Entity | Handle | Current ID | Notes |
|--------|--------|-----------|-------|
| Product Family | `eshop-subscribe` | 3023074 | Container for plans and metered component |
| Pro Plan | `eshop-pro` | 7126957 | $299.00/mo (primary subscription target) |
| Basic Plan | `basic-plan` | 7126958 | $29.00/mo (alternate plan) |
| Metered Component | `api-call` | 3057195 | $0.01 per unit (optional usage tracking) |

**Note:** Numeric IDs change on re-seed; use handles as the authoritative reference.

Both plans are configured with:
- No trial period
- No setup fee
- Never expires
- Not taxable
- Payment method not required (enables subscription without card)

## Testing the Integration

### Prerequisites

1. **Environment variables set** (see Configuration above)
2. **PublicApi running** on `https://localhost:25423`
3. **In-memory database** configured (`UseOnlyInMemoryDatabase=true`)

### Step 1: Get a JWT Token

The PublicApi has an authenticate endpoint that returns a bearer token:

```bash
# Register/authenticate a user (check PublicApi for the exact endpoint)
curl -X POST https://localhost:25423/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"email": "test@example.com", "password": "password"}' \
  -k  # Ignore self-signed certificate for development
```

Save the returned `token` value.

### Step 2: List Available Subscription Plans

```bash
curl -X GET https://localhost:25423/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

**Expected response** (200 OK):
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "$299 Pro Plan",
      "handle": "eshop-pro",
      "description": "Pro subscription with advanced features",
      "pricePerMonth": 299.00,
      "billingInterval": "1 month"
    },
    {
      "id": 7126958,
      "name": "$29 Basic Plan",
      "handle": "basic-plan",
      "description": "Basic subscription",
      "pricePerMonth": 29.00,
      "billingInterval": "1 month"
    }
  ],
  "correlationId": "uuid"
}
```

### Step 3: Create a Subscription

```bash
curl -X POST https://localhost:25423/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}' \
  -k
```

**Expected response** (201 Created):
```json
{
  "subscription": {
    "id": 99999999,
    "productName": "$299 Pro Plan",
    "productHandle": "eshop-pro",
    "price": 299.00,
    "state": "active",
    "nextBillingDate": "2026-10-06T14:48:10Z"
  },
  "correlationId": "uuid"
}
```

**What happens behind the scenes:**
1. The endpoint extracts the user ID from the JWT token
2. It looks up or creates a Maxio customer using the user's email and name
3. It creates a subscription on that customer for the specified plan
4. It records the mapping locally in the `UserSubscriptionMapping` table
5. It returns the subscription details

### Step 4: Retrieve User's Subscriptions

```bash
curl -X GET https://localhost:25423/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

**Expected response** (200 OK):
```json
{
  "subscriptions": [
    {
      "id": 99999999,
      "productName": "$299 Pro Plan",
      "productHandle": "eshop-pro",
      "price": 299.00,
      "state": "active",
      "nextBillingDate": "2026-10-06T14:48:10Z"
    }
  ],
  "correlationId": "uuid"
}
```

### Step 5: Verify Idempotency

Create a subscription with the same user again. You should **not** see two Maxio customers or duplicate subscriptions. The service:
1. Checks if a user mapping exists
2. If yes, reuses the Maxio customer ID
3. Creates a new subscription (you can have multiple per customer)

## Maxio API Contract

The integration strictly follows the Maxio OpenAPI specification in `maxio-spec/openapi.yaml`. Key endpoints used:

- `POST /customers.json` - Create customer (idempotent via `reference` field)
- `GET /customers/lookup.json?reference=...` - Look up customer by reference
- `GET /products.json` - List all products (filtered client-side by family handle)
- `POST /subscriptions.json` - Create subscription
- `GET /subscriptions/{id}.json` - Get subscription details
- `GET /customers/{id}/subscriptions.json` - List customer's subscriptions

All requests use Basic Authentication: `Authorization: Basic Base64(api_key:x)`

## Error Handling

### Common Issues

**401 Unauthorized from Maxio:**
- Check `MAXIO_API_KEY` is set correctly
- Verify the key belongs to the sandbox account and site

**404 Not Found (subscription plans):**
- Verify `MAXIO_SITE_SUBDOMAIN` and `MAXIO_DEFAULT_PRODUCT_FAMILY` match your sandbox setup
- Check that plans exist on the Maxio site with the expected handles

**422 Unprocessable Entity (subscription creation):**
- Ensure customer email is valid
- Verify the product handle matches a real product on the site
- Check Maxio logs for detailed validation errors

**No JWT token:**
- Missing `Authorization: Bearer ...` header
- Token is expired or malformed
- Check PublicApi's authentication endpoint for obtaining a valid token

## Data Persistence

- **In-memory database** (development): `UseOnlyInMemoryDatabase=true`
  - Data persists only within a single application run
  - Useful for testing and development
  - Restart clears all subscriptions

- **SQL Server** (production): Point `ConnectionStrings.CatalogConnection` to a real SQL Server
  - Subscriptions and user mappings are persisted
  - EF Core migrations in `Infrastructure/Data/Migrations/` handle schema

The subscription tables are:
- `UserSubscriptionMappings` - User ID ↔ Maxio Customer ID mapping
- `Subscriptions` - Local cache of subscription details

## Implementation Details

### Idempotent Customer Creation

When a user subscribes, the flow ensures we don't create duplicate customers:

```
User clicks "Subscribe"
  ↓
Extract user ID from JWT
  ↓
Look up UserSubscriptionMapping for user ID
  ↓
If exists:
   Use existing MaxioCustomerId
Else:
   Look up Maxio customer by reference (user ID)
     ↓
     If exists:
        Create local mapping
     Else:
        Create new Maxio customer with reference=userID
        Create local mapping
  ↓
Create subscription using MaxioCustomerId
```

The `reference` field in Maxio is set to the eShopWeb user ID, making lookups deterministic.

### No Double-Click Subscriptions

Users can click "Subscribe" multiple times without harm:
- If same plan: Creates another subscription (Maxio allows this)
- If different plan: Creates a second subscription to the new plan

To prevent dual subscriptions, implement a frontend check or return an error if a subscription already exists for the product.

## Future Enhancements

- Webhook integration to sync subscription state from Maxio
- Subscription cancellation via `/api/subscriptions/{id}` DELETE endpoint
- Plan upgrades/downgrades via `PATCH /subscriptions/{id}`
- Usage-based billing for metered components
- Dunning management for failed payments
- Billing portal link generation for customer self-service

## References

- [Maxio Advanced Billing API](https://maxio.zendesk.com) (official docs)
- `maxio-spec/openapi.yaml` - OpenAPI specification (authoritative contract)
- [eShopOnWeb Reference Architecture](https://github.com/dotnet-architecture/eShopOnWeb)
