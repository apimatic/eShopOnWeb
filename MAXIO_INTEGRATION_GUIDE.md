# Maxio Subscription Billing Integration Guide

## Overview
This guide provides step-by-step instructions for testing and verifying the Maxio subscription billing integration in eShopOnWeb.

## Integration Summary

The integration adds recurring-subscription billing capability to eShopOnWeb using **Maxio Advanced Billing** as the billing system of record. This is an **additive capability** that does not replace the existing cart/checkout flow.

### Architecture
- **Service Layer**: `MaxioBillingService` (ApplicationCore) - handles all Maxio API interactions
- **Data Model**: `MaxioSubscription` entity - tracks user-subscription mappings locally
- **API Endpoints**: Three REST endpoints under `/api/` in PublicApi
  - `GET /api/subscription-plans` - List available subscription plans
  - `POST /api/subscriptions` - Create a subscription for the current user
  - `GET /api/my-subscriptions` - Retrieve current user's subscriptions

## Prerequisites

### Environment Setup

1. **Configure .NET SDK**
   ```powershell
   $env:DOTNET_ROLL_FORWARD = "Major"
   ```

2. **Set Maxio Credentials via Environment Variables**

   You must set these environment variables before running the application. These should reference your Maxio sandbox site (cp-exp-4).

   ```powershell
   $env:MAXIO_API_KEY = "your-maxio-api-key"
   $env:MAXIO_SITE_SUBDOMAIN = "your-site-subdomain"
   $env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
   ```

   Or set them in your system environment or in a `.env` file that you source before running.

   **IMPORTANT**: These credentials are NEVER committed to the repository. They are environment variables only.

3. **Enable In-Memory Database (for local testing)**
   ```powershell
   $env:UseOnlyInMemoryDatabase = "true"
   ```

   This allows testing without SQL Server LocalDB. Note: data does not persist across restarts with in-memory database.

## Building the Application

```powershell
cd "C:\claude-runs\t1h45ali-none-haiku45high-020\repo\src\PublicApi"
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet build
```

## Running the Application

```powershell
cd "C:\claude-runs\t1h45ali-none-haiku45high-020\repo\src\PublicApi"
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "your-subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"

dotnet run
```

The API will start on:
- HTTPS: `https://localhost:25923`
- HTTP: `http://localhost:25924`

## Verification Steps

### Step 1: Get a Bearer Token (Authentication)

All subscription endpoints except `GET /api/subscription-plans` require JWT authentication.

1. First, authenticate to get a token:
   ```powershell
   $body = @{
       username = "demouser@microsoft.com"
       password = "Pass@word123"
   } | ConvertTo-Json

   $response = Invoke-WebRequest `
       -Uri "https://localhost:25923/api/authenticate" `
       -Method POST `
       -ContentType "application/json" `
       -Body $body `
       -SkipCertificateCheck

   $token = ($response.Content | ConvertFrom-Json).token
   Write-Host "Token: $token"
   ```

2. Save the token for use in subsequent requests:
   ```powershell
   $headers = @{
       Authorization = "Bearer $token"
       "Content-Type" = "application/json"
   }
   ```

### Step 2: List Available Subscription Plans

This endpoint is public and does not require authentication.

```powershell
$response = Invoke-WebRequest `
    -Uri "https://localhost:25923/api/subscription-plans" `
    -Method GET `
    -ContentType "application/json" `
    -SkipCertificateCheck

$plans = $response.Content | ConvertFrom-Json
$plans | ConvertTo-Json

# Expected output includes plans like:
# - "eshop-pro" ($299/month)
# - "eshop-basic" ($29/month)
```

### Step 3: Create a Subscription

This endpoint requires JWT authentication. The request should specify a product handle (e.g., "eshop-pro" or "eshop-basic").

```powershell
$body = @{
    productHandle = "eshop-pro"
} | ConvertTo-Json

$response = Invoke-WebRequest `
    -Uri "https://localhost:25923/api/subscriptions" `
    -Method POST `
    -Headers $headers `
    -Body $body `
    -SkipCertificateCheck

$subscription = $response.Content | ConvertFrom-Json
$subscription | ConvertTo-Json

# Expected response includes:
# {
#   "success": true,
#   "subscriptionId": 123456,
#   "state": "active",
#   "nextBillingAt": "2026-10-06T..."
# }
```

### Step 4: Retrieve User's Subscriptions

```powershell
$response = Invoke-WebRequest `
    -Uri "https://localhost:25923/api/my-subscriptions" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

$mySubscriptions = $response.Content | ConvertFrom-Json
$mySubscriptions | ConvertTo-Json

# Expected response includes array of subscriptions with state, price, dates
```

## Key Implementation Details

### Maxio API Authentication
- **Method**: HTTP Basic Auth
- **Username**: Your API key
- **Password**: Literal string `"x"`
- **Base URL**: `https://{subdomain}.chargify.com`

### Data Flow: Creating a Subscription

1. User calls `POST /api/subscriptions` with product handle
2. Service extracts user ID from JWT claims
3. Service calls Maxio to get-or-create customer:
   - Uses user ID as `reference` (idempotent - won't create duplicate)
   - Pulls first_name, last_name, email from JWT claims
4. Service creates subscription on Maxio:
   - Links customer to product by handle
   - Maxio returns subscription details (ID, state, billing dates, etc.)
5. Service stores subscription locally in database (maps user to Maxio subscription ID)
6. Response includes subscription state and next billing date

### Idempotency
- Customer creation is idempotent via the `reference` field (user ID)
- Calling the subscribe endpoint multiple times with the same user will create new subscriptions (not ideal, but safe for the MVP)

## Troubleshooting

### "Access denied" or 401 errors
- Verify your JWT token is valid and included in the `Authorization: Bearer <token>` header
- Token must come from the `/api/authenticate` endpoint

### "Failed to connect to Maxio" errors
- Verify MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN are set correctly
- Verify you can reach `https://{subdomain}.chargify.com` from your network
- Check that your API key has the necessary permissions

### "The type or namespace name" compilation errors
- Ensure you're building with `$env:DOTNET_ROLL_FORWARD = "Major"` set
- Run `dotnet clean` and rebuild

### Port already in use
- Port 25923 is already in use. Stop other services or change the port in `launchSettings.json`

## Database Schema

### MaxioSubscription Entity
- `Id` (int): Primary key
- `UserId` (string): eShopOnWeb user ID
- `MaxioCustomerId` (long): Customer ID from Maxio
- `MaxioSubscriptionId` (long): Subscription ID from Maxio
- `ProductHandle` (string): Product handle (e.g., "eshop-pro")
- `State` (string): Subscription state (active, canceled, etc.)
- `CurrentPriceInCents` (decimal?): Billing amount in cents
- `ActivatedAt` (DateTime?): When subscription became active
- `NextBillingAt` (DateTime?): When next payment is due
- `CancelledAt` (DateTime?): When subscription was cancelled
- `CreatedAt` (DateTime): Record creation timestamp
- `UpdatedAt` (DateTime): Last update timestamp

## Production Considerations

1. **Webhook Integration**: Implement webhooks from Maxio to update subscription state in real-time
2. **Subscription Management**: Add endpoints to cancel, upgrade, downgrade subscriptions
3. **Payment Methods**: The sandbox plans don't require payment methods, but production deployments will
4. **Error Handling**: Current error handling is basic - add retry logic and detailed logging
5. **Rate Limiting**: Maxio API has rate limits - add queuing/backoff logic
6. **Audit Trail**: Store audit logs of all Maxio API calls
7. **Compliance**: Add GDPR data export/deletion functionality
8. **Monitoring**: Add health checks and monitoring for Maxio API availability

## Files Modified/Created

### New Files
- `src/ApplicationCore/Entities/SubscriptionAggregate/MaxioSubscription.cs`
- `src/ApplicationCore/Models/MaxioSettings.cs`
- `src/ApplicationCore/Models/MaxioCustomer.cs`
- `src/ApplicationCore/Models/MaxioSubscription.cs`
- `src/ApplicationCore/Models/SubscriptionPlan.cs`
- `src/ApplicationCore/Interfaces/IMaxioBillingService.cs`
- `src/ApplicationCore/Services/MaxioBillingService.cs`
- `src/PublicApi/SubscriptionEndpoints/*` (5 files)
- `src/Infrastructure/Data/Config/MaxioSubscriptionConfiguration.cs`

### Modified Files
- `src/PublicApi/Program.cs` - Added Maxio service registration and endpoint mapping
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/Infrastructure/Data/CatalogContext.cs` - Added MaxioSubscription DbSet

## Maxio API Endpoints Used

- `POST /customers.json` - Create customer
- `GET /customers/lookup.json` - Find customer by reference (idempotent)
- `GET /product_families/{handle}/products.json` - List products in family
- `POST /subscriptions.json` - Create subscription
- `GET /subscriptions/{id}.json` - Get subscription details
- `GET /customers/{id}/subscriptions.json` - List customer subscriptions

## Notes

- The integration uses standard HTTP Basic Auth with the Maxio API
- JSON property naming uses snake_case to match Maxio's API conventions
- All monetary amounts from Maxio are in cents and converted to decimal dollars in DTOs
- The in-memory database is suitable for testing but not production
