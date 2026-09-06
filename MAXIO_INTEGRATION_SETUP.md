# Maxio Subscription Billing Integration Setup & Verification

## Overview
This document describes the Maxio subscription billing integration added to eShopOnWeb and how to verify it works end-to-end.

## Architecture

### Components Added

1. **Domain Model** (`src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs`)
   - Represents a local subscription record linked to a Maxio subscription
   - Stores: user ID, Maxio IDs, product handle, price, status, period dates

2. **Service** (`src/PublicApi/Services/MaxioSubscriptionService.cs`)
   - All Maxio API interactions go through this service
   - Handles: plan listing, customer idempotent lookup/creation, subscription creation, subscription listing
   - Includes proper error handling with typed and raw error accessors

3. **Endpoints** (under `src/PublicApi/SubscriptionEndpoints/`)
   - `GET /api/subscription-plans` — List all available subscription plans
   - `POST /api/subscriptions` — Subscribe authenticated user to a plan
   - `GET /api/my-subscriptions` — List authenticated user's subscriptions

4. **Configuration** (`src/PublicApi/MaxioConfiguration.cs`)
   - Binds Maxio settings from `appsettings.json` and environment variables

## Setup Steps

### 1. Configure Environment Variables

Set these environment variables before running the application. They override the placeholder values in `appsettings.json`:

```bash
# Linux/macOS
export MAXIO_API_KEY="your-maxio-api-key-here"
export MAXIO_SITE_SUBDOMAIN="cp-exp-3"
export MAXIO_ENVIRONMENT="us"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Windows (PowerShell)
$env:MAXIO_API_KEY="your-maxio-api-key-here"
$env:MAXIO_SITE_SUBDOMAIN="cp-exp-3"
$env:MAXIO_ENVIRONMENT="us"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

**IMPORTANT:** These are secrets — never commit them to the repository. Use environment variables or .NET user-secrets in development.

### 2. Verify Database Setup

The integration uses the in-memory database (as configured). When the PublicApi runs, it creates the subscription table automatically.

To use a real database instead, change the connection string and remove `UseOnlyInMemoryDatabase=true` from your configuration.

### 3. Build & Run

```bash
cd src/PublicApi
dotnet run
```

The API will start on `https://localhost:27563`.

## Verification Steps

### Step 1: Get an Authentication Token

The subscription endpoints require JWT authentication. First, authenticate:

```bash
curl -X POST "https://localhost:27563/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  --insecure
```

This returns a response with a `token` field. Save it for the next requests.

### Step 2: List Available Subscription Plans

```bash
curl -X GET "https://localhost:27563/api/subscription-plans" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  --insecure
```

Expected response:
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "...",
      "priceInCents": 29900
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "...",
      "priceInCents": 2900
    }
  ]
}
```

### Step 3: Subscribe to a Plan

```bash
curl -X POST "https://localhost:27563/api/subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

Expected response (201 Created):
```json
{
  "id": 1,
  "maxioSubscriptionId": 12345678,
  "productHandle": "eshop-pro",
  "status": "active",
  "priceInCents": 29900,
  "currentPeriodEndsAt": "2026-10-07T12:34:56Z"
}
```

### Step 4: List User's Subscriptions

```bash
curl -X GET "https://localhost:27563/api/my-subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  --insecure
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 12345678,
      "productHandle": "eshop-pro",
      "status": "active",
      "priceInCents": 29900,
      "currentPeriodEndsAt": "2026-10-07T12:34:56Z"
    }
  ]
}
```

## Production Considerations

### Security

- **Secrets**: Use .NET user-secrets in development, Azure Key Vault or similar in production
- **HTTPS**: Always use HTTPS in production
- **Rate Limiting**: Consider adding rate limiting to subscription endpoints
- **Validation**: Requests are validated at the application level; consider adding WAF rules

### Resilience

- **Retries**: The Maxio SDK includes configurable retries for transient failures
- **Timeouts**: HTTP client timeout is set to 30 seconds per attempt
- **Idempotency**: Subscription creation uses customer reference for idempotent lookups
- **Error Logging**: All errors are logged with HTTP status codes for debugging

### Monitoring

- **Logging**: Enable application logging to track Maxio API interactions
- **Metrics**: Monitor subscription creation success rates and API latencies
- **Alerts**: Set up alerts for authentication failures and API errors

## Troubleshooting

### Error: "Maxio configuration not found"
- Verify environment variables are set correctly
- Check that `appsettings.json` has the Maxio section

### Error: "Failed to list plans: HTTP 404"
- Verify the product family handle (`eshop-subscribe`) exists in your Maxio account
- Check the API key has permission to read products

### Error: "Subscription creation failed: ..."
- Check the subscription response for validation errors
- Verify the product handle exists and is valid
- Ensure the user doesn't already have an active subscription (if applicable)

### Error: "Customer creation failed: ..."
- Verify the API key and subdomain are correct
- Check that the user reference (email/ID) is unique
- Review error messages for validation failures

## Files Modified/Added

- `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs` — Domain entity
- `src/Infrastructure/Data/Config/SubscriptionConfiguration.cs` — EF configuration
- `src/Infrastructure/Data/CatalogContext.cs` — Added Subscriptions DbSet
- `src/PublicApi/MaxioConfiguration.cs` — Configuration class
- `src/PublicApi/Services/MaxioSubscriptionService.cs` — Service layer
- `src/PublicApi/SubscriptionEndpoints/` — Three API endpoints
- `src/PublicApi/Program.cs` — DI registration, HTTP client setup
- `src/PublicApi/appsettings.json` — Maxio configuration section
- `Directory.Packages.props` — Added AsadAli.AdvancedBilling.Sdk, Microsoft.Extensions.Http
- `PublicApi.csproj` — Added package references

## Next Steps

1. **Test the integration** following the verification steps above
2. **Add error handling** for specific business scenarios (e.g., cancellations, plan changes)
3. **Implement webhooks** for Maxio events (subscription changes, payment failures)
4. **Add metrics and monitoring** for production readiness
5. **Create integration tests** for the subscription endpoints
