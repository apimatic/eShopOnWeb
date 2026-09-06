# Maxio Subscription Billing Integration

This document describes how to set up and test the Maxio subscription billing integration for eShopOnWeb.

## Setup Instructions

### 1. Environment Variables

The integration uses environment variables to configure Maxio credentials. Set these in your shell before running:

```bash
# Set these from your Maxio sandbox site
export MAXIO_API_KEY="your_api_key_here"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"  # or your sandbox subdomain
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export MAXIO_ENVIRONMENT="sandbox"
```

### 2. Database Configuration

Since the default connection strings use LocalDB which may not be available:

```bash
export UseOnlyInMemoryDatabase=true
```

### 3. .NET Runtime Setup

The project pins to .NET 8.0 in `global.json`, but only .NET 10 SDK is installed:

```bash
export DOTNET_ROLL_FORWARD=Major
```

### 4. Running the PublicApi

From the repository root:

```bash
cd src/PublicApi
dotnet run
```

The API will start on `https://localhost:25643` (or the port configured in your launchSettings).

## Testing the Integration

### Prerequisites

You'll need:
- A user account in eShopOnWeb (create one via the Web frontend)
- A JWT token from the PublicApi

### Step 1: Get a JWT Token

Authenticate to get a bearer token:

```bash
curl -X POST https://localhost:25643/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "your_username",
    "password": "your_password"
  }'
```

Extract the `token` from the response. You'll use this in subsequent requests.

### Step 2: List Available Subscription Plans

```bash
curl -X GET https://localhost:25643/api/subscription-plans \
  -H "Accept: application/json"
```

Response example:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00,
      "billingCycle": "$299.00 per month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00,
      "billingCycle": "$29.00 per month"
    }
  ],
  "correlationId": "..."
}
```

### Step 3: Create a Subscription

```bash
curl -X POST https://localhost:25643/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

Response example:
```json
{
  "subscription": {
    "id": 12345678,
    "customerId": 98765,
    "productId": 7126957,
    "state": "active",
    "nextBillingAt": "2026-10-06",
    "currentPrice": 299.00
  },
  "correlationId": "..."
}
```

### Step 4: List Your Subscriptions

```bash
curl -X GET https://localhost:25643/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

Response example:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "customerId": 98765,
      "productId": 7126957,
      "state": "active",
      "nextBillingAt": "2026-10-06",
      "currentPrice": 299.00
    }
  ],
  "correlationId": "..."
}
```

## API Endpoints

### GET /api/subscription-plans
Lists all available subscription plans from Maxio.
- **Authentication**: Not required
- **Returns**: List of subscription plans

### POST /api/subscriptions
Creates a new subscription for the authenticated user.
- **Authentication**: Required (JWT bearer token)
- **Request body**:
  ```json
  {
    "productHandle": "eshop-pro"
  }
  ```
- **Returns**: Created subscription details

### GET /api/my-subscriptions
Lists all subscriptions for the authenticated user.
- **Authentication**: Required (JWT bearer token)
- **Returns**: List of user's subscriptions

## Implementation Details

### Maxio Configuration

Configuration is loaded from `appsettings.json` and environment variables in this order:
1. `Maxio:*` keys from appsettings.json
2. `MAXIO_*` environment variables
3. Defaults

Settings:
- `Maxio:ApiKey` / `MAXIO_API_KEY` - Your Maxio API key
- `Maxio:Subdomain` / `MAXIO_SITE_SUBDOMAIN` - Maxio sandbox subdomain (default: cp-exp-2)
- `Maxio:ProductFamilyHandle` / `MAXIO_DEFAULT_PRODUCT_FAMILY` - Product family handle (default: eshop-subscribe)
- `Maxio:BaseUrl` - Optional API base URL override

### Customer Mapping

Customers are created in Maxio with the eShopOnWeb user ID as the reference. This ensures idempotency:
- If a customer with that reference already exists, that customer is reused
- Subsequent calls with the same user will not create duplicate customers
- The mapping persists in Maxio

### Sandbox Entities

The demo sandbox (cp-exp-2) has these pre-configured:

**Product Family**: `eshop-subscribe` (ID: 3023074)

**Products**:
- `eshop-pro`: Pro Plan, $299.00/month (ID: 7126957)
- `basic-plan`: Basic Plan, $29.00/month (ID: 7126958)

**Component** (optional, not required for subscribe flow):
- `api-call`: Metered component, $0.01/unit (ID: 3057195)

All plans have:
- No trial period
- No setup fee
- No payment method required (pre-configured)
- Never expire
- Non-taxable

## Troubleshooting

### "API key not found" or 401 errors

Verify environment variables are set:
```bash
echo $MAXIO_API_KEY
echo $MAXIO_SITE_SUBDOMAIN
```

### "Subscription not found" errors

Check that:
1. The product handle is correct (eshop-pro or basic-plan)
2. The user is authenticated
3. The Maxio sandbox is accessible

### Database errors

Ensure `UseOnlyInMemoryDatabase=true` is set, or configure proper SQL Server connection.

### .NET version errors

Run with:
```bash
export DOTNET_ROLL_FORWARD=Major
dotnet run
```

## Notes

- All data is stored in Maxio (the subscription billing system of record)
- eShopOnWeb user ID is used as the Maxio customer reference for idempotency
- The in-memory database means subscription data is not persisted locally (it's only in Maxio)
- Payment methods are not required for these demo plans
- No 3D Secure or PCI compliance is needed for this demo setup
