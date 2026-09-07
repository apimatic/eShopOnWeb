# Maxio Subscription Billing Integration Guide

## Overview

This document describes the subscription billing capability added to eShopOnWeb using Maxio Advanced Billing as the billing system of record.

## Architecture

### New Components

**Entities (ApplicationCore)**
- `UserSubscription` — tracks subscription state synced from Maxio (userId, maxioSubscriptionId, productHandle, state, etc.)

**DTOs (PublicApi/SubscriptionEndpoints)**
- `SubscriptionPlanDto` — plan details returned by listing endpoint
- `SubscriptionDto` — subscription state returned by create/list endpoints
- Response wrappers: `ListSubscriptionPlansResponse`, `CreateSubscriptionResponse`, `ListUserSubscriptionsResponse`

**Service (PublicApi/Services)**
- `MaxioSubscriptionService` — stateless service that wraps Maxio SDK calls
  - `ListSubscriptionPlansAsync()` — retrieves plans from Maxio
  - `EnsureCustomerExistsAsync()` — idempotent customer lookup/creation
  - `CreateSubscriptionAsync()` — creates subscription for Maxio customer
  - `ListUserSubscriptionsAsync()` — lists active subscriptions for customer

**Endpoints (PublicApi/SubscriptionEndpoints)**
- `GET /api/subscription-plans` — list available plans (JWT-authenticated)
- `POST /api/subscriptions` — subscribe to a plan (creates/looks up customer, creates subscription)
- `GET /api/my-subscriptions` — list user's active subscriptions (JWT-authenticated)

### Configuration

Settings are loaded from environment variables into .NET configuration under `Maxio:` section:

```
Maxio:ApiKey              (from MAXIO_API_KEY)
Maxio:Subdomain           (from MAXIO_SITE_SUBDOMAIN)
Maxio:Environment         (from MAXIO_ENVIRONMENT — "sandbox" or "production")
Maxio:ProductFamilyHandle (from MAXIO_DEFAULT_PRODUCT_FAMILY)
Maxio:BaseUrl             (optional override)
```

## Setup & Verification

### Step 1: Load Maxio Credentials into User Secrets

The SDK requires credentials to connect to Maxio. Store them securely using .NET user-secrets (never hardcode in repo):

```powershell
# Set the API key (from Maxio API Keys page)
dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY_HERE" --project src/PublicApi

# Set the site subdomain (e.g., "cp-exp-1" for the sandbox demo site)
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-1" --project src/PublicApi

# Set the environment
dotnet user-secrets set "Maxio:Environment" "sandbox" --project src/PublicApi

# Set the default product family handle
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe" --project src/PublicApi
```

Or set environment variables before running:
```powershell
$env:MAXIO_API_KEY = "YOUR_API_KEY"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-1"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

### Step 2: Configure In-Memory Database (for development)

The app uses in-memory database for this run. No migrations needed. Set environment variable:
```powershell
$env:UseOnlyInMemoryDatabase = 'true'
```

### Step 3: Run the PublicApi

```powershell
cd src/PublicApi
dotnet run
```

The PublicApi listens on `https://localhost:5000` (or assigned port from `APP_PORT_BLOCK_BASE`).

### Step 4: Authenticate to Get a Bearer Token

POST to `/api/authenticate` with test credentials:

```powershell
$response = Invoke-WebRequest -Uri "https://localhost:5000/api/authenticate" `
  -Method POST `
  -ContentType "application/json" `
  -Body @{ username = "test@example.com"; password = "Pass123!" } `
  -SkipCertificateCheck

$token = ($response.Content | ConvertFrom-Json).token
Write-Host "Bearer token: $token"
```

Or use curl:
```bash
curl -X POST https://localhost:5000/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"test@example.com","password":"Pass123!"}' \
  -k
```

### Step 5: Test the Subscription Endpoints

#### List Available Subscription Plans

```powershell
$headers = @{ "Authorization" = "Bearer $token" }

$plans = Invoke-WebRequest -Uri "https://localhost:5000/api/subscription-plans" `
  -Headers $headers `
  -SkipCertificateCheck | ConvertFrom-Json

$plans.plans | ForEach-Object { 
  Write-Host "Plan: $($_.name) ($($_.handle)) - `$$($_.price)/month"
}
```

Expected output: Lists `eshop-pro` and `basic-plan` from the Maxio sandbox.

#### Create a Subscription

```powershell
$body = @{ productHandle = "eshop-pro" } | ConvertTo-Json

$subscription = Invoke-WebRequest -Uri "https://localhost:5000/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body `
  -SkipCertificateCheck | ConvertFrom-Json

Write-Host "Subscription created: ID=$($subscription.subscription.id), State=$($subscription.subscription.state)"
Write-Host "Next billing date: $($subscription.subscription.nextAssessmentAt)"
```

Expected: Returns subscription details with `state: "active"` (or other state depending on plan).

#### List User's Subscriptions

```powershell
$subscriptions = Invoke-WebRequest -Uri "https://localhost:5000/api/my-subscriptions" `
  -Headers $headers `
  -SkipCertificateCheck | ConvertFrom-Json

$subscriptions.subscriptions | ForEach-Object { 
  Write-Host "Subscription: $($_.productName) ($($_.productHandle)) - State: $($_.state)"
  Write-Host "  Next billing: $($_.nextAssessmentAt)"
}
```

Expected: Lists all subscriptions for the logged-in user.

### Step 6: Verify Idempotency

Double-click the subscribe endpoint with the same product:

```powershell
# Run the create subscription request twice with the same product
$subscription2 = Invoke-WebRequest -Uri "https://localhost:5000/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body `
  -SkipCertificateCheck | ConvertFrom-Json

Write-Host "Second subscription ID: $($subscription2.subscription.id)"
```

Expected: The user lookup is idempotent (no duplicate Maxio customers created), but a second subscription creation will either succeed with a new subscription or fail if the plan doesn't allow multiple subscriptions.

## Error Handling

The integration includes production-grade error handling:

- **Typed errors (422 Validation)**: `CreateCustomerError`, `CreateSubscriptionError` with typed accessors
- **Network errors**: Caught and wrapped as `InvalidOperationException` with caller-safe messages
- **JSON parse failures**: Caught as `System.Text.Json.JsonException` and logged
- **Authorization errors**: Returns 401 Unauthorized if JWT is missing or invalid
- **User not found**: Returns 404 if identity claim cannot be resolved

All errors are logged with structured context (user ID, Maxio customer ID, error detail).

## Notes

- **In-memory database limitation**: Subscription records persist only within a single run; they are lost on restart.
- **No payment collection**: The integration assumes payment profiles are on file in Maxio or the site is configured for invoice-based payment. No credit card capture is done by eShopOnWeb.
- **Plan states**: The `Subscription.State` enum includes `Active`, `Canceled`, `Paused`, `TrialEnded`, `PastDue`, etc. Plans in the sandbox have no trial period.
- **USD pricing**: Prices are stored in cents (multiply by 100); the DTO converts to decimal dollars for display.
- **CORS**: Ensure PublicApi CORS policy allows your client origin if testing cross-origin.

## Files Modified/Created

**Created:**
- `src/ApplicationCore/Entities/SubscriptionAggregate/UserSubscription.cs`
- `src/PublicApi/Services/MaxioSubscriptionService.cs`
- `src/PublicApi/SubscriptionEndpoints/*.cs` (DTOs and endpoints)

**Modified:**
- `src/PublicApi/Program.cs` — DI registration for Maxio client and service
- `Directory.Packages.props` — Added Maxio SDK NuGet package reference

**No changes** to Web project, existing endpoints, or data migrations (in-memory only).

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| `Maxio configuration is missing` | Credentials not set in secrets/env vars | Load user-secrets as per Step 1 |
| `401 Unauthorized` | Missing/invalid JWT token | Get a new token via /api/authenticate |
| `404 Not Found on /api/subscription-plans` | Endpoint not registered | Verify PublicApi started; check port |
| `Subscription creation failed: ...` | Plan constraints, missing customer, or Maxio error | Check Maxio sandbox site; logs have detail |
| `User not found` | JWT claim resolution failed | Verify user ID matches a real eShopOnWeb user |

## Next Steps (Future Work)

1. **Persistence**: Add a database migration to `CatalogContext` to persist `UserSubscription` records across runs
2. **Webhook Handling**: Implement Maxio webhooks to sync subscription state changes (cancellations, renewals)
3. **Billing Portal**: Link to Maxio's hosted billing portal for users to manage payment methods and upgrades
4. **Plan Upgrades/Downgrades**: Add PUT endpoint to change subscription plan
5. **Cancellation**: Add DELETE endpoint to cancel subscriptions
6. **Metered Billing**: Expose the `api-call` metered component for usage-based charges
