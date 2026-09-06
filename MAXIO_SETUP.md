# Maxio Subscription Billing Integration Setup

This document describes how to set up and use the Maxio subscription billing integration for eShopOnWeb.

## Environment Setup

### 1. Set Environment Variables

Before running the PublicApi project, set these environment variables with your Maxio sandbox credentials:

```bash
# Linux/macOS
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Windows (PowerShell)
$env:MAXIO_API_KEY="your-api-key"
$env:MAXIO_SITE_SUBDOMAIN="cp-exp-2"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Windows (Command Prompt)
set MAXIO_API_KEY=your-api-key
set MAXIO_SITE_SUBDOMAIN=cp-exp-2
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### 2. (Optional) Use .NET User Secrets for Development

For development convenience, you can store secrets in .NET user-secrets:

```bash
# From the PublicApi directory
cd src/PublicApi

# Set the secrets
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Verify secrets are set
dotnet user-secrets list
```

## Running the Application

### With In-Memory Database (Default)

The PublicApi uses an in-memory database by default. Run with:

```bash
cd src/PublicApi
dotnet run --environment Development
```

Or with environment variables (these take precedence over user-secrets):

```bash
# Linux/macOS
MAXIO_API_KEY="your-api-key" MAXIO_SITE_SUBDOMAIN="cp-exp-2" MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe" dotnet run --environment Development

# Windows (PowerShell)
$env:MAXIO_API_KEY="your-api-key"; $env:MAXIO_SITE_SUBDOMAIN="cp-exp-2"; $env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"; dotnet run --environment Development

# Windows (Command Prompt)
set MAXIO_API_KEY=your-api-key & set MAXIO_SITE_SUBDOMAIN=cp-exp-2 & set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe & dotnet run --environment Development
```

### Handle SDK/Runtime Mismatch

If you see warnings about SDK version mismatch, allow rollforward:

```bash
DOTNET_ROLL_FORWARD=Major dotnet run --environment Development
```

## API Endpoints

### Authentication

First, authenticate to get a JWT token:

```bash
curl -X POST https://localhost:27543/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"[GH0st]*"}'
```

Response includes a `token` field. Use this token for subsequent requests.

### 1. List Available Plans

```bash
curl -X GET https://localhost:27543/api/subscription-plans \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

Response example:
```json
{
  "correlationId": "...",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with advanced features",
      "pricePerMonth": 299.00,
      "billingInterval": "Every 1 month",
      "hasTrial": false,
      "trialDays": null
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic plan for getting started",
      "pricePerMonth": 29.00,
      "billingInterval": "Every 1 month",
      "hasTrial": false,
      "trialDays": null
    }
  ]
}
```

### 2. Subscribe to a Plan

```bash
curl -X POST https://localhost:27543/api/subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}'
```

Response example:
```json
{
  "correlationId": "...",
  "success": true,
  "message": "Successfully subscribed to Pro Plan",
  "subscriptionId": 12345678,
  "state": "active",
  "customerMaxioId": 9876543,
  "planName": "Pro Plan",
  "pricePerMonth": 299.00,
  "nextBillingAt": "2026-10-07T...",
  "errorMessage": null
}
```

### 3. Get Current User's Subscriptions

```bash
curl -X GET https://localhost:27543/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

Response example:
```json
{
  "correlationId": "...",
  "success": true,
  "message": "Found 1 subscription(s)",
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "nextBillingAt": "2026-10-07T...",
      "mrrPerMonth": 299.00,
      "createdAt": "2026-09-07T...",
      "updatedAt": "2026-09-07T..."
    }
  ],
  "errorMessage": null
}
```

## Available Plans

The following plans are pre-configured on the Maxio sandbox (site `cp-exp-2`):

| Plan | Handle | Price | Details |
|------|--------|-------|---------|
| Pro Plan | `eshop-pro` | $299.00/month | Professional features, no trial, no setup fee |
| Basic Plan | `basic-plan` | $29.00/month | Basic features, no trial, no setup fee |

Both plans:
- No payment method required at signup
- Monthly billing interval
- Never expire
- Not taxable by default

## Customer Management

When a user subscribes:

1. The system looks up an existing Maxio customer by their eShopOnWeb user ID (as the `reference` field)
2. If not found, a new customer is automatically created with:
   - `reference` = eShopOnWeb user ID
   - `first_name`, `last_name`, `email` from JWT claims
3. A subscription is created for that customer

This ensures that:
- Multiple subscriptions can be managed per eShopOnWeb user
- Double-clicking subscribe doesn't create duplicate customers or subscriptions
- The mapping between eShopOnWeb users and Maxio customers is idempotent

## Database Notes

The PublicApi uses an **in-memory database** by default, which:
- Requires no SQL Server or LocalDB installation
- Loses all data when the app restarts
- The user ID ↔ subscription mapping only persists within a single run

To persist data across restarts, set `UseOnlyInMemoryDatabase=false` in configuration and ensure you have SQL Server LocalDB or a proper SQL Server connection.

## Troubleshooting

### "Failed to create or retrieve customer"
- Verify your Maxio credentials (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN)
- Ensure the API key has permission to create customers
- Check that your IP address is whitelisted for API access

### "Plan not found"
- Verify the plan handle is correct (e.g., "eshop-pro" or "basic-plan")
- Ensure you're using the correct Maxio site (cp-exp-2)
- Plans must be created in Maxio before they can be subscribed to

### 401 Unauthorized on API endpoints
- Ensure your JWT token is valid and included in the Authorization header
- Tokens expire after a period; get a new one from the `/api/authenticate` endpoint
- Verify you're using "Bearer" scheme in the header

### HTTPS certificate errors
- Ensure the dev certificate is trusted: `dotnet dev-certs https --check`
- If not trusted, install it: `dotnet dev-certs https --trust`

## Architecture

The integration follows these principles:

- **Service Layer**: `MaxioApiService` handles all HTTP communication with Maxio
- **Configuration**: Settings read from environment variables or configuration files
- **Endpoints**: Follow the existing Ardalis.ApiEndpoints pattern
- **Authentication**: JWT-based, inherited from PublicApi's existing auth
- **Idempotency**: Customer lookup by reference ensures no duplicate customers
- **Secrets**: Never stored in code; loaded from environment or user-secrets

## Next Steps

- Integrate subscription status into the Web UI
- Set up webhook handlers for subscription lifecycle events
- Implement subscription management (upgrade, downgrade, cancel)
- Add usage metering for the API-call component
- Set up automated billing compliance checks
