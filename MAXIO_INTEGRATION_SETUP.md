# Maxio Advanced Billing Integration - Setup & Verification Guide

## Overview

This document provides step-by-step instructions to set up and verify the Maxio subscription billing integration in eShopOnWeb's PublicApi.

The integration enables recurring-subscription capabilities alongside the existing one-time purchase flow. All subscription data is managed in Maxio Advanced Billing (sandbox environment).

## Prerequisites

### Environment Setup

1. **.NET SDK**: Ensure .NET 10 SDK is installed (the project rolls forward from 8.0 pinning)
   ```powershell
   dotnet --version  # Should show 10.x.x
   ```

2. **Environment Variables** - Required Maxio credentials must be available:
   ```powershell
   $env:MAXIO_API_KEY                  # API authentication key
   $env:MAXIO_SITE_SUBDOMAIN           # Maxio sandbox subdomain (e.g., "cp-exp-4")
   $env:MAXIO_DEFAULT_PRODUCT_FAMILY   # Product family handle (e.g., "eshop-subscribe")
   ```

   Verify they're set:
   ```powershell
   $env:MAXIO_API_KEY; $env:MAXIO_SITE_SUBDOMAIN; $env:MAXIO_DEFAULT_PRODUCT_FAMILY
   ```

3. **Database**: No SQL Server required - uses in-memory database for development
   ```powershell
   $env:UseOnlyInMemoryDatabase = 'true'
   ```

## Setup Steps

### 1. Configure User Secrets

Store Maxio credentials securely in .NET user-secrets (development only):

```powershell
cd src/PublicApi

# Set API key
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY

# Set Maxio subdomain
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN

# Set product family handle
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY
```

Verify secrets are set:
```powershell
dotnet user-secrets list
```

### 2. Build the Project

```powershell
cd C:\repo
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet build src/PublicApi/PublicApi.csproj -c Debug
```

### 3. Run the PublicApi Service

```powershell
cd src/PublicApi
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:UseOnlyInMemoryDatabase = 'true'
$env:ASPNETCORE_ENVIRONMENT = 'Development'

dotnet run --configuration Debug --launch-profile PublicApi
```

The API will be available at:
- HTTPS: `https://localhost:25283`
- HTTP: `http://localhost:25284`
- Swagger: `https://localhost:25283/swagger`

## Testing the Integration

### Test 1: List Available Subscription Plans

**Endpoint**: `GET /api/subscription-plans`

This endpoint is public (no authentication required):

```powershell
$response = Invoke-WebRequest -Uri "https://localhost:25283/api/subscription-plans" `
  -SkipCertificateCheck

$response.StatusCode  # Should be 200
$response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 5
```

**Expected Response** (from Maxio sandbox seeded data):
```json
{
  "plans": [
    {
      "id": 7131000,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "price": 29.00
    },
    {
      "id": 7130999,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "price": 299.00
    }
  ],
  "errorMessage": null
}
```

### Test 2: Authenticate User

**Endpoint**: `POST /api/authenticate`

Get a JWT token for authenticated requests:

```powershell
$authResponse = Invoke-WebRequest -Uri "https://localhost:25283/api/authenticate" `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"username": "demouser@microsoft.com", "password": "Pass@word1"}' `
  -SkipCertificateCheck

$token = ($authResponse.Content | ConvertFrom-Json).token
"Token: $($token.Substring(0, 50))..."
```

**Credentials** (seeded in database):
- Email: `demouser@microsoft.com`
- Password: `Pass@word1`

### Test 3: Create a Subscription

**Endpoint**: `POST /api/subscriptions`

Create a subscription for the authenticated user:

```powershell
$headers = @{ "Authorization" = "Bearer $token" }

$subResponse = Invoke-WebRequest -Uri "https://localhost:25283/api/subscriptions" `
  -Method Post `
  -ContentType "application/json" `
  -Body '{"productHandle": "eshop-pro"}' `
  -Headers $headers `
  -SkipCertificateCheck

$subResponse.StatusCode  # Should be 200
$subResponse.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

**Expected Response**:
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "currentPeriodStartedAt": "2026-09-06T10:30:00Z",
  "currentPeriodEndsAt": "2026-10-06T10:30:00Z",
  "nextAssessmentAt": "2026-10-06T10:30:00Z",
  "success": true,
  "errorMessage": null
}
```

### Test 4: Retrieve User's Subscriptions

**Endpoint**: `GET /api/my-subscriptions`

Get all subscriptions for the authenticated user:

```powershell
$headers = @{ "Authorization" = "Bearer $token" }

$mySubsResponse = Invoke-WebRequest -Uri "https://localhost:25283/api/my-subscriptions" `
  -Headers $headers `
  -SkipCertificateCheck

$mySubsResponse.StatusCode  # Should be 200
$mySubsResponse.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

**Expected Response**:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "currentPeriodStartedAt": "2026-09-06T10:30:00Z",
      "currentPeriodEndsAt": "2026-10-06T10:30:00Z",
      "nextAssessmentAt": "2026-10-06T10:30:00Z",
      "balanceInCents": 0,
      "activatedAt": "2026-09-06T10:30:00Z",
      "createdAt": "2026-09-06T10:30:00Z"
    }
  ],
  "errorMessage": null
}
```

## Key Features Implemented

### 1. Idempotent Customer Creation
- Uses the user identifier as the `reference` field in Maxio
- Subsequent calls with the same userId return the existing customer
- Prevents duplicate customer records

### 2. Atomic Subscription Creation
- Creates customer and subscription in a single flow
- If customer already exists (by reference), uses the existing customer
- Minimal API calls to Maxio

### 3. HTTP-Based Integration
- Uses standard .NET HttpClient for Maxio API communication
- Basic Authentication with API key
- Direct JSON parsing (no external SDK dependency required for MVP)

### 4. JWT Authentication
- Integrates with existing eShopOnWeb JWT authentication
- User identity flows from HTTP Bearer token to Maxio customer reference
- Protected endpoints require valid JWT

### 5. Configuration Management
- Settings loaded from:
  1. User secrets (development)
  2. Environment variables
  3. appsettings.json

## Troubleshooting

### Issue: "Maxio configuration (ApiKey and Subdomain) is required"

**Solution**: Ensure user secrets are set correctly:
```powershell
cd src/PublicApi
dotnet user-secrets list
```

If empty, re-run the user-secrets set commands from Setup Step 1.

### Issue: HTTP 404 on subscription endpoints

**Solution**: Ensure the app is running and check Swagger:
```powershell
curl -k https://localhost:25283/swagger/v1/swagger.json
```

Look for `/api/subscriptions` and `/api/subscription-plans` in the output.

### Issue: HTTP 400 or 422 from Maxio API

**Cause**: Invalid request to Maxio API

**Solutions**:
1. Verify the product handle exists in Maxio
2. Check that the subscription plan is active in Maxio sandbox
3. Verify Maxio credentials are correct

### Issue: JWT Token claims not populated

**Note**: The current implementation uses `ClaimTypes.Name` from the JWT token (set to username).

## Architecture

### Endpoints Structure
```
src/PublicApi/
├── SubscriptionEndpoints/
│   ├── ListSubscriptionPlansEndpoint.cs      # GET /api/subscription-plans
│   ├── CreateSubscriptionEndpoint.cs         # POST /api/subscriptions
│   ├── MySubscriptionsEndpoint.cs            # GET /api/my-subscriptions
│   └── SubscriptionPlanDto.cs                # Response DTOs
├── Services/
│   └── MaxioSubscriptionService.cs           # Maxio API integration
├── MaxioConfiguration.cs                      # Configuration model
├── Program.cs                                 # Dependency injection setup
├── appsettings.json                          # Configuration with empty Maxio section
└── PublicApi.csproj                          # Project file with Maxio SDK reference
```

### Data Flow

1. **User authenticates** → Gets JWT token with username claim
2. **User requests subscription plans** → Fetches from Maxio, cached/transformed
3. **User creates subscription** → 
   - Extracts user ID from JWT
   - Creates/retrieves Maxio customer (using user ID as reference)
   - Creates subscription on that customer
   - Returns subscription details
4. **User queries subscriptions** → Lists all subscriptions for that customer

## Dependencies

- `Maxio.AdvancedBillingSdk` (v10.0.0) - Installed but not currently used; direct HTTP calls used instead
- Existing eShopOnWeb dependencies (Identity, Entity Framework, minimal APIs)

## Environment Variables Reference

| Variable | Example | Purpose |
|----------|---------|---------|
| `MAXIO_API_KEY` | `JYBHrFCa25GHKetVizgnPUoif33pQZslQiItKzilE` | API key for Maxio authentication |
| `MAXIO_SITE_SUBDOMAIN` | `cp-exp-4` | Maxio sandbox subdomain |
| `MAXIO_DEFAULT_PRODUCT_FAMILY` | `eshop-subscribe` | Product family containing subscription plans |
| `MAXIO_ENVIRONMENT` | `US` | Maxio environment (optional, defaults to US) |
| `UseOnlyInMemoryDatabase` | `true` | Use in-memory DB instead of SQL Server |
| `ASPNETCORE_ENVIRONMENT` | `Development` | ASP.NET Core environment |
| `DOTNET_ROLL_FORWARD` | `Major` | Allow .NET 10 SDK to be used |

## Sandbox Data Available

The Maxio sandbox (site `cp-exp-4`) is pre-seeded with:

| Entity | Handle | Price | Notes |
|--------|--------|-------|-------|
| Product Family | `eshop-subscribe` | - | Contains all subscription plans |
| Pro Plan | `eshop-pro` | $299.00/month | Full-featured plan |
| Basic Plan | `basic-plan` | $29.00/month | Starter plan |
| API Call Component | `api-call` | $0.01/unit | Metered usage component |

All plans:
- No trial period
- No setup fee
- Never expire
- Not taxable
- **Payment method not required** (allows testing without card entry)

## Production Considerations

- Store Maxio API key in secure vault (Azure Key Vault, AWS Secrets Manager, etc.)
- Implement webhook handlers for Maxio events (subscription renewed, failed payment, etc.)
- Add comprehensive error handling and logging
- Implement subscription cancellation endpoint
- Add usage/metering tracking for components
- Consider caching subscription plans (they change infrequently)
- Add idempotency keys to subscription creation requests
- Implement retry logic with exponential backoff for Maxio API calls
