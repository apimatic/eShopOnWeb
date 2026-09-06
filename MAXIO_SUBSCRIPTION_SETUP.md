# Maxio Subscription Billing Integration - Setup & Verification Guide

## Overview

This guide explains how to set up and test the Maxio subscription billing integration added to eShopOnWeb. The integration provides three endpoints for managing recurring subscriptions:

- `GET /api/subscription-plans` - List available plans
- `POST /api/subscriptions` - Subscribe to a plan
- `GET /api/my-subscriptions` - Get user's active subscriptions

## Prerequisites

- .NET SDK (10.0 or higher, due to global.json rollForward setting)
- Maxio Billing API sandbox credentials
- Access to the Maxio sandbox site (cp-exp-2)

## 1. Configure Credentials

### Using User Secrets (Recommended for Local Development)

Since the repository is published, secrets must be stored using .NET User Secrets, not in config files.

```powershell
# Set environment variable for SDK compatibility
$env:DOTNET_ROLL_FORWARD='Major'

# Set up user secrets for the PublicApi project
cd src/PublicApi

dotnet user-secrets init

# Add Maxio sandbox credentials from environment or your Maxio account
# Replace values with actual credentials from cp-exp-2 sandbox
dotnet user-secrets set "Maxio:ApiKey" "your_api_key_here"
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain_here"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:Environment" "sandbox"
# BaseUrl is optional - leave empty to use default {subdomain}.chargify.com
dotnet user-secrets set "Maxio:BaseUrl" ""

cd ../..
```

### Environment Variables (Alternative)

Set environment variables before running:

```powershell
$env:MAXIO_API_KEY = "your_api_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

## 2. Database Configuration

The application supports both in-memory and SQL Server databases:

### Using In-Memory Database (Default for Testing)

```powershell
$env:UseOnlyInMemoryDatabase = "true"
```

**Note:** In-memory database loses all data on restart. The subscription userId ↔ subscription mapping only survives within a single run.

### Using SQL Server LocalDB

If LocalDB is installed and running:

```powershell
$env:UseOnlyInMemoryDatabase = "false"
# Ensure connection strings in appsettings.json or user secrets are correct
```

## 3. HTTPS Certificate Setup

Both PublicApi and Web use HTTPS redirection. Ensure the dev certificate is trusted:

```powershell
dotnet dev-certs https --check
# If not trusted:
dotnet dev-certs https --trust
```

## 4. Run the Application

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'

# Navigate to PublicApi directory
cd src/PublicApi

# Run the application
dotnet run

# The API will be available at https://localhost:25163
# Swagger UI at: https://localhost:25163/swagger/ui
```

## 5. Obtain Authentication Token

All subscription endpoints (POST /api/subscriptions, GET /api/my-subscriptions) require JWT authentication.

### Step 1: Authenticate to Get Token

```powershell
$auth = @{
    Username = "demouser@microsoft.com"
    Password = "Pass@word123"
} | ConvertTo-Json

$response = Invoke-WebRequest `
    -Uri "https://localhost:25163/api/authenticate" `
    -Method POST `
    -ContentType "application/json" `
    -Body $auth `
    -SkipCertificateCheck

$token = ($response.Content | ConvertFrom-Json).token
Write-Host "Token: $token"
```

## 6. Test the Integration

### 6.1 Get Available Plans

```powershell
$headers = @{
    "Content-Type" = "application/json"
}

$response = Invoke-WebRequest `
    -Uri "https://localhost:25163/api/subscription-plans" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json)
```

**Expected Response:**
```json
{
  "correlationId": "...",
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan",
      "priceInCents": 29900,
      "priceFormatted": "$299.00"
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic plan",
      "priceInCents": 2900,
      "priceFormatted": "$29.00"
    }
  ]
}
```

### 6.2 Subscribe to a Plan

```powershell
$body = @{
    planHandle = "eshop-pro"
} | ConvertTo-Json

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$response = Invoke-WebRequest `
    -Uri "https://localhost:25163/api/subscriptions" `
    -Method POST `
    -Headers $headers `
    -Body $body `
    -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json)
```

**Expected Response:**
```json
{
  "correlationId": "...",
  "subscription": {
    "id": 12345678,
    "customerId": 98765432,
    "planHandle": "eshop-pro",
    "planName": "Pro Plan",
    "status": "active",
    "priceInCents": 29900,
    "priceFormatted": "$299.00",
    "currentPeriodStartsAt": "2026-09-06T...",
    "nextBillingAt": "2026-10-06T..."
  }
}
```

### 6.3 Get User's Subscriptions

```powershell
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$response = Invoke-WebRequest `
    -Uri "https://localhost:25163/api/my-subscriptions" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json)
```

**Expected Response:**
```json
{
  "correlationId": "...",
  "subscriptions": [
    {
      "id": 12345678,
      "customerId": 98765432,
      "planHandle": "eshop-pro",
      "planName": "Pro Plan",
      "status": "active",
      "priceInCents": 29900,
      "priceFormatted": "$299.00",
      "currentPeriodStartsAt": "2026-09-06T...",
      "nextBillingAt": "2026-10-06T..."
    }
  ]
}
```

## 7. Implementation Details

### Architecture

The integration follows these patterns:

1. **Entities** (`ApplicationCore/Entities/BuyerAggregate/Subscription.cs`)
   - Stores subscription metadata linked to users

2. **Services** (`Infrastructure/Services/SubscriptionService.cs`)
   - Orchestrates Maxio API calls with local database persistence
   - Handles subscription creation with idempotent customer creation

3. **Maxio Client** (`Infrastructure/Maxio/MaxioApiClient.cs`)
   - HTTP client wrapping Maxio REST API
   - Uses Basic Auth with API Key
   - Parses responses and handles errors

4. **Endpoints** (`PublicApi/SubscriptionEndpoints/`)
   - Minimal API endpoints using JWT authentication
   - Extracts user identity from JWT claims
   - Returns structured DTOs

### Configuration

Configuration flows from environment variables → user secrets → appsettings.json:

```
Environment Variables
       ↓
   User Secrets
       ↓
 appsettings.json (fallback)
       ↓
 MaxioSettings (bound config)
```

### Database

The `Subscription` entity is persisted locally to map eShopOnWeb users to Maxio customers:

- **BuyerId**: Reference to eShopOnWeb Buyer aggregate (for future enhancement)
- **IdentityId**: ApplicationUser ID for user lookup
- **MaxioSubscriptionId**: Maxio subscription ID (immutable)
- **MaxioCustomerId**: Maxio customer ID (immutable)
- **PlanHandle, PlanName**: Product information
- **Status**: Current subscription state (active, pending, canceled, etc.)
- **Billing dates**: Current period and next billing date

## 8. Troubleshooting

### "Maxio product family handle is not configured"
- Verify `Maxio:ProductFamilyHandle` is set to "eshop-subscribe"
- Check user secrets: `dotnet user-secrets list`

### "Unauthorized" on protected endpoints
- Ensure authentication token is passed: `Authorization: Bearer {token}`
- Verify token hasn't expired (valid for 7 days from issue)
- Check that authenticate endpoint works first

### "Payment method required"
- The sandbox plans are configured with `payment_method_not_required: true`
- If seeing this error, the configuration on cp-exp-2 may have changed

### HTTPS certificate errors
- Run: `dotnet dev-certs https --trust`
- Or use `-SkipCertificateCheck` in test scripts

### "Connection refused" errors
- Ensure PublicApi is running: `dotnet run` in src/PublicApi
- Check port 25163 is available
- Verify firewall isn't blocking localhost connections

## 9. Production Considerations

When deploying to production:

1. **Use environment variables** or secure configuration providers (Azure Key Vault, AWS Secrets Manager)
   - Never store secrets in code or appsettings.json files

2. **Enable payment methods** for production
   - The sandbox plans don't require credit cards
   - Production plans should have proper payment processing

3. **Database migration**
   - Run `dotnet ef database update -c CatalogContext` before first startup
   - Or let EF Core create the schema with context.Database.EnsureCreated()

4. **Idempotency**
   - Customer creation is idempotent via `customer_reference`
   - Double-clicking subscribe won't create duplicate customers/subscriptions

5. **Error handling**
   - Implement retry logic for network failures
   - Handle 3D Secure (3DS) authentication flows if required
   - Monitor Maxio webhook events for subscription state changes

6. **Logging**
   - The Maxio client logs errors via ILogger
   - Monitor logs for API authentication failures or rate limits

## 10. API Reference

### Maxio Integration Points

- **List Plans**: `GET /product_families/handle:{family}/products.json`
- **Get/Create Customer**: `GET /customers/lookup.json?reference={ref}` / `POST /customers.json`
- **Create Subscription**: `POST /subscriptions.json`
- **Get Subscription**: `GET /subscriptions/{id}.json`
- **List Customer Subscriptions**: `GET /customers/{id}/subscriptions.json`

All Maxio API calls use HTTP Basic Auth: `Authorization: Basic base64({apiKey}:X)`

For full Maxio API documentation, see the embedded maxio-docs MCP server or https://docs.maxio.com/
