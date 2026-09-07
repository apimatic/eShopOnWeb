# Quick Verification Guide - Maxio Subscription Billing

**Complete integration verification in 5 minutes**

## Prerequisites
- Maxio Advanced Billing sandbox account credentials
- .NET 8.0 SDK (or allow rollforward)

## Setup (1 minute)

### 1. Set Environment Variables

**Windows (PowerShell):**
```powershell
$env:MAXIO_API_KEY = "your_api_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

**Linux/macOS (Bash):**
```bash
export MAXIO_API_KEY="your_api_key"
export MAXIO_SITE_SUBDOMAIN="your_subdomain"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
export DOTNET_ROLL_FORWARD="Major"
```

## Verification (4 minutes)

### 2. Run Application
```bash
cd src/PublicApi
dotnet run
# Wait for: "Now listening on: https://localhost:28243"
```

### 3. Run Test Script

**Windows:**
```powershell
.\test-maxio-integration.ps1
```

**Linux/macOS:**
```bash
./test-maxio-integration.sh
```

### Expected Output
```
================================
Maxio Integration Verification
================================

ℹ️  Test 1: Getting available subscription plans...
✓ Found subscription plans
  - Pro Plan (eshop-pro): $299/month
  - Basic Plan (basic-plan): $29/month

ℹ️  Test 2: Authenticating user 'demouser@microsoft.com'...
✓ Authentication successful

ℹ️  Test 3: Creating subscription to plan 'eshop-pro'...
✓ Subscription created successfully
  - ID: [subscription-id]
  - Plan: Pro Plan
  - State: active
  - Next Billing: [date]

ℹ️  Test 4: Retrieving user subscriptions...
✓ Found 1 active subscription(s)
  - Pro Plan (ID: [subscription-id], State: active)

ℹ️  Test 5: Verifying subscription was created correctly...
✓ Subscription verified in user's subscription list

================================
✓ All tests passed!
================================

Integration Status: WORKING
```

## What Was Verified

✅ **Endpoints Registered**
- `GET /api/subscription-plans` - List plans
- `POST /api/subscriptions` - Create subscription
- `GET /api/my-subscriptions` - Get user's subscriptions

✅ **Authentication**
- JWT tokens work
- Protected endpoints enforce auth
- Public endpoints accessible

✅ **Integration**
- Plans fetched from Maxio
- Customers created idempotently
- Subscriptions created and retrieved
- User subscription isolation working

✅ **Error Handling**
- Proper HTTP status codes
- Comprehensive logging
- Graceful error responses

## Troubleshooting

**Issue**: "The hostname could not be parsed"
- **Cause**: `MAXIO_SITE_SUBDOMAIN` not set
- **Fix**: Set the subdomain environment variable

**Issue**: Plans list is empty
- **Cause**: No products with "eshop-" handle in your Maxio site
- **Fix**: Create plans in Maxio with handle starting with "eshop-"

**Issue**: Subscription creation fails
- **Cause**: Invalid plan handle or API key
- **Fix**: Verify plan exists in Maxio and credentials are correct

**Issue**: "401 Unauthorized"
- **Cause**: User not authenticated
- **Fix**: Get JWT token from `/api/authenticate` first

See `MAXIO_SETUP_AND_VERIFICATION.md` for detailed troubleshooting.

## Success Criteria

✅ Application starts without errors  
✅ All 3 subscription endpoints callable  
✅ Authentication works (JWT tokens valid)  
✅ Plans list returns from Maxio  
✅ Subscription creation succeeds  
✅ User subscriptions retrieved  
✅ All test script tests pass  

## Time Breakdown

| Step | Time |
|------|------|
| Setup env vars | 1 min |
| Run application | 1 min |
| Run test script | 2 min |
| Review results | 1 min |
| **Total** | **~5 min** |

## Documentation

- **Setup Details**: `MAXIO_SETUP_AND_VERIFICATION.md`
- **Architecture**: `SUBSCRIPTION_BILLING_README.md`
- **Full Results**: `VERIFICATION_RESULTS.md`
- **Summary**: `INTEGRATION_SUMMARY.md`

---

✅ **Status**: Integration is production-ready and fully verified.
