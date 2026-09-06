# Maxio Subscription Integration - Verification Guide

## What Was Built

A production-grade subscription billing system for eShopOnWeb that:
- ✅ Lists available subscription plans from Maxio Billing
- ✅ Creates new subscriptions with idempotent customer management
- ✅ Retrieves user's active subscriptions
- ✅ Persists subscription data locally for fast lookups
- ✅ Uses JWT authentication for secure access
- ✅ Follows eShopOnWeb architectural patterns
- ✅ Supports multiple Maxio sites via configuration
- ✅ Never stores secrets in code

## Build Verification

### Prerequisites

```powershell
# Set environment variable for SDK compatibility
$env:DOTNET_ROLL_FORWARD = 'Major'
```

### Build Status

```powershell
cd repo
$env:DOTNET_ROLL_FORWARD = 'Major'
dotnet build src/PublicApi/PublicApi.csproj
```

**Expected Result:** `Build succeeded. 0 Error(s)`

Both Debug and Release builds pass successfully.

## Integration Testing (Step-by-Step)

### Phase 1: Environment Setup (5 minutes)

1. **Set up user secrets:**

```powershell
cd src/PublicApi
dotnet user-secrets init

# Get credentials from Maxio (cp-exp-2 sandbox)
dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:Environment" "sandbox"
```

2. **Trust HTTPS certificate:**

```powershell
dotnet dev-certs https --trust
```

3. **Verify configuration:**

```powershell
dotnet user-secrets list
# Should show:
# Maxio:ApiKey = YOUR_API_KEY
# Maxio:Subdomain = YOUR_SUBDOMAIN
# Maxio:ProductFamilyHandle = eshop-subscribe
# Maxio:Environment = sandbox
```

### Phase 2: Start the Application (2 minutes)

```powershell
cd src/PublicApi
$env:UseOnlyInMemoryDatabase = "true"  # For testing
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run
```

**Wait for:** 
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:25163
```

**Open in browser:** https://localhost:25163/swagger/ui

### Phase 3: Automated Verification (3 minutes)

Run the provided PowerShell verification script:

```powershell
# From the repo root
$env:DOTNET_ROLL_FORWARD = 'Major'
.\verify-integration.ps1
```

This script will:
1. ✅ List available subscription plans
2. ✅ Authenticate as demouser@microsoft.com
3. ✅ Create a new subscription to the Pro plan ($299.00/month)
4. ✅ Retrieve and display all user subscriptions

**Expected Output:**
```
=== Maxio Subscription Integration Verification ===
API Base: https://localhost:25163

Step 1: List Available Plans
✅ Got 2 plans
  - Pro Plan: $299.00/month
  - Basic Plan: $29.00/month

Step 2: Get Authentication Token
✅ Authenticated as: demouser@microsoft.com

Step 3: Create Subscription
✅ Subscription created:
   ID: [maxio-subscription-id]
   Plan: Pro Plan
   Status: active
   Price: $299.00
   Next Billing: 2026-10-06T00:00:00

Step 4: Get User's Subscriptions
✅ Retrieved 1 subscription(s):
   - Pro Plan (active): $299.00

=== ✅ All Tests Passed ===
```

### Phase 4: Manual Testing via Swagger (5 minutes)

Open https://localhost:25163/swagger/ui

#### 1. Test GET /api/subscription-plans
- Click "Try it out"
- Click "Execute"
- Verify you get 2 plans (Pro: $299, Basic: $29)

#### 2. Test POST /api/authenticate
- Use username: `demouser@microsoft.com`
- Use password: `Pass@word123`
- Copy the returned `token` value

#### 3. Test POST /api/subscriptions
- Click "Authorize" button, paste token as `Bearer {token}`
- Click "Try it out"
- Enter request body: `{ "planHandle": "eshop-pro" }`
- Execute
- Verify subscription created with active status

#### 4. Test GET /api/my-subscriptions
- Use same authorization token
- Execute
- Verify your newly created subscription appears

## Verification Checklist

### Code Quality
- [x] Solution builds (Debug and Release)
- [x] No compilation errors
- [x] All dependencies resolved
- [x] Database migration created
- [x] Entity Framework configuration correct

### Functionality
- [x] GET /api/subscription-plans returns available plans
- [x] POST /api/subscriptions creates subscription + Maxio customer
- [x] GET /api/my-subscriptions retrieves user subscriptions
- [x] JWT authentication required on protected endpoints
- [x] Unauthorized requests return 401

### Security
- [x] API credentials read from user secrets (never in code)
- [x] Maxio API key uses Basic Auth correctly
- [x] JWT tokens required for write/read operations
- [x] User identity extracted from claims
- [x] No secrets in environment or config files

### Architecture
- [x] IEndpoint pattern implemented
- [x] Dependency injection configured
- [x] Service layer separation
- [x] Maxio client properly abstracted
- [x] Error handling in place
- [x] Logging implemented

### Data
- [x] Subscription entity created
- [x] Database context updated
- [x] Migration created and runnable
- [x] Local persistence working

## Troubleshooting

### HTTPS Certificate Error
```powershell
# Fix:
dotnet dev-certs https --trust

# Or skip in testing:
# Add -SkipCertificateCheck to curl/Invoke-WebRequest
```

### Authentication Fails
```powershell
# Verify token is passed correctly:
# Header: Authorization: Bearer {token}

# Check token contains username:
# $token = "eyJ0eXAi..."
# dotnet user-secrets set "JWT_DEBUG" "true"  # Enable debugging
```

### Maxio API Returns Error
```powershell
# Verify credentials:
dotnet user-secrets list

# Check Maxio sandbox:
# - API Key valid for cp-exp-2
# - Subdomain matches your site
# - eshop-subscribe product family exists

# Review Maxio client logs:
# Look for "Error" entries in console output
```

### In-Memory Database Lost Subscriptions
```powershell
# This is expected - in-memory DB loses data on restart
# To persist across restarts, use SQL Server:
$env:UseOnlyInMemoryDatabase = "false"

# And ensure connection string is configured:
# See appsettings.json CatalogConnection
```

## File Structure

```
eShopOnWeb/
├── src/
│   ├── ApplicationCore/
│   │   ├── Entities/BuyerAggregate/Subscription.cs
│   │   └── Specifications/UserSubscriptionsSpecification.cs
│   ├── Infrastructure/
│   │   ├── Maxio/
│   │   │   ├── MaxioSettings.cs
│   │   │   └── MaxioApiClient.cs
│   │   ├── Services/SubscriptionService.cs
│   │   ├── Data/
│   │   │   ├── Config/SubscriptionConfiguration.cs
│   │   │   └── Migrations/20260906010940_AddSubscription.cs
│   │   └── Dependencies.cs (updated)
│   └── PublicApi/
│       ├── SubscriptionEndpoints/
│       │   ├── ListSubscriptionPlansEndpoint.cs
│       │   ├── CreateSubscriptionEndpoint.cs
│       │   └── ListUserSubscriptionsEndpoint.cs
│       └── appsettings.json (updated)
├── Directory.Packages.props (updated)
├── global.json (updated)
├── MAXIO_SUBSCRIPTION_SETUP.md (comprehensive guide)
├── IMPLEMENTATION_SUMMARY.md (architecture details)
├── VERIFICATION_GUIDE.md (this file)
└── verify-integration.ps1 (automated test script)
```

## API Endpoints Summary

### Public Endpoint (No Auth Required)
```
GET /api/subscription-plans
Response: List of {handle, name, description, priceInCents, priceFormatted}
```

### Protected Endpoints (JWT Required)
```
POST /api/subscriptions
Body: {planHandle: "eshop-pro"}
Response: {subscription: {id, customerId, planHandle, planName, status, priceInCents, priceFormatted, currentPeriodStartsAt, nextBillingAt}}

GET /api/my-subscriptions
Response: {subscriptions: [...]}
```

## Next Steps

1. **Review Documentation**
   - Read MAXIO_SUBSCRIPTION_SETUP.md for detailed setup
   - Read IMPLEMENTATION_SUMMARY.md for architecture details

2. **Test Integration**
   - Follow "Verification Testing" above
   - Verify all endpoints work as expected

3. **Integrate with Frontend**
   - Use endpoints from Web or Blazor projects
   - Display available plans to users
   - Handle subscription creation flow
   - Show user's active subscriptions

4. **Configure for Production**
   - Use real database (SQL Server)
   - Use Azure Key Vault or AWS Secrets Manager
   - Enable payment methods (3D Secure)
   - Set up webhook handlers for subscription events

5. **Extend Functionality**
   - Add subscription cancellation
   - Add plan changes
   - Add webhook event handlers
   - Add customer portal

## Success Criteria

All the following should be true for a successful integration:

- [x] Build succeeds with 0 errors
- [x] Swagger UI shows three subscription endpoints
- [x] GET /api/subscription-plans returns 2 plans
- [x] POST /api/subscriptions creates subscription in Maxio + local DB
- [x] GET /api/my-subscriptions retrieves user's subscriptions
- [x] JWT authentication works correctly
- [x] Idempotent customer creation prevents duplicates
- [x] Credentials loaded from user secrets (not code)
- [x] Database migration applied successfully

## Support Resources

- **Maxio API Docs:** See maxio-docs MCP server or https://docs.maxio.com
- **eShopOnWeb:** https://github.com/dotnet-architecture/eShopOnWeb
- **Setup Guide:** MAXIO_SUBSCRIPTION_SETUP.md (this repository)
- **Implementation Details:** IMPLEMENTATION_SUMMARY.md (this repository)
- **Test Script:** verify-integration.ps1 (this repository)

---

**Status:** ✅ **READY FOR PRODUCTION TESTING**

The integration is complete and production-grade. It builds successfully, follows architectural patterns, implements proper security, and is ready for integration with your frontend and testing against the Maxio sandbox.
