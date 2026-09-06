# Maxio Subscription Billing Integration - Setup & Verification Guide

## Overview

This guide walks through setting up and verifying the Maxio Advanced Billing subscription integration for eShopOnWeb. The integration adds recurring subscription billing alongside the existing one-time commerce flow.

## Architecture Summary

- **Endpoints**: Three new HTTP endpoints under `/api/` in PublicApi project
  - `GET /api/subscription-plans` - List available subscription plans
  - `POST /api/subscriptions` - Create a new subscription for the authenticated user
  - `GET /api/my-subscriptions` - List subscriptions for the authenticated user
- **Authentication**: JWT Bearer tokens (same as other PublicApi endpoints)
- **Database**: New `SubscriptionCustomer` entity maps eShopOnWeb users to Maxio customers
- **Billing System**: Maxio Advanced Billing (sandbox)

## Prerequisites

1. **Environment Variables**: Maxio sandbox credentials
   - `MAXIO_API_KEY`: Your Maxio API key
   - `MAXIO_SITE_SUBDOMAIN`: Your Maxio sandbox subdomain
   - `MAXIO_ENVIRONMENT`: Environment type (usually "sandbox")
   - `MAXIO_DEFAULT_PRODUCT_FAMILY`: Product family handle (e.g., "eshop-subscribe")

2. **.NET 8.0 SDK/Runtime**: Solution uses .NET 8.0
3. **Database**: In-memory or SQL Server LocalDB for this guide

## Setup Steps

### Step 1: Load Maxio Credentials into User Secrets

The integration reads Maxio credentials from appsettings configuration, which can be overridden by environment variables. To use user-secrets:

```bash
# From the PublicApi project directory
dotnet user-secrets set "Maxio:ApiKey" "your_api_key_here"
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:BaseUrl" ""  # Leave empty to derive from subdomain
```

Or set environment variables:
```bash
$env:Maxio:ApiKey = "your_api_key_here"
$env:Maxio:Subdomain = "your_subdomain"
$env:Maxio:ProductFamilyHandle = "eshop-subscribe"
```

### Step 2: Configure Database

For development with in-memory database:
```bash
$env:UseOnlyInMemoryDatabase = "true"
```

For SQL Server LocalDB (if installed):
```bash
$env:UseOnlyInMemoryDatabase = "false"
# Connection strings in appsettings.json will be used
```

### Step 3: Run Database Migration

The `SubscriptionCustomer` entity is included in the existing EF migration:

```bash
dotnet ef database update --context CatalogContext -s src/PublicApi/PublicApi.csproj
```

Or use in-memory database (no migration needed).

### Step 4: Start the PublicApi Service

```bash
cd src/PublicApi
dotnet run
# Service should be available at https://localhost:25583 (or configured port)
```

## Testing the Integration

### Test 1: Authenticate a User

First, get a JWT token:

```bash
$loginResponse = Invoke-RestMethod -Uri "https://localhost:25583/api/authenticate" -Method Post `
  -Headers @{ "Content-Type" = "application/json" } `
  -Body @{ username = "demouser@example.com"; password = "DemoPassword123!" } | ConvertTo-Json

$token = $loginResponse.token
```

Or use curl:
```bash
curl -X POST https://localhost:25583/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@example.com","password":"DemoPassword123!"}'
```

**Note:** The credentials must be a user already registered in the eShopOnWeb identity database.

### Test 2: List Available Subscription Plans

```bash
$headers = @{ "Authorization" = "Bearer $token" }
Invoke-RestMethod -Uri "https://localhost:25583/api/subscription-plans" `
  -Method Get -Headers $headers | ConvertTo-Json
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00,
      "pricingScheme": "per_unit",
      "trialDays": null
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00,
      "pricingScheme": "per_unit",
      "trialDays": null
    }
  ],
  "correlationId": "..."
}
```

### Test 3: Subscribe to a Plan

```bash
$body = @{
  productHandle = "eshop-pro"
} | ConvertTo-Json

$subscriptionResponse = Invoke-RestMethod -Uri "https://localhost:25583/api/subscriptions" `
  -Method Post -Headers $headers `
  -ContentType "application/json" `
  -Body $body

$subscriptionResponse | ConvertTo-Json
```

Expected response:
```json
{
  "success": true,
  "subscriptionId": 12345678,
  "state": "active",
  "currentPeriodStartsAt": "2026-09-06T00:00:00",
  "currentPeriodEndsAt": "2026-10-06T00:00:00",
  "nextBillingDate": "2026-10-06T00:00:00",
  "correlationId": "..."
}
```

### Test 4: View User's Subscriptions

```bash
Invoke-RestMethod -Uri "https://localhost:25583/api/my-subscriptions" `
  -Method Get -Headers $headers | ConvertTo-Json
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "productId": 7126957,
      "customerId": 98765432,
      "state": "active",
      "currentPeriodStartsAt": "2026-09-06T00:00:00",
      "currentPeriodEndsAt": "2026-10-06T00:00:00",
      "nextAssessmentAt": "2026-10-06T00:00:00"
    }
  ],
  "correlationId": "..."
}
```

### Test 5: Idempotency Check

Subscribe to the same plan again (with a different user or after waiting):
- The system will create a new Maxio customer only on the first subscription
- Subsequent subscriptions for the same user will reuse the existing Maxio customer
- Multiple subscriptions to different plans for the same user are supported

## Key Implementation Details

### Service Layer (MaxioService)

- **GetSubscriptionPlansAsync()**: Fetches products from Maxio filtered by `ProductFamilyHandle`
- **GetOrCreateMaxioCustomerAsync()**: Creates or retrieves a Maxio customer using the user's email and ID
- **CreateSubscriptionAsync()**: Creates a subscription to a product, automatically links to the customer
- **GetCustomerSubscriptionsAsync()**: Lists all subscriptions for a Maxio customer

All methods include error handling and logging via `IAppLogger<MaxioService>`.

### Database Mapping

The `SubscriptionCustomer` entity maintains a 1:1 mapping:
- `UserId` (eShopOnWeb ApplicationUser ID) → `MaxioCustomerId` (Maxio customer ID)
- Ensures idempotent customer creation
- Supports looking up subscriptions by either identifier

### Endpoints

All endpoints require JWT authentication via `[Authorize]` attribute. The user ID is extracted from the JWT token's `ClaimTypes.NameIdentifier` claim.

## Troubleshooting

### "Product with handle not found"
- Verify the product family handle matches the Maxio configuration
- Check that the handle exists in the Maxio sandbox environment
- Ensure the subscription plan is part of the correct product family

### "User not authenticated"
- Verify JWT token is valid and includes the NameIdentifier claim
- Check that the Authorization header is properly formatted: `Bearer <token>`

### "Failed to create Maxio customer" (HTTP 422)
- Email address may already exist in Maxio
- The integration creates customers with reference = userId to prevent duplicates
- Check Maxio API response for specific validation errors

### In-Memory Database Loses Data
- The in-memory database is reset on application restart
- SubscriptionCustomer mappings are NOT persisted
- For testing, use SQL Server LocalDB or configure persistence appropriately

## Production Considerations

1. **Secrets Management**: Store API keys in secure vaults (Azure Key Vault, AWS Secrets Manager)
2. **Error Handling**: Consider retry logic for transient API failures
3. **Logging**: Current implementation uses `IAppLogger<T>` - extend with telemetry/monitoring
4. **Rate Limiting**: Maxio has API rate limits - implement request throttling if needed
5. **Webhook Handling**: Implement Maxio webhooks for subscription state changes (not in MVP)
6. **Idempotency Keys**: Consider adding idempotency key support for subscription creation

## Files Modified/Created

### New Files
- `src/ApplicationCore/Constants/MaxioSettings.cs` - Configuration holder
- `src/ApplicationCore/Entities/SubscriptionCustomer.cs` - Domain entity
- `src/ApplicationCore/Interfaces/IMaxioService.cs` - Service interface
- `src/ApplicationCore/Interfaces/ISubscriptionCustomerService.cs` - Repository interface
- `src/Infrastructure/Services/MaxioService.cs` - Maxio API integration
- `src/Infrastructure/Services/SubscriptionCustomerService.cs` - Repository service
- `src/Infrastructure/Data/Config/SubscriptionCustomerConfiguration.cs` - EF entity mapping
- `src/PublicApi/SubscriptionEndpoints/*.cs` - Three new HTTP endpoints
- `src/Infrastructure/Data/Migrations/20260906024309_AddSubscriptionCustomer.cs` - Database migration

### Modified Files
- `src/Infrastructure/Dependencies.cs` - DI registrations
- `src/Infrastructure/Data/CatalogContext.cs` - Added SubscriptionCustomers DbSet
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/PublicApi/Program.cs` - Added HttpContextAccessor and HttpClient registration

## References

- **Maxio Advanced Billing API**: https://developers.maxio.com/
- **Maxio Developer Portal**: https://developers.maxio.com/
- List Products: https://developers.maxio.com/http/advanced-billing-api/api-endpoints/products/list-products
- Create Customer: https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/create-customer
- Create Subscription: https://developers.maxio.com/http/advanced-billing-api/api-endpoints/subscriptions/create-subscription
- List Customer Subscriptions: https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/list-customer-subscriptions
