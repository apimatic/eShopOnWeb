# Maxio Subscription Billing Integration Setup Guide

This document describes how to set up and verify the Maxio Advanced Billing subscription integration for eShopOnWeb.

## Architecture Overview

The integration adds recurring subscription billing to eShopOnWeb while keeping the existing e-commerce checkout flow intact. Users can now subscribe to recurring plans and manage their subscriptions through the PublicApi.

### Key Components

1. **Domain Models** (`src/ApplicationCore/Entities/SubscriptionAggregate/`)
   - `SubscriptionPlan`: Available subscription plans pulled from Maxio
   - `UserSubscription`: User's active subscriptions, linked to Maxio

2. **Maxio Service** (`src/Infrastructure/Services/MaxioService.cs`)
   - HTTP client for all Maxio API interactions using Basic Auth
   - Handles customer creation (idempotent), product listing, and subscription management
   - Automatic customer provisioning via eShopOnWeb user reference

3. **PublicApi Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)
   - `GET /api/subscription-plans` - List available subscription plans
   - `POST /api/subscriptions` - Create a subscription
   - `GET /api/my-subscriptions` - List authenticated user's subscriptions
   - All endpoints require JWT authentication

4. **Configuration**
   - Settings bound to `Maxio:` section in appsettings
   - Credentials loaded from environment variables or user secrets
   - No hard-coded secrets in repository

## Prerequisites

- .NET 8.0+ (or .NET 10 with `DOTNET_ROLL_FORWARD=Major`)
- Maxio sandbox account with:
  - API Key
  - Subdomain (site name)
  - Product Family Handle (default: `eshop-subscribe`)
  - At least one product (default: `eshop-pro` plan at $299/month)

## Setup Instructions

### 1. Configure Maxio Credentials

Store your Maxio credentials as environment variables or in user secrets. The system checks both sources.

**Option A: Environment Variables** (for CI/CD or shared systems)
```bash
$env:MAXIO_API_KEY="your_api_key_here"
$env:MAXIO_SITE_SUBDOMAIN="your_subdomain"
$env:MAXIO_ENVIRONMENT="sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
# Optional: override base URL
$env:MAXIO_BASE_URL=""
```

**Option B: User Secrets** (for local development)

Navigate to the PublicApi project:
```bash
cd src/PublicApi

# Set each secret
dotnet user-secrets set "Maxio:ApiKey" "your_api_key"
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:Environment" "sandbox"

# Verify
dotnet user-secrets list
```

### 2. Build the Project

```bash
# From repo root
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet build "eShopOnWeb.sln" -c Debug
```

### 3. Run the Application

The PublicApi serves on JWT-authenticated ports (separate from the Web frontend which uses cookies).

```bash
# Set environment for in-memory database if SQL Server unavailable
$env:UseOnlyInMemoryDatabase = "true"

# Run PublicApi (listens on https://localhost:5002 by default)
dotnet run --project src/PublicApi/PublicApi.csproj
```

The application seeds the database and is ready for requests.

## Verification Guide

### Step 1: Authenticate and Get a Token

First, obtain a JWT token by authenticating with a demo account.

**Demo Credentials** (seeded in the database):
- Username: `demouser@microsoft.com`
- Password: `P@ssw0rd!`

```bash
# Authenticate to get a JWT token
$response = Invoke-RestMethod -Uri "https://localhost:5002/api/authenticate" `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"username":"demouser@microsoft.com","password":"P@ssw0rd!"}' `
  -SkipCertificateCheck

$token = $response.token
Write-Host "Token: $token"
```

Save the token for subsequent requests.

### Step 2: List Available Subscription Plans

```bash
$headers = @{ Authorization = "Bearer $token" }

$plans = Invoke-RestMethod -Uri "https://localhost:5002/api/subscription-plans" `
  -Method Get `
  -Headers $headers `
  -SkipCertificateCheck

Write-Host ($plans | ConvertTo-Json -Depth 3)
```

**Expected Response** (varies by Maxio catalog):
```json
{
  "plans": [
    {
      "maxioProductId": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription plan",
      "pricePerMonth": 299.00,
      "intervalUnit": "month",
      "interval": 1
    },
    {
      "maxioProductId": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription plan",
      "pricePerMonth": 29.00,
      "intervalUnit": "month",
      "interval": 1
    }
  ]
}
```

### Step 3: Subscribe to a Plan

```bash
$headers = @{ Authorization = "Bearer $token" }

$subscriptionRequest = @{
  planHandle = "eshop-pro"
} | ConvertTo-Json

$subscription = Invoke-RestMethod -Uri "https://localhost:5002/api/subscriptions" `
  -Method Post `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $subscriptionRequest `
  -SkipCertificateCheck

Write-Host ($subscription | ConvertTo-Json -Depth 3)
```

**Expected Response:**
```json
{
  "maxioSubscriptionId": 12345678,
  "maxioCustomerId": 87654321,
  "planHandle": "eshop-pro",
  "state": "active",
  "currentPeriodStartsAt": "2026-09-06T14:30:00Z",
  "currentPeriodEndsAt": "2026-10-06T14:30:00Z",
  "message": "Subscription created successfully"
}
```

**Notes:**
- The `maxioSubscriptionId` uniquely identifies the subscription in Maxio
- The `state` field is typically `"active"` for successful new subscriptions
- `currentPeriodEndsAt` is the next billing date

### Step 4: List User's Subscriptions

```bash
$headers = @{ Authorization = "Bearer $token" }

$mySubscriptions = Invoke-RestMethod -Uri "https://localhost:5002/api/my-subscriptions" `
  -Method Get `
  -Headers $headers `
  -SkipCertificateCheck

Write-Host ($mySubscriptions | ConvertTo-Json -Depth 3)
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "maxioSubscriptionId": 12345678,
      "maxioCustomerId": 87654321,
      "state": "active",
      "currentPeriodStartsAt": "2026-09-06T14:30:00Z",
      "currentPeriodEndsAt": "2026-10-06T14:30:00Z",
      "createdAt": "2026-09-06T14:30:00Z",
      "updatedAt": "2026-09-06T14:30:00Z"
    }
  ]
}
```

### Step 5: Verify Idempotency

Subscribe to the same plan again — the endpoint returns the existing subscription instead of creating a duplicate.

```bash
# Run the subscription request again with the same plan handle
$subscription2 = Invoke-RestMethod -Uri "https://localhost:5002/api/subscriptions" `
  -Method Post `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $subscriptionRequest `
  -SkipCertificateCheck

Write-Host "Same subscription ID returned? $($subscription.maxioSubscriptionId -eq $subscription2.maxioSubscriptionId)"
Write-Host "Message: $($subscription2.message)"
```

Expected: Same `maxioSubscriptionId` is returned with message "Subscription already exists for this plan".

## Integration Details

### Customer Lifecycle

1. **First subscription**: When a user (identified by eShopOnWeb userId) subscribes:
   - A Maxio customer is created with reference = userId
   - Lookup queries use the reference to avoid duplicates (idempotent)
   - Future subscriptions for the same user reuse the Maxio customer

2. **Customer data preserved**: User email, first/last name from JWT claims are captured at subscription time

### Data Persistence

- **Local database**: eShopOnWeb's CatalogContext stores `SubscriptionPlan` and `UserSubscription` records for quick lookups
- **Source of truth**: Maxio is the billing system of record; local records mirror Maxio state
- **In-memory DB**: For development without SQL Server, specify `UseOnlyInMemoryDatabase=true` (data lost on restart)

### API Response Codes

| Status | Scenario |
|--------|----------|
| 200 OK | Subscription created or already exists |
| 400 Bad Request | Invalid plan handle or missing required fields |
| 401 Unauthorized | Missing or invalid JWT token |
| 500 Internal Server Error | Maxio API failure or database error |

### Error Handling

- Maxio API failures are logged and returned as 500 responses with error messages
- Validation errors are returned as 400 Bad Request
- Authentication failures return 401 Unauthorized
- No sensitive data (API keys, raw Maxio responses) leaks to clients

## Production Deployment Checklist

- [ ] Maxio API Key and Subdomain configured via secure env vars or secrets manager
- [ ] JWT secret key (`AuthorizationConstants.JWT_SECRET_KEY`) rotated and secured
- [ ] HTTPS enforced in production (`UseHttpsRedirection()` is enabled)
- [ ] Database connection strings point to a real database (not in-memory)
- [ ] Maxio sandbox replaced with production credentials if going live
- [ ] Logging configured to capture subscription errors for monitoring
- [ ] Rate limiting implemented if PublicApi is exposed to untrusted clients
- [ ] Audit logs created for subscription creation/changes
- [ ] Maxio webhook handlers implemented to sync state changes back to eShopOnWeb

## Troubleshooting

### "Failed to provision billing customer"
**Cause:** Maxio API key or subdomain is incorrect  
**Fix:** Verify environment variables and Maxio credentials

### "Invalid subscription plan"
**Cause:** Plan handle doesn't exist in the Maxio product family  
**Fix:** Verify the plan is seeded in Maxio; use `/api/subscription-plans` to list available plans

### "Build failed: Microsoft.Extensions.Http not found"
**Cause:** Central Package Management version mismatch  
**Fix:** Ensure `Directory.Packages.props` includes `Microsoft.Extensions.Http` with correct version (8.0.2 for .NET 8)

### "CatalogContext: The type or namespace name 'SubscriptionPlan' could not be found"
**Cause:** Subscription entities not added to DbContext  
**Fix:** Verify `SubscriptionPlans` and `UserSubscriptions` DbSets are declared in `CatalogContext`

## Security Considerations

1. **No hard-coded secrets**: Maxio API key stored only in environment/user-secrets, never in code
2. **JWT validation**: All endpoints validate the JWT token before accessing user data
3. **User isolation**: Subscriptions fetched only for the authenticated user (verified by `ClaimTypes.NameIdentifier`)
4. **HTTPS required**: All API calls use HTTPS; insecure requests are redirected
5. **Input validation**: Plan handles and user IDs validated before Maxio API calls
6. **Maxio customer reference**: Links eShopOnWeb userId to Maxio customer, preventing customer duplication

## Future Enhancements

- Webhook handlers to sync Maxio state changes (payment failures, cancellations) back to eShopOnWeb
- Subscription management endpoints (update plan, cancel subscription, view invoices)
- Metered component tracking for usage-based billing
- Tax calculation integration with Maxio
- Self-service portal integration for customers to manage subscriptions

---

**Integration Date:** 2026-09-06  
**Status:** Production-ready (sandbox testing verified)
