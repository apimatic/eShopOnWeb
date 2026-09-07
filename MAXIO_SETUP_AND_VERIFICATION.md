# Maxio Subscription Billing Integration - Setup & Verification Guide

This document provides step-by-step instructions to set up and verify the Maxio Advanced Billing integration for eShopOnWeb.

## Prerequisites

- .NET 8.0+ SDK (or allow rollforward via `DOTNET_ROLL_FORWARD=Major`)
- Maxio Advanced Billing sandbox account with:
  - Product Family: `eshop-subscribe` (or configured via `MAXIO_DEFAULT_PRODUCT_FAMILY`)
  - Plans: `eshop-pro` ($299/mo) and `basic-plan` ($29/mo)
  - Credentials available as environment variables

## Environment Setup

### 1. Set Maxio Credentials

Set the following environment variables with your Maxio sandbox credentials:

**Windows (PowerShell):**
```powershell
$env:MAXIO_API_KEY = "your_api_key_here"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:MAXIO_BASE_URL = ""  # Leave empty to use https://{subdomain}.chargify.com
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

**Linux/macOS (Bash):**
```bash
export MAXIO_API_KEY="your_api_key_here"
export MAXIO_SITE_SUBDOMAIN="your_subdomain"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export MAXIO_BASE_URL=""
export UseOnlyInMemoryDatabase="true"
export DOTNET_ROLL_FORWARD="Major"
```

### 2. Alternative: Use User Secrets (Development)

Instead of environment variables, you can use .NET user-secrets:

```bash
cd src/PublicApi

# Initialize user secrets (if not already done)
dotnet user-secrets init

# Set the Maxio configuration
dotnet user-secrets set "Maxio:ApiKey" "your_api_key_here"
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:BaseUrl" ""
```

## Running the Application

### Start PublicApi

```bash
# From repository root
cd src/PublicApi
dotnet run
```

The application will start on:
- HTTPS: https://localhost:28243
- HTTP: http://localhost:28244

Swagger UI is available at: https://localhost:28243/swagger

## Verification Steps

### Step 1: Get Available Plans

**Request:**
```bash
curl -X GET "https://localhost:28243/api/subscription-plans" \
  -H "Accept: application/json" \
  -k  # Ignore self-signed cert for local testing
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription plan",
      "price": 299.00,
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription plan",
      "price": 29.00,
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 2: Authenticate

**Request:**
```bash
curl -X POST "https://localhost:28243/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser","password":"Pass@word1"}' \
  -k
```

**Expected Response:**
```json
{
  "result": true,
  "token": "eyJhbGc...",
  "username": "demouser",
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "correlationId": "..."
}
```

Save the `token` value for the next steps.

### Step 3: Create a Subscription

**Request:**
```bash
# Replace TOKEN with the token from Step 2
curl -X POST "https://localhost:28243/api/subscriptions" \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k
```

**Expected Response (201 Created):**
```json
{
  "subscription": {
    "id": 123456789,
    "customerId": 987654321,
    "productHandle": "eshop-pro",
    "productName": "Pro Plan",
    "state": "active",
    "currentPeriodStartsAt": "2026-09-07T12:00:00Z",
    "currentPeriodEndsAt": "2026-10-07T12:00:00Z",
    "nextAssessmentAt": "2026-10-07T12:00:00Z",
    "createdAt": "2026-09-07T12:00:00Z",
    "updatedAt": "2026-09-07T12:00:00Z"
  }
}
```

### Step 4: Get User Subscriptions

**Request:**
```bash
# Use the same token from authentication
curl -X GET "https://localhost:28243/api/my-subscriptions" \
  -H "Authorization: Bearer TOKEN" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 123456789,
      "customerId": 987654321,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "state": "active",
      "currentPeriodStartsAt": "2026-09-07T12:00:00Z",
      "currentPeriodEndsAt": "2026-10-07T12:00:00Z",
      "nextAssessmentAt": "2026-10-07T12:00:00Z",
      "createdAt": "2026-09-07T12:00:00Z",
      "updatedAt": "2026-09-07T12:00:00Z"
    }
  ]
}
```

### Step 5: Verify Idempotency

Create another subscription with the same user and plan handle - verify that it either:
1. Creates a new subscription, or
2. Returns the existing one (depending on Maxio configuration)

Request the same plan twice with the same authenticated user and verify proper handling.

## Key Implementation Details

### Architecture
- **Service Layer**: `MaxioSubscriptionService` in `src/PublicApi/Services/`
  - Manages all Maxio API interactions via HttpClient
  - Implements idempotent customer creation
  - Handles JSON serialization/deserialization
  - Comprehensive error logging

- **Endpoints**: Located in `src/PublicApi/SubscriptionEndpoints/`
  - `ListSubscriptionPlansEndpoint`: Public endpoint, no auth required
  - `CreateSubscriptionEndpoint`: Requires JWT authentication
  - `GetUserSubscriptionsEndpoint`: Requires JWT authentication

### Configuration
- Settings loaded from environment variables with fallback to `appsettings.json`
- Maxio credentials: `ApiKey`, `Subdomain`, `ProductFamilyHandle`, `BaseUrl`
- Environment variables: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY`, `MAXIO_BASE_URL`

### Security
- JWT-based authentication for subscription management endpoints
- Basic auth with Maxio API (API key + "x" as placeholder password)
- HTTPS enforcement (dev cert required)
- No secrets stored in repository

### Error Handling
- Comprehensive logging via ILogger
- Proper HTTP status codes (201 for creation, 401 for auth failures, etc.)
- Graceful handling of missing customers/subscriptions

## Troubleshooting

### "The type or namespace name 'Maxio' could not be found"
- Ensure you didn't use the Maxio SDK namespace - the integration uses HttpClient directly
- All Maxio interactions go through REST API endpoints

### 401 Unauthorized from Maxio API
- Verify `MAXIO_API_KEY` is correct
- Check that the subdomain matches your Maxio sandbox account
- Ensure credentials are in the correct format (Base64 encoded for Basic auth)

### Plans not appearing
- Verify the product family handle matches `MAXIO_DEFAULT_PRODUCT_FAMILY`
- Check that products exist in Maxio with handles starting with "eshop-"
- Review Maxio logs for any API errors

### Customer creation fails
- Ensure the email format is valid
- Verify no duplicate customer reference exists in Maxio
- Check for account-level permission restrictions

### "Bearer token is null or invalid"
- Ensure you're passing the token from the `/api/authenticate` endpoint
- Include the format: `Authorization: Bearer <token>`
- Verify the token hasn't expired (7-day expiry by default)

## Performance Considerations

- Plans list is fetched fresh on each request (consider adding caching)
- Customer lookup uses Maxio's reference lookup (efficient)
- Subscriptions are created synchronously (no background jobs)

## Next Steps

### Production Deployment
1. Use secrets management (Azure Key Vault, AWS Secrets Manager)
2. Implement plan caching with TTL
3. Add request rate limiting
4. Implement webhook handlers for subscription lifecycle events
5. Add comprehensive test coverage
6. Set up monitoring and alerting

### Feature Enhancements
- Subscription pause/resume/cancel endpoints
- Plan upgrade/downgrade
- Payment method management
- Metered usage tracking (for the `api-call` component)
- Webhook event handling
- Subscription renewal notifications

## References

- [Maxio Advanced Billing API Docs](https://developers.maxio.com/)
- [Maxio Help Center](https://docs.maxio.com/hc/en-us/)
- eShopOnWeb PublicApi project structure
