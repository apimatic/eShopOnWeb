# Maxio Subscription Billing Integration - Verification Guide

## Overview

This integration adds recurring subscription billing to eShopOnWeb using Maxio Advanced Billing. The implementation includes three REST API endpoints for managing subscriptions:

- **GET /api/subscription-plans** - List available subscription plans
- **POST /api/subscriptions** - Create a new subscription for the authenticated user  
- **GET /api/my-subscriptions** - Get the authenticated user's subscriptions

All endpoints require JWT authentication.

## Architecture

### Components Added

1. **Maxio Client Service** (`Infrastructure/Services/MaxioClient.cs`)
   - Handles HTTP communication with Maxio API
   - Implements basic authentication using API key
   - Automatically maps JSON responses using snake_case naming

2. **Maxio Subscription Service** (`Infrastructure/Services/MaxioSubscriptionService.cs`)
   - High-level API for subscription operations
   - Provides idempotent customer creation (checks for existing customer by reference)
   - Manages product lookups and subscription creation

3. **Subscription Endpoints** (`PublicApi/SubscriptionEndpoints/`)
   - `ListSubscriptionPlansEndpoint.cs` - Fetches available plans from Maxio
   - `CreateSubscriptionEndpoint.cs` - Creates subscriptions with automatic customer management
   - `ListUserSubscriptionsEndpoint.cs` - Retrieves user's subscriptions

### Configuration

Maxio settings are loaded from environment variables and configuration:

| Configuration Key | Environment Variable | Purpose |
|-------------------|---------------------|---------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Sandbox API key for authentication |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Maxio sandbox subdomain (e.g., "cp-exp-4") |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family handle (e.g., "eshop-subscribe") |
| `Maxio:ProductFamilyId` | - | Numeric product family ID (3023074) |
| `Maxio:BaseUrl` | - | Optional: Custom API base URL |

## Setup Instructions

### 1. Environment Variables

Set the following environment variables before running the application:

```powershell
# PowerShell
$env:MAXIO_API_KEY = "your-sandbox-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-4"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

Or for a single run:
```bash
MAXIO_API_KEY=your-api-key MAXIO_SITE_SUBDOMAIN=cp-exp-4 MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe dotnet run
```

### 2. Database Configuration

Since LocalDB is not available on this machine:

```powershell
$env:UseOnlyInMemoryDatabase = "true"
```

This uses an in-memory database that persists only during a single application run.

### 3. HTTPS Configuration

Ensure the dev certificate is trusted:

```powershell
dotnet dev-certs https --check
```

If not trusted, install it:

```powershell
dotnet dev-certs https --trust
```

## Running the Application

### Start the PublicApi Service

```powershell
cd C:\claude-runs\t1h45ali-openapi-haiku45high-015\repo\src\PublicApi

# Set environment variables
$env:MAXIO_API_KEY = "your-sandbox-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-4"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"

# Run the service
dotnet run --configuration Debug
```

The service will start on `https://localhost:25523` (see `Properties/launchSettings.json` for port configuration).

## Verification Steps

### Step 1: Get an Authentication Token

Create a new user or use an existing one. The `CatalogContextSeed.cs` seeds default users.

**Request:**
```powershell
$auth = @{
    username = "demouser@microsoft.com"
    password = "Pass@word1"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:25523/api/authenticate" `
    -Method POST `
    -ContentType "application/json" `
    -Body $auth `
    -SkipCertificateCheck

$token = ($response.Content | ConvertFrom-Json).token
Write-Output "Token: $token"
```

### Step 2: List Available Subscription Plans

**Request:**
```powershell
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$response = Invoke-WebRequest -Uri "https://localhost:25523/api/subscription-plans" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

$plans = $response.Content | ConvertFrom-Json
Write-Output "Available Plans:"
$plans.plans | ForEach-Object {
    Write-Output "  - $($_.name) ($($_.handle)): `$$($_.price)/month"
}
```

**Expected Response:**
```json
{
  "correlationId": "...",
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional plan for power users",
      "price": 299.00,
      "billingInterval": "Every 1 month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic plan for getting started",
      "price": 29.00,
      "billingInterval": "Every 1 month"
    }
  ]
}
```

### Step 3: Create a Subscription

**Request:**
```powershell
$subscriptionRequest = @{
    productHandle = "eshop-pro"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:25523/api/subscriptions" `
    -Method POST `
    -Headers $headers `
    -Body $subscriptionRequest `
    -SkipCertificateCheck

$subscription = $response.Content | ConvertFrom-Json
Write-Output "Subscription Created:"
Write-Output "  ID: $($subscription.subscription.id)"
Write-Output "  State: $($subscription.subscription.state)"
Write-Output "  Product: $($subscription.subscription.productHandle)"
Write-Output "  Next Billing: $($subscription.subscription.nextAssessmentAt)"
```

**Expected Response:**
```json
{
  "correlationId": "...",
  "subscription": {
    "id": 12345678,
    "customerId": 87654321,
    "productId": 7126957,
    "productHandle": "eshop-pro",
    "state": "active",
    "currentPeriodEndsAt": "2024-10-06T12:00:00Z",
    "nextAssessmentAt": "2024-10-06T12:00:00Z",
    "activatedAt": "2024-09-06T12:00:00Z",
    "createdAt": "2024-09-06T12:00:00Z"
  }
}
```

### Step 4: List User's Subscriptions

**Request:**
```powershell
$response = Invoke-WebRequest -Uri "https://localhost:25523/api/my-subscriptions" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

$userSubscriptions = $response.Content | ConvertFrom-Json
Write-Output "Your Subscriptions:"
$userSubscriptions.subscriptions | ForEach-Object {
    Write-Output "  - $($_.productHandle) (ID: $($_.id), State: $($_.state))"
    Write-Output "    Next Billing: $($_.nextAssessmentAt)"
}
```

**Expected Response:**
```json
{
  "correlationId": "...",
  "subscriptions": [
    {
      "id": 12345678,
      "customerId": 87654321,
      "productId": 7126957,
      "productHandle": "eshop-pro",
      "state": "active",
      "currentPeriodEndsAt": "2024-10-06T12:00:00Z",
      "nextAssessmentAt": "2024-10-06T12:00:00Z",
      "activatedAt": "2024-09-06T12:00:00Z",
      "createdAt": "2024-09-06T12:00:00Z"
    }
  ]
}
```

## Key Features

### Idempotent Customer Creation

When a subscription is created, the system:
1. Checks if a Maxio customer exists for the user (using userId as reference)
2. If exists, uses the existing customer
3. If not exists, creates a new customer with email, name, and userId reference
4. This ensures double-clicks never create duplicate customers or subscriptions

### JWT Authentication

All subscription endpoints require a valid JWT bearer token:
```
Authorization: Bearer <token>
```

The token is obtained from the `/api/authenticate` endpoint and includes the user's identity information.

### Error Handling

The endpoints provide meaningful error messages:
- Missing or invalid authentication → 401 Unauthorized
- Missing product handle → 400 Bad Request with error message
- Maxio API failures → 400 Bad Request with error details

## Architecture Decisions

### Why Not Replace Cart/Checkout?

This is an additive integration, not a replacement. Customers can still:
- Use the existing one-time purchase flow via basket/cart/checkout
- Subscribe to plans independently
- Both capabilities coexist

### Why Remittance Payment Collection?

Per the Maxio configuration, subscription plans are set to "payment method not required" with `remittance` as the payment collection method. This allows subscribing without capturing payment information immediately.

### Configuration via Environment Variables

Using environment variables and the configuration system allows:
- Different credentials for different environments
- Secrets never stored in the repository
- Easy CI/CD integration
- Runtime flexibility

## Troubleshooting

### Issue: "Maxio product family ID not configured"
**Solution:** Ensure `Maxio:ProductFamilyId` is set to `3023074` in configuration or via environment mapping.

### Issue: "Failed to create or retrieve customer from Maxio"
**Solution:** Verify the Maxio API key is correct and has sufficient permissions. Check Maxio sandbox site is accessible.

### Issue: HTTPS Certificate Errors
**Solution:** Run `dotnet dev-certs https --trust` to trust the development certificate.

### Issue: Database errors
**Solution:** Ensure `UseOnlyInMemoryDatabase=true` is set, or provide valid LocalDB connection strings.

## Next Steps for Production

1. **Payment Collection:** When ready to collect payments, update subscription creation to include payment profiles
2. **Webhook Integration:** Add Maxio webhooks to sync subscription state changes
3. **User Dashboard:** Build a UI for users to manage subscriptions
4. **Analytics:** Track subscription metrics in your business intelligence system
5. **Billing Portal:** Consider using Maxio's hosted billing portal for self-service management

## Code Structure

```
src/
├── ApplicationCore/
│   └── MaxioSettings.cs                    # Configuration model
├── Infrastructure/
│   └── Services/
│       ├── MaxioClient.cs                  # HTTP client for Maxio API
│       └── (MaxioClient defines all DTOs)
├── PublicApi/
│   ├── SubscriptionEndpoints/
│   │   ├── ListSubscriptionPlansEndpoint.cs
│   │   ├── CreateSubscriptionEndpoint.cs
│   │   ├── ListUserSubscriptionsEndpoint.cs
│   │   ├── SubscriptionPlanDto.cs
│   │   └── SubscriptionDto.cs
│   ├── Program.cs                          # DI registration
│   └── appsettings.json                    # Configuration
```

## References

- Maxio API Spec: `maxio-spec/openapi.yaml`
- Sandbox Plans:
  - Pro Plan (eshop-pro): $299/month
  - Basic Plan (basic-plan): $29/month
- Demo Credentials: `demouser@microsoft.com` / `Pass@word1`
