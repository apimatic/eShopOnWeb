# Maxio Subscription Billing Integration - Setup & Verification Guide

## Overview

This guide walks through setting up and verifying the Maxio subscription billing integration for eShopOnWeb.

## Prerequisites

- .NET 8.0 runtime and SDK installed
- Access to Maxio sandbox credentials:
  - `MAXIO_API_KEY` - Maxio API authentication key
  - `MAXIO_SITE_SUBDOMAIN` - Maxio sandbox subdomain (e.g., "cp-exp-2")
  - `MAXIO_ENVIRONMENT` - Environment identifier
  - `MAXIO_DEFAULT_PRODUCT_FAMILY` - Product family handle (e.g., "eshop-subscribe")

## Setup Instructions

### 1. Store Maxio Credentials in User Secrets

The integration reads Maxio credentials from environment variables and stores them in .NET user secrets (never commit them to the repository).

For **Windows PowerShell**:

```powershell
$apiKey = $env:MAXIO_API_KEY
$subdomain = $env:MAXIO_SITE_SUBDOMAIN
$family = $env:MAXIO_DEFAULT_PRODUCT_FAMILY

cd src/PublicApi

dotnet user-secrets init --id "microsoft-eshopweb-publicapi"
dotnet user-secrets set "Maxio:ApiKey" "$apiKey"
dotnet user-secrets set "Maxio:Subdomain" "$subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$family"
```

For **bash/zsh**:

```bash
export MAXIO_API_KEY=your_api_key_here
export MAXIO_SITE_SUBDOMAIN=your_subdomain_here
export MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe

cd src/PublicApi

dotnet user-secrets init --id "microsoft-eshopweb-publicapi"
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

### 2. Run the Application with In-Memory Database

Since this machine doesn't have SQL Server LocalDB, use the in-memory database:

```powershell
cd repo root

$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"

dotnet run --project src/PublicApi/PublicApi.csproj
```

The PublicApi will start at `https://localhost:28603`.

### 3. Set Up Test User

Use the existing authentication endpoint to create credentials:

```bash
curl -X POST https://localhost:28603/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser","password":"Pass@word1"}' \
  --insecure
```

Response contains a JWT token in the `token` field. Save this for step 4.

## Verification Steps

### Step 1: Get JWT Token

Authenticate to get a bearer token:

```bash
TOKEN=$(curl -s -X POST https://localhost:28603/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser","password":"Pass@word1"}' \
  --insecure | jq -r '.token')

echo "Token: $TOKEN"
```

### Step 2: List Available Subscription Plans

```bash
curl -X GET https://localhost:28603/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  --insecure | jq
```

**Expected Response** (200 OK):
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional monthly plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic monthly plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 3: Create a Subscription

```bash
curl -X POST https://localhost:28603/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "John",
    "lastName": "Doe",
    "email": "john@example.com",
    "productHandle": "eshop-pro"
  }' \
  --insecure | jq
```

**Expected Response** (200 OK):
```json
{
  "subscription": {
    "id": 1,
    "maxioSubscriptionId": 123456,
    "productHandle": "eshop-pro",
    "productName": "Pro Plan",
    "state": "active",
    "priceInCents": 29900,
    "nextBillingAt": "2026-10-07T00:00:00Z",
    "createdAt": "2026-09-07T12:34:56Z"
  }
}
```

### Step 4: Retrieve User's Subscriptions

```bash
curl -X GET https://localhost:28603/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure | jq
```

**Expected Response** (200 OK):
```json
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 123456,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "state": "active",
      "priceInCents": 29900,
      "nextBillingAt": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:34:56Z"
    }
  ]
}
```

### Step 5: Idempotency Test

Create a subscription for the same user again with the same email:

```bash
curl -X POST https://localhost:28603/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "John",
    "lastName": "Doe",
    "email": "john@example.com",
    "productHandle": "basic-plan"
  }' \
  --insecure | jq
```

The Maxio customer lookup succeeds (idempotent), and a new subscription to "basic-plan" is created. The user should have 2 subscriptions when checking `/my-subscriptions`.

## Architecture Overview

### New Components

**ApplicationCore:**
- `MaxioConfiguration.cs` - Configuration container for Maxio API credentials
- `Entities/MaxioCustomer.cs` - Maps eShopOnWeb users to Maxio customers (idempotency)
- `Entities/Subscription.cs` - Tracks subscription records locally
- `Services/MaxioApiClient.cs` - HTTP client for Maxio API (GET /products, POST /customers, POST /subscriptions, GET /customers/{id}/subscriptions)
- `Services/SubscriptionService.cs` - Business logic (create subscription, get subscriptions)

**Infrastructure:**
- Updated `AppIdentityDbContext` to include `MaxioCustomers` and `Subscriptions` DbSets
- Updated `Dependencies.cs` to register Maxio services

**PublicApi:**
- `SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - GET /api/subscription-plans
- `SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - POST /api/subscriptions
- `SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs` - GET /api/my-subscriptions

All endpoints require JWT bearer token authentication.

### Data Flow

1. **Get Plans**: ListSubscriptionPlansEndpoint → MaxioApiClient → GET /products.json → Maxio
2. **Create Subscription**:
   - CreateSubscriptionEndpoint receives request with user credentials
   - SubscriptionService calls MaxioApiClient.GetOrCreateCustomerAsync (idempotent)
   - Store MaxioCustomer mapping locally
   - Create subscription in Maxio
   - Store subscription record locally
3. **Get Subscriptions**: GetMySubscriptionsEndpoint → SubscriptionService → MaxioApiClient → GET /customers/{id}/subscriptions

### Database

Uses in-memory database for development (no LocalDB required). For production:
- Create EF Core migration: `dotnet ef migrations add AddSubscriptions --project src/Infrastructure --startup-project src/PublicApi`
- Apply migration before running

## Configuration Keys

All Maxio configuration comes from user secrets or environment variables:
- `Maxio:ApiKey` - from `MAXIO_API_KEY`
- `Maxio:Subdomain` - from `MAXIO_SITE_SUBDOMAIN`
- `Maxio:ProductFamilyHandle` - from `MAXIO_DEFAULT_PRODUCT_FAMILY`
- `Maxio:BaseUrl` - optional override (from `MAXIO_BASE_URL`)

## Troubleshooting

**Authentication fails with 401**
- Verify JWT token is valid: `jq -R 'split(".")[1] | @base64d | fromjson' <<< "$TOKEN"`
- Token expires after 7 days
- Re-authenticate using the endpoint in Step 1

**Maxio API returns 403**
- Verify API key and subdomain are correct
- Ensure product family handle matches ("eshop-subscribe" for sandbox)

**No subscriptions listed**
- Verify user has authenticated (valid JWT)
- Check that subscriptions were actually created (Step 3)
- In-memory database resets on app restart (data is lost)

**Subscription state is not "active"**
- State depends on Maxio product configuration
- No trial period and no payment method required for sandbox plans
- Check Maxio UI for subscription details

## Next Steps

For production deployment:
1. Use real database (SQL Server)
2. Create EF Core migration
3. Set up proper secret management (Azure Key Vault, etc.)
4. Configure HTTPS certificates properly
5. Set up monitoring and logging for Maxio API calls
6. Implement webhook handlers for Maxio events (subscription renewed, payment failed, etc.)
7. Add rate limiting and API usage tracking
