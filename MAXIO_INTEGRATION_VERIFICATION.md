# Maxio Subscription Billing Integration - Verification Guide

This document provides step-by-step instructions to verify that the Maxio Advanced Billing subscription integration is working correctly in eShopOnWeb.

## Integration Overview

The Maxio Advanced Billing integration adds recurring subscription capabilities to eShopOnWeb as an **additive, parallel** feature to the existing cart/checkout flow. Three new API endpoints have been implemented:

1. **GET /api/subscription-plans** - List available subscription plans
2. **POST /api/subscriptions** - Create a new subscription for the authenticated user
3. **GET /api/my-subscriptions** - Retrieve subscriptions for the authenticated user

## Architecture

### Files Added/Modified

**New Files:**
- `src/PublicApi/MaxioOptions.cs` - Configuration options class
- `src/PublicApi/Services/MaxioService.cs` - Maxio API interaction service
- `src/PublicApi/SubscriptionEndpoints/SubscriptionEndpoints.cs` - Endpoint definitions
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs` - Data transfer objects
- `src/Infrastructure/Identity/Migrations/*AddMaxioCustomerIdToApplicationUser.cs` - Database migration
- `MAXIO_INTEGRATION_VERIFICATION.md` - This file

**Modified Files:**
- `src/Infrastructure/Identity/ApplicationUser.cs` - Added `MaxioCustomerId` property
- `src/PublicApi/Program.cs` - Registered MaxioService and subscription endpoints
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/PublicApi/appsettings.Development.json` - Enabled in-memory database
- `Directory.Packages.props` - No SDK dependency (using direct HTTP)

### Key Design Decisions

1. **HTTP Client over SDK** - Uses direct HTTP with Basic Authentication instead of relying on an unavailable NuGet SDK package. This provides full control and transparency.

2. **Direct Configuration Binding** - Maxio settings are bound from configuration (appsettings + user-secrets), with values loaded from environment variables:
   - `MAXIO_API_KEY` → `Maxio:ApiKey`
   - `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
   - `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`

3. **JWT Authentication** - All subscription endpoints require JWT bearer token authentication
   - Token obtained via existing `POST /api/authenticate` endpoint
   - User identity extracted from token claims

4. **Idempotent Customer Creation** - Customers are created/looked up using a unique reference (user ID)
   - Prevents duplicate customer creation on repeated calls

5. **In-Memory Database for Development** - Uses EntityFrameworkCore in-memory provider to avoid LocalDB requirements
   - Note: Data persists only within a single application run

## Prerequisites

### Maxio Sandbox Account
- Site subdomain: `cp-exp-4` (or your assigned sandbox site)
- API key from Maxio account settings
- The following entities must be pre-seeded on your Maxio site:
  - Product Family: `eshop-subscribe` (ID may vary after re-seeding)
  - Pro Plan: `eshop-pro` ($299.00/mo)
  - Basic Plan: `eshop-basic` ($29.00/mo)

### .NET Environment
- SDK: .NET 10 or compatible with `.NET 8.0 x` (via `rollForward: latestMajor`)
- ASP.NET Core 8.0 runtime required (or SDK 10)
- Development certificate for HTTPS (automatically installed)

## Setup Instructions

### 1. Configure Maxio Credentials

Set the following user-secrets with your actual Maxio sandbox credentials:

```bash
cd src/PublicApi

dotnet user-secrets set "Maxio:ApiKey" "YOUR_ACTUAL_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-4"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

Alternatively, set environment variables before running:
```bash
$env:MAXIO_API_KEY="YOUR_ACTUAL_API_KEY"
$env:MAXIO_SITE_SUBDOMAIN="cp-exp-4"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

### 2. Build the Solution

```bash
cd repo-root
dotnet build
```

Expected outcome: Build succeeds with warnings about System.Text.Json vulnerabilities (pre-existing)

### 3. Run the PublicApi Service

```bash
cd repo-root
$env:DOTNET_ROLL_FORWARD="Major"
$env:UseOnlyInMemoryDatabase="true"
dotnet run --project src/PublicApi/PublicApi.csproj
```

The API should start and listen on:
- HTTPS: `https://localhost:24643`
- HTTP: `http://localhost:24644`

Look for log output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:24643
info: Microsoft.Hosting.Lifetime[0]
      Application started.
```

## Testing the Integration

### Test 1: List Subscription Plans (No Auth Required)

```bash
# Using curl
curl -X GET https://localhost:24643/api/subscription-plans \
  -H "Accept: application/json" \
  --insecure

# Expected response (200 OK):
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription",
      "price": 299.00,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "eshop-basic",
      "name": "Basic Plan",
      "description": "Basic subscription",
      "price": 29.00,
      "intervalUnit": "month"
    }
  ]
}
```

### Test 2: Authenticate and Get JWT Token

```bash
# Using curl
curl -X POST https://localhost:24643/api/authenticate \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'

# Expected response (200 OK):
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com",
  ...
}

# Save the token for subsequent requests
export TOKEN="<token_from_response>"
```

### Test 3: Create a Subscription (Requires Auth)

```bash
# Using curl with the token from Test 2
curl -X POST https://localhost:24643/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure \
  -d '{
    "productHandle": "eshop-pro"
  }'

# Expected response (201 Created):
{
  "subscriptionId": 123456,
  "state": "active",
  "productName": "Pro Plan",
  "createdAt": "2026-09-06T10:30:00Z",
  "nextBillingAt": "2026-10-06T00:00:00Z"
}
```

### Test 4: List User's Subscriptions (Requires Auth)

```bash
# Using curl with the same token
curl -X GET https://localhost:24643/api/my-subscriptions \
  -H "Accept: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure

# Expected response (200 OK):
{
  "subscriptions": [
    {
      "subscriptionId": 123456,
      "state": "active",
      "productName": "Pro Plan",
      "createdAt": "2026-09-06T10:30:00Z",
      "nextBillingAt": "2026-10-06T00:00:00Z"
    }
  ]
}
```

## Troubleshooting

### Issue: 400 Bad Request on `/api/subscription-plans`

**Cause:** Maxio credentials are not properly configured or are invalid

**Solution:**
1. Verify user-secrets are set correctly: `dotnet user-secrets list`
2. Verify environment variables are set: `$env:MAXIO_API_KEY`, etc.
3. Check Maxio API key is valid (not placeholder text like "your_api_key_here")
4. Verify Maxio site subdomain is correct (e.g., "cp-exp-4")

### Issue: 401 Unauthorized on subscription endpoints

**Cause:** JWT token is missing or invalid

**Solution:**
1. Ensure you obtained a valid token from `/api/authenticate`
2. Include the token in the Authorization header: `Authorization: Bearer <token>`
3. Check token hasn't expired (tokens expire after 24 hours by default)

### Issue: 404 Not Found on `/api/subscription-plans`

**Cause:** Endpoints not registered (PublicApi project issue)

**Solution:**
1. Verify build succeeded: `dotnet build`
2. Check appsettings configuration includes `"Maxio"` section
3. Verify `AddSubscriptionEndpoints()` is called in `Program.cs`

### Issue: SDK/Runtime Mismatch

**Cause:** global.json pins SDK to 8.0.x but .NET 10 is installed

**Solution:**
1. Enable forward roll: `$env:DOTNET_ROLL_FORWARD="Major"`
2. Or install ASP.NET Core 8.0 runtime: `dotnet --info`

### Issue: "No connection could be made because the target machine actively refused it"

**Cause:** PublicApi not running or not listening on expected port

**Solution:**
1. Verify API is running: `netstat -ano | findstr 24643`
2. Check for port conflicts: `Get-NetTCPConnection -LocalPort 24643`
3. Review API startup logs for exceptions

## Database Migrations

A migration has been created to add the `MaxioCustomerId` column to the `AspNetUsers` table:

```bash
# To apply the migration (if using SQL Server):
dotnet ef database update --project src/Infrastructure/Infrastructure.csproj --startup-project src/PublicApi/PublicApi.csproj --context AppIdentityDbContext
```

**Note:** For development with in-memory database, migrations are automatically applied on startup.

## Production Deployment Checklist

- [ ] Maxio API key stored in secure configuration (Azure Key Vault, etc.)
- [ ] API key NOT stored in appsettings files or environment files
- [ ] Maxio:ApiKey loaded from environment variables at runtime
- [ ] Database migration applied to production database
- [ ] HTTPS enforced (already configured in project)
- [ ] JWT token expiration reviewed and set appropriately
- [ ] Error handling tested for Maxio API downtime
- [ ] Maxio webhook endpoints configured for subscription state changes (future enhancement)
- [ ] Rate limiting considered for API endpoints (future enhancement)

## Future Enhancements

1. **Webhook Support** - Listen for Maxio webhooks to update subscription state in real-time
2. **Subscription Management** - Update, pause, resume, or cancel subscriptions
3. **Metered Components** - Track and bill usage-based charges (e.g., API call overage)
4. **Payment Management** - Add/update payment methods tied to subscriptions
5. **Billing History** - View invoices and payment history
6. **Retry Logic** - Implement exponential backoff for Maxio API calls
7. **Caching** - Cache subscription plans to reduce API calls

## Support

For issues with the integration:
1. Check the logs in the PublicApi output
2. Review the Maxio API documentation: https://docs.maxio.com/
3. Verify Maxio sandbox site: https://cp-exp-4.chargify.com
4. Test authentication separately: `POST /api/authenticate`

---

**Created:** September 6, 2026  
**Integration Type:** Maxio Advanced Billing  
**Status:** Ready for Testing
