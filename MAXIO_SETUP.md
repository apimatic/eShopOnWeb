# Maxio Subscription Billing Integration Setup

This guide explains how to set up and test the Maxio subscription billing integration for eShopOnWeb.

## Environment Setup

### 1. Set Environment Variables for Maxio Credentials

Before running the application, set the following environment variables with your Maxio sandbox credentials:

```powershell
# Windows PowerShell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "your-subdomain"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

```bash
# Linux/macOS
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="your-subdomain"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

### 2. Configure for In-Memory Database (Development)

Set this environment variable to use the in-memory database:

```powershell
$env:UseOnlyInMemoryDatabase = "true"
```

```bash
export UseOnlyInMemoryDatabase=true
```

### 3. (Optional) Store Credentials in User Secrets

Instead of using environment variables, you can use .NET user secrets:

```bash
cd src/PublicApi

# Initialize user secrets (if not already done)
dotnet user-secrets init

# Set Maxio credentials
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-subdomain"
dotnet user-secrets set "Maxio:Environment" "sandbox"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

## Running the Application

### With SDK Roll Forward

Since the project is pinned to .NET 8.0 but uses `rollForward: latestMajor`, you can run it with .NET 10:

```bash
cd C:\claude-runs\t1h45ali-openapi-haiku45high-029\repo
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --project src/PublicApi/PublicApi.csproj
```

Or without setting the env var if you're using .NET 10 SDK:

```bash
dotnet run --project src/PublicApi/PublicApi.csproj
```

## API Endpoints

### 1. Authenticate (Get JWT Token)

First, authenticate to get a JWT token:

```bash
curl -X POST https://localhost:28623/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@example.com",
    "password": "Pass@word1"
  }' \
  -k  # Ignore self-signed cert
```

Copy the returned token for use in subsequent requests.

### 2. List Available Subscription Plans

```bash
curl -X GET https://localhost:28623/api/subscription-plans \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -k
```

Example response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "$299/month Pro Plan",
      "description": "Professional plan",
      "priceInCents": 29900,
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "$29/month Basic Plan",
      "description": "Basic plan",
      "priceInCents": 2900,
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 3. Create a Subscription

```bash
curl -X POST https://localhost:28623/api/subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  -k
```

Example response:
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productName": "$299/month Pro Plan",
  "priceInCents": 29900,
  "price": 299.00,
  "nextAssessmentAt": "2024-10-07T14:00:00Z",
  "activatedAt": "2024-09-07T14:00:00Z",
  "message": "Subscription created successfully"
}
```

### 4. Get Current User's Subscriptions

```bash
curl -X GET https://localhost:28623/api/my-subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -k
```

Example response:
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productName": "$299/month Pro Plan",
      "productHandle": "eshop-pro",
      "priceInCents": 29900,
      "price": 299.00,
      "balanceInCents": 0,
      "currentPeriodEndsAt": "2024-10-07T14:00:00Z",
      "nextAssessmentAt": "2024-10-07T14:00:00Z",
      "activatedAt": "2024-09-07T14:00:00Z",
      "canceledAt": null
    }
  ],
  "message": "Found 1 subscription(s)"
}
```

## Testing Checklist

- [ ] Environment variables or user secrets are set correctly
- [ ] Application builds successfully
- [ ] Application runs without errors
- [ ] Can authenticate and get a JWT token
- [ ] Can list subscription plans
- [ ] Can create a subscription
- [ ] Can retrieve user's subscriptions
- [ ] Maxio customer is created idempotently (no duplicates on multiple calls)
- [ ] Subscription state reflects correctly in Maxio

## Troubleshooting

### Cannot find Maxio configuration

Ensure that `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN` environment variables are set before running the application. The configuration is read at startup.

### Subscription creation fails

Check:
1. API key and subdomain are correct
2. The product family handle matches the seeded handle on your Maxio site
3. The plan handle (eshop-pro, basic-plan) exists in the product family
4. Check Maxio logs for error details

### Database issues

The integration uses an in-memory database for development. All data is lost when the application restarts. To use a persistent database, modify the connection strings in `appsettings.json` or set `UseOnlyInMemoryDatabase=false`.

### HTTPS certificate errors

When testing locally, you may see certificate warnings. Use the `-k` or `--insecure` flag in curl, or configure your HTTP client to accept self-signed certificates.

## Architecture Overview

- **MaxioSettings**: Configuration class for Maxio API credentials and settings
- **MaxioApiService**: HTTP client service for communicating with Maxio API
- **MaxioCustomerMapping**: Database entity mapping ApplicationUser to Maxio customer ID
- **SubscriptionEndpoints**: Three Minimal API endpoints for subscription management
  - ListSubscriptionPlansEndpoint: GET /api/subscription-plans
  - CreateSubscriptionEndpoint: POST /api/subscriptions
  - GetMySubscriptionsEndpoint: GET /api/my-subscriptions

All endpoints require JWT authentication via the Bearer token scheme.
