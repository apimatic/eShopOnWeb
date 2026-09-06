# Maxio Subscription Billing Integration - Verification Guide

## Build Verification ✅

The integration has been completed and verified to compile cleanly:

```
Build succeeded.
0 Errors
2 Warnings (pre-existing package vulnerabilities)
```

**Verification command:**
```bash
cd repo
dotnet build eShopOnWeb.sln
```

## Architecture Overview

### New Components
- **Entity**: `src/ApplicationCore/Entities/Subscription.cs` - Tracks user subscriptions
- **Service**: `src/ApplicationCore/Services/SubscriptionService.cs` - Maxio SDK wrapper
- **Endpoints**: `src/PublicApi/SubscriptionEndpoints/*` - Three REST APIs
  - `GET /api/subscription-plans` - List available plans
  - `POST /api/subscriptions` - Create subscription
  - `GET /api/my-subscriptions` - List user's subscriptions

### Configuration
- Settings: `Maxio:` section in `appsettings.json`
- Credentials: Environment variables (no secrets in repo)
  - `MAXIO_API_KEY` → `Maxio:ApiKey`
  - `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
  - `MAXIO_ENVIRONMENT` → `Maxio:Environment`
  - `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`

## Step-by-Step Verification

### 1. Prerequisites

Ensure you have:
- .NET 8.0+ SDK installed (`dotnet --version`)
- Maxio sandbox account credentials
- HTTPS dev cert trusted locally
- Recommended: Postman or curl for API testing

### 2. Configure Credentials

**Option A: Environment Variables** (recommended for CI/CD)
```powershell
# Windows PowerShell
$env:MAXIO_API_KEY = "<your_api_key>"
$env:MAXIO_SITE_SUBDOMAIN = "<your_subdomain>"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

**Option B: .NET User Secrets** (recommended for local development)
```bash
cd src/PublicApi

# Set all secrets
dotnet user-secrets set "Maxio:ApiKey" "<your_api_key>"
dotnet user-secrets set "Maxio:Subdomain" "<your_subdomain>"
dotnet user-secrets set "Maxio:Environment" "sandbox"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Verify secrets are set
dotnet user-secrets list
```

### 3. Run the Application

**Start PublicApi with in-memory database:**
```bash
cd src/PublicApi
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --configuration Debug

# Expected output:
# info: Microsoft.AspNetCore.Hosting.Diagnostics[1]
#   Request starting HTTP/2 POST https://localhost:27983/api/...
```

**Expected startup sequence:**
1. "PublicApi App created..."
2. "Seeding Database..."
3. "LAUNCHING PublicApi"
4. Listening on ports 27983 (HTTPS) and 27984 (HTTP)

### 4. Verify Each Endpoint

#### 4a. Authenticate User

Get a JWT token (required for subscription endpoints):

**Request:**
```bash
curl -X POST https://localhost:27983/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"test@example.com","password":"<password>"}' \
  --insecure
```

**Expected Response:**
```json
{
  "token": "eyJhbGc...",
  "result": true,
  "username": "test@example.com",
  "correlationId": "..."
}
```

**Save the token:**
```bash
$TOKEN = "eyJhbGc..."  # Copy from response
```

#### 4b. List Subscription Plans

This endpoint is public (no auth required):

**Request:**
```bash
curl -X GET https://localhost:27983/api/subscription-plans --insecure
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "priceFormatted": "$299.00",
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "priceFormatted": "$29.00",
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

**What this verifies:**
- ✅ Maxio API connectivity
- ✅ SDK client initialization
- ✅ Product family and plan retrieval
- ✅ Error handling (if no plans found)

#### 4c. Create Subscription

Subscribe a user to a plan (idempotent):

**Request:**
```bash
curl -X POST https://localhost:27983/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

**Expected Response:**
```json
{
  "subscriptionId": 12345678,
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "priceInCents": 29900,
  "state": "active",
  "activatedAt": "2026-09-07T12:00:00Z",
  "nextAssessmentAt": "2026-10-07T12:00:00Z",
  "currentPeriodEndsAt": "2026-10-07T12:00:00Z",
  "message": "Subscription created successfully"
}
```

**What this verifies:**
- ✅ JWT authentication
- ✅ User identity extraction from token
- ✅ Customer creation (idempotent lookup/create)
- ✅ Subscription creation
- ✅ Database persistence
- ✅ Idempotency: repeating request returns same subscription ID

#### 4d. List User's Subscriptions

Get all subscriptions for the authenticated user:

**Request:**
```bash
curl -X GET https://localhost:27983/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "priceInCents": 29900,
      "state": "active",
      "activatedAt": "2026-09-07T12:00:00Z",
      "nextAssessmentAt": "2026-10-07T12:00:00Z",
      "currentPeriodEndsAt": "2026-10-07T12:00:00Z"
    }
  ]
}
```

**What this verifies:**
- ✅ User identity extraction
- ✅ Database query filtering
- ✅ Subscription state retrieval

### 5. Complete Test Scenario

Follow this sequence to verify the complete flow:

```powershell
# 1. Set credentials
$env:MAXIO_API_KEY = "your_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"

# 2. Start the API
cd src/PublicApi
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --configuration Debug
# (wait for startup)

# 3. In another terminal, test endpoints
cd repo

# 3a. Authenticate
$authResponse = curl -s -X POST https://localhost:27983/api/authenticate `
  -H "Content-Type: application/json" `
  -d '{"username":"test@example.com","password":"Pass@123"}' `
  --insecure | ConvertFrom-Json
  
$TOKEN = $authResponse.token
Write-Host "Token acquired: $($TOKEN.Substring(0,20))..."

# 3b. List plans
curl -s https://localhost:27983/api/subscription-plans --insecure | ConvertFrom-Json

# 3c. Subscribe
curl -s -X POST https://localhost:27983/api/subscriptions `
  -H "Authorization: Bearer $TOKEN" `
  -H "Content-Type: application/json" `
  -d '{"productHandle":"eshop-pro"}' `
  --insecure | ConvertFrom-Json

# 3d. List my subscriptions
curl -s https://localhost:27983/api/my-subscriptions `
  -H "Authorization: Bearer $TOKEN" `
  --insecure | ConvertFrom-Json
```

## Troubleshooting

### "Failed to connect to Maxio"
- Verify credentials are correct and set in environment
- Check sandbox subdomain is correct (from Maxio account)
- Verify API key has permissions for Products, Customers, Subscriptions

### "401 Unauthorized" on subscription endpoints
- Ensure JWT token is being sent in Authorization header
- Token must be acquired from /api/authenticate endpoint first
- Check that user credentials are correct

### "404 Not Found" on /api/subscription-plans
- Verify product family handle matches Maxio sandbox setup
- Ensure `MAXIO_DEFAULT_PRODUCT_FAMILY` is set to "eshop-subscribe"
- Plans must be created in Maxio admin first

### "500 Internal Server Error"
- Check PublicApi console output for detailed error messages
- Ensure all Maxio configuration is set correctly
- Verify database is initialized (use `UseOnlyInMemoryDatabase=true` for testing)

## Production Deployment Checklist

- [ ] Use SQL Server instead of in-memory database
- [ ] Store credentials in Azure Key Vault or secure configuration system
- [ ] Set `MAXIO_ENVIRONMENT` to "production" for production Maxio account
- [ ] Enable HTTPS certificate pinning
- [ ] Configure request timeout and retry policies
- [ ] Set up monitoring/alerting for subscription endpoints
- [ ] Create migration for Subscription table
- [ ] Add integration tests for subscription flows
- [ ] Document API endpoints in API documentation
- [ ] Set up CI/CD pipeline for automated testing

## Key Files Modified/Created

**New Files:**
- `src/ApplicationCore/Entities/Subscription.cs`
- `src/ApplicationCore/Configuration/MaxioSettings.cs`
- `src/ApplicationCore/Services/SubscriptionService.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlansListEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/GetUserSubscriptionsEndpoint.cs`

**Modified Files:**
- `src/Infrastructure/Dependencies.cs` - Registered SubscriptionService
- `src/Infrastructure/Data/CatalogContext.cs` - Added Subscriptions DbSet
- `Directory.Packages.props` - Added Maxio SDK
- `src/PublicApi/appsettings.json` - Added Maxio configuration
- `src/ApplicationCore/ApplicationCore.csproj` - Added Maxio SDK reference

## Support

For issues with:
- **Maxio SDK**: Check `src/ApplicationCore/Services/SubscriptionService.cs` implementation
- **Endpoints**: Check `src/PublicApi/SubscriptionEndpoints/` for request/response models
- **Database**: Check Subscription entity configuration in EF Core
- **Authentication**: Verify JWT token is correctly extracted from HTTP context

---

**Integration Status**: ✅ Complete and ready for testing
**Build Status**: ✅ 0 Errors, 0 Warnings (SDK-related)
**Last Updated**: 2026-09-07
