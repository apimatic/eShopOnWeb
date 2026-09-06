# Maxio Advanced Billing Integration Setup Guide

This document provides step-by-step instructions for setting up, configuring, and testing the Maxio Advanced Billing integration with eShopOnWeb.

## Prerequisites

- .NET 10 SDK (or use `DOTNET_ROLL_FORWARD=Major` if you have .NET 8 installed)
- Access to a Maxio sandbox site at `cp-exp-2`
- The following credentials/information from your Maxio site:
  - API Key
  - Subdomain
  - Product Family Handle (default: `eshop-subscribe`)
  - Available product handles (e.g., `eshop-pro`, `basic-plan`)

## Configuration

### 1. Set Environment Variables

Set the following environment variables on your machine with your Maxio sandbox credentials:

```bash
# Windows PowerShell
$env:MAXIO_API_KEY = "<your-api-key>"
$env:MAXIO_SITE_SUBDOMAIN = "<your-subdomain>"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
# Optional: override the base URL if needed
# $env:MAXIO_BASE_URL = "<custom-base-url>"
```

```bash
# Windows Command Prompt
set MAXIO_API_KEY=<your-api-key>
set MAXIO_SITE_SUBDOMAIN=<your-subdomain>
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

```bash
# Linux/macOS Bash
export MAXIO_API_KEY="<your-api-key>"
export MAXIO_SITE_SUBDOMAIN="<your-subdomain>"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

### 2. Optional: Configure User Secrets (Development Only)

For local development, you can use .NET user-secrets to store sensitive credentials:

```bash
cd src/PublicApi

# Initialize user secrets (if not already done)
dotnet user-secrets init

# Store your Maxio credentials
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "<your-subdomain>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

User-secrets take precedence over environment variables during development.

## Building the Application

### Handle SDK/Runtime Mismatch

If you have .NET 8 but the SDK is pinned to 8.0.x in `global.json`:

**Option 1: Use environment variable to roll forward**
```bash
set DOTNET_ROLL_FORWARD=Major
dotnet build
```

**Option 2: Install ASP.NET Core 8.0 Runtime**
Download and install from: https://dotnet.microsoft.com/download/dotnet/8.0

### Build and Run

```bash
cd repo

# Restore and build
dotnet build

# Run PublicApi (subscription features)
cd src/PublicApi
dotnet run
```

The PublicApi will start at: `https://localhost:25723/api/`

## Testing the Integration

### 1. Get an Authentication Token

First, authenticate to get a JWT token:

```bash
curl -X POST https://localhost:25723/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }'
```

Save the `token` from the response.

### 2. List Available Subscription Plans

```bash
curl -X GET https://localhost:25723/api/subscription-plans \
  -H "Content-Type: application/json"
```

Expected response:
```json
{
  "subscriptionPlans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "...",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month",
      "requireCreditCard": false
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "...",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month",
      "requireCreditCard": false
    }
  ],
  "correlationId": "..."
}
```

### 3. Create a Subscription

Subscribe the user to the Pro Plan:

```bash
curl -X POST https://localhost:25723/api/subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN_HERE>" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

Expected response:
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "customerId": 98765432,
    "productId": 7126957,
    "productHandle": "eshop-pro",
    "productName": "Pro Plan",
    "productPriceInCents": 29900,
    "currentPeriodStartsAt": "2026-09-06T...",
    "currentPeriodEndsAt": "2026-10-06T...",
    "nextAssessmentAt": "2026-10-06T..."
  },
  "correlationId": "..."
}
```

### 4. List User's Subscriptions

Retrieve all subscriptions for the authenticated user:

```bash
curl -X GET https://localhost:25723/api/my-subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN_HERE>" \
  -H "Content-Type: application/json"
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "customerId": 98765432,
      "productId": 7126957,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "productPriceInCents": 29900,
      "currentPeriodStartsAt": "2026-09-06T...",
      "currentPeriodEndsAt": "2026-10-06T...",
      "nextAssessmentAt": "2026-10-06T..."
    }
  ],
  "correlationId": "..."
}
```

### 5. Verify Idempotency

Try creating a subscription for the same product again with the same user token:
- The endpoint should return the same subscription if one already exists
- No duplicate subscription should be created

## API Endpoints Summary

| Method | Endpoint | Authentication | Purpose |
|--------|----------|----------------|---------|
| GET | `/api/subscription-plans` | None | List available subscription plans |
| POST | `/api/subscriptions` | JWT Required | Create a subscription for authenticated user |
| GET | `/api/my-subscriptions` | JWT Required | List all subscriptions for authenticated user |

## Key Implementation Details

### Configuration Schema

All Maxio settings are read from the `Maxio:` configuration section:
- `Maxio:ApiKey` - The Maxio API key (from `MAXIO_API_KEY` env var)
- `Maxio:Subdomain` - The Maxio subdomain (from `MAXIO_SITE_SUBDOMAIN` env var)
- `Maxio:ProductFamilyHandle` - Product family handle (from `MAXIO_DEFAULT_PRODUCT_FAMILY` env var)
- `Maxio:BaseUrl` - Optional override for the API base URL

### Authentication

- PublicApi uses JWT Bearer token authentication
- Customer identity is extracted from the JWT claims (`sub` or `NameIdentifier`)
- User reference in Maxio is the authenticated user's ID from eShopOnWeb

### Customer Management

- Customers are created automatically in Maxio when a subscription is created
- Customer reference is the eShopOnWeb user ID (idempotent)
- Double-click is safe: creating a subscription for an existing customer reuses the same Maxio customer

### Subscription Flow

1. Authenticated user requests subscription creation
2. Service checks if customer exists in Maxio by reference (user ID)
3. If not, creates customer automatically
4. Creates subscription for that customer with the requested product
5. Returns subscription details including state, pricing, and billing dates

## Troubleshooting

### "API key invalid" or "Unauthorized" errors
- Verify environment variables are set correctly
- Check that the API key has not expired in Maxio
- Ensure the subdomain matches your Maxio site

### "Product not found" errors
- Verify the product handle is spelled correctly
- Confirm the product exists in the Maxio product family
- Check that the product family handle is correct

### "No payment method on file" errors (422)
- This integration is configured for products that don't require a payment method
- If you get this error, verify the products in Maxio don't have "Request credit card" enabled

### Certificate/HTTPS errors
- Ensure the dev certificate is trusted: `dotnet dev-certs https --check --trust`
- Regenerate if needed: `dotnet dev-certs https --clean && dotnet dev-certs https`

## Production Deployment Notes

When deploying to production:

1. **Store credentials securely:**
   - Use Azure Key Vault, AWS Secrets Manager, or similar
   - Never commit secrets to version control
   - Use environment variables or secure configuration providers

2. **Update configuration:**
   - Set `Maxio:BaseUrl` to production Maxio URL if different
   - Update `Maxio:ApiKey` and `Maxio:Subdomain` for production site

3. **Monitoring:**
   - Enable structured logging for Maxio API calls
   - Monitor subscription creation failures
   - Set up alerts for API errors

4. **Testing:**
   - Test with production Maxio sandbox first
   - Verify all workflows with actual payment methods in production
   - Test error scenarios and edge cases

## Architecture

### Project Structure

- `src/PublicApi/MaxioBilling/` - Maxio integration services
  - `IMaxioBillingService.cs` - Interface and DTOs
  - `MaxioBillingService.cs` - Implementation with HTTP calls to Maxio API

- `src/PublicApi/SubscriptionEndpoints/` - API endpoints
  - `ListSubscriptionPlansEndpoint.cs` - GET /api/subscription-plans
  - `CreateSubscriptionEndpoint.cs` - POST /api/subscriptions
  - `ListMySubscriptionsEndpoint.cs` - GET /api/my-subscriptions

- `src/PublicApi/MaxioSettings.cs` - Configuration class

### Design Patterns

- **Idempotent Customer Creation:** Uses customer reference (user ID) to ensure duplicate subscriptions aren't created
- **Async/Await:** All API calls are fully asynchronous
- **Dependency Injection:** HttpClient and configuration are injected
- **Structured Logging:** All errors are logged with context
- **Bearer Token Auth:** Uses JWT tokens extracted from HTTP context

## Support

For issues with:
- **eShopOnWeb:** See https://github.com/dotnet-architecture/eShopOnWeb
- **Maxio API:** See https://maxio.zendesk.com/hc/en-us or refer to the Maxio Billing API docs
