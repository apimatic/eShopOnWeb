# Maxio Subscription Billing Setup Guide

This document describes how to set up and verify the Maxio subscription billing integration in eShopOnWeb.

## Prerequisites

- .NET 8 SDK installed
- Environment variables set from Maxio sandbox (cp-exp-3):
  - `MAXIO_API_KEY`: Your Maxio API key
  - `MAXIO_SITE_SUBDOMAIN`: `cp-exp-3` (or your sandbox subdomain)
  - `MAXIO_ENVIRONMENT`: Environment name (typically "sandbox")
  - `MAXIO_DEFAULT_PRODUCT_FAMILY`: `eshop-subscribe`

## Setup Instructions

### 1. Configure .NET User Secrets

Open a terminal in the PublicApi project directory and run:

```bash
cd src/PublicApi

# Initialize user secrets (if not already initialized)
dotnet user-secrets init

# Set the Maxio configuration from environment variables
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:BaseUrl" "https://$env:MAXIO_SITE_SUBDOMAIN.chargify.com"
```

On Windows PowerShell, use:
```powershell
$apiKey = $env:MAXIO_API_KEY
$subdomain = $env:MAXIO_SITE_SUBDOMAIN
$family = $env:MAXIO_DEFAULT_PRODUCT_FAMILY

dotnet user-secrets set "Maxio:ApiKey" "$apiKey"
dotnet user-secrets set "Maxio:Subdomain" "$subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$family"
dotnet user-secrets set "Maxio:BaseUrl" "https://$subdomain.chargify.com"
```

Verify secrets were set:
```bash
dotnet user-secrets list
```

### 2. Build the Solution

```bash
cd ../..
dotnet build eShopOnWeb.sln -c Release
```

Expected output: Build succeeded with 0 errors.

## Verification Steps

### 1. Start the PublicApi Server

From the repo root:
```bash
dotnet run --project src/PublicApi/PublicApi.csproj --configuration Release
# Or set environment variables and run
$env:UseOnlyInMemoryDatabase="true"
$env:DOTNET_ROLL_FORWARD="Major"
dotnet run --project src/PublicApi/PublicApi.csproj --configuration Release
```

The API should start on `https://localhost:27643` (HTTPS).

### 2. Authenticate and Get Token

Use curl or Postman to authenticate:

```bash
curl -X POST https://localhost:27643/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

Response:
```json
{
  "result": true,
  "token": "eyJhbGc...",
  "username": "demouser@microsoft.com",
  ...
}
```

Save the token for subsequent requests: `$TOKEN = "eyJhbGc..."`

### 3. Test GET /api/subscription-plans

List available subscription plans:

```bash
curl -X GET https://localhost:27643/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN"
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 4. Test POST /api/subscriptions

Create a subscription:

```bash
curl -X POST https://localhost:27643/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"product_id": 7126957}'
```

Expected response (201 Created):
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "reference": "user-id-guid:7126957:timestamp",
    "createdAt": "2026-09-07T12:00:00Z",
    "nextAssessmentAt": "2026-10-07T12:00:00Z"
  }
}
```

### 5. Test GET /api/my-subscriptions

List your subscriptions:

```bash
curl -X GET https://localhost:27643/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN"
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "reference": "user-id-guid:7126957:timestamp",
      "createdAt": "2026-09-07T12:00:00Z",
      "nextAssessmentAt": "2026-10-07T12:00:00Z",
      "currentPeriodEndsAt": "2026-10-07T12:00:00Z"
    }
  ]
}
```

## Idempotency Verification

The integration ensures idempotent Maxio customer creation. To verify:

1. Make two identical POST /api/subscriptions requests without creating a new subscription
2. Both should return the same subscription ID
3. Check Maxio dashboard - only one customer and one subscription should exist for the user

## Notes

- **Environment Variables**: The integration reads `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, etc., from environment at startup
- **User Secrets**: Store values in user secrets for local development, never commit them to the repository
- **In-Memory Database**: Subscription state in the app does not persist across restarts (uses in-memory DB). The Maxio API is the system of record.
- **Authentication**: PublicApi uses JWT Bearer tokens. The Web application (cookie-based) cannot call these endpoints directly.
- **Sandbox Only**: All configuration targets the Maxio sandbox (cp-exp-3). For production, swap the configuration values only.

## Troubleshooting

### "API Key is invalid" error
- Verify `MAXIO_API_KEY` environment variable is set correctly
- Verify user secrets were set: `dotnet user-secrets list`
- Ensure the key is from the correct Maxio sandbox environment

### "Customer already exists" error
- This is expected if you run the test multiple times with the same user
- The idempotency key (user ID) prevents duplicate customer creation
- Maxio returns 422 with a validation error; the service catches this and returns 400

### "Product not found" error  
- Verify the product IDs (7126957, 7126958) exist in cp-exp-3
- Check that the product family handle is correct: `eshop-subscribe`
- Ensure the product configuration permits zero-payment subscriptions

### HTTPS certificate issues
- Ensure dev cert is trusted: `dotnet dev-certs https --check`
- Trust it if needed: `dotnet dev-certs https --trust`

## Files Modified/Created

- **Created**: `src/PublicApi/Services/MaxioSubscriptionService.cs`
- **Created**: `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- **Created**: `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- **Created**: `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs`
- **Modified**: `src/PublicApi/Program.cs` (DI registration, endpoint mapping)
- **Modified**: `src/PublicApi/appsettings.json` (Maxio config keys)
- **Modified**: `Directory.Packages.props` (SDK package version)

