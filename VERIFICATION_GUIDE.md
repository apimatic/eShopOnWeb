# Maxio Subscription Integration - Verification Guide

## Overview

This guide provides step-by-step instructions to verify the Maxio subscription billing integration is working correctly in eShopOnWeb.

## Pre-Requisites

- .NET 10 SDK installed (or .NET 8 with `rollForward: latestMajor`)
- Maxio sandbox account with:
  - Product family handle: `eshop-subscribe`
  - Plans: `eshop-pro` ($299/mo), `basic-plan` ($29/mo)
  - API key from Maxio
- PowerShell or terminal access

## Setup Steps

### 1. Configure Maxio Credentials

```bash
cd src/PublicApi

# Set your actual Maxio credentials (replace with real values)
dotnet user-secrets set "Maxio:ApiKey" "your-actual-maxio-api-key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-1"
```

**Note:** The default subdomain is `cp-exp-1` (sandbox). Change if using a different Maxio site.

### 2. Verify Build

```bash
cd C:\claude-runs\t1h45ali-maxio-sdk-haiku45high-058\repo

# Clean build
dotnet clean eShopOnWeb.sln
dotnet build eShopOnWeb.sln -c Debug

# Expected: "Build succeeded. 0 Error(s)"
```

### 3. Verify Database Migration Applied

The migration `AddMaxioCustomerIdToApplicationUser` should be automatically applied on startup. To verify it's in the migrations folder:

```bash
ls src/Infrastructure/Identity/Migrations | grep -i maxio
# Expected: timestamp_AddMaxioCustomerIdToApplicationUser.cs
```

## Running Tests

### Start the PublicApi Server

```bash
cd src/PublicApi
dotnet run
```

Expected output should include:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:28503
      Now listening on: http://localhost:28503
```

**Note:** The application uses HTTPS on port 28503. In development, the dev cert is used (ignore SSL warnings in curl with `--insecure`).

### Test 1: Verify Endpoints Are Registered

Check that the endpoints are available in Swagger/OpenAPI:

```bash
# In another terminal, curl the Swagger spec
curl https://localhost:28503/swagger/v1/swagger.json --insecure | grep -i subscription
```

Expected: Should find references to the three subscription endpoints:
- `/api/subscription-plans`
- `/api/subscriptions`  
- `/api/my-subscriptions`

### Test 2: Get Authentication Token

```bash
curl -X POST https://localhost:28503/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  --insecure

# Save the returned token for use in other requests
# Export: TOKEN="<token from response>"
```

Expected response includes:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "result": true,
  "username": "demouser@microsoft.com"
}
```

### Test 3: Verify GET /api/subscription-plans

This endpoint should work without needing Maxio connectivity for the basic endpoint registration.

```bash
export TOKEN="<token from Test 2>"

curl -X GET https://localhost:28503/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Expected behaviors:**

**A) If Maxio credentials are valid and available:**
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Professional",
      "description": "...",
      "priceInCents": 29900
    },
    {
      "handle": "basic-plan",
      "name": "Basic",
      "description": "...",
      "priceInCents": 2900
    }
  ],
  "correlationId": "uuid"
}
```

**B) If Maxio credentials are invalid/unavailable:**
```
HTTP 500 (or appropriate error status)
```
This is expected if credentials aren't configured - it means the endpoint routing works but Maxio isn't reachable.

### Test 4: Verify POST /api/subscriptions (Endpoint Structure)

This test verifies the endpoint is properly registered and authenticated:

```bash
export TOKEN="<token from Test 2>"

curl -X POST https://localhost:28503/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure
```

**Expected behaviors:**

**A) If Maxio credentials are valid:**
```json
{
  "subscriptionId": 12345,
  "state": "active",
  "nextBillingAt": "2026-10-07T00:00:00Z",
  "productHandle": "eshop-pro",
  "productName": "Professional",
  "productPriceInCents": 29900,
  "correlationId": "uuid"
}
HTTP 201 Created
```

**B) If Maxio credentials are invalid/unavailable:**
```
HTTP 500
```

**C) If authentication fails (no token or invalid token):**
```
HTTP 401 Unauthorized
```

This confirms the JWT authentication is working.

### Test 5: Verify GET /api/my-subscriptions

```bash
export TOKEN="<token from Test 2>"

curl -X GET https://localhost:28503/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Expected behaviors:**

**A) If user has subscriptions (Maxio credentials valid):**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345,
      "productHandle": "eshop-pro",
      "productName": "Professional",
      "productPriceInCents": 29900,
      "state": "active",
      "nextBillingAt": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:00:00Z"
    }
  ],
  "correlationId": "uuid"
}
HTTP 200 OK
```

**B) If user has no subscriptions:**
```json
{
  "subscriptions": [],
  "correlationId": "uuid"
}
HTTP 200 OK
```

**C) If Maxio credentials invalid or user not found:**
```
HTTP 500 (if exception during lookup)
or
HTTP 200 with empty subscriptions (if no customer found)
```

### Test 6: Verify Authentication Required

Test that endpoints reject requests without proper JWT:

```bash
# No authentication header
curl -X GET https://localhost:28503/api/subscription-plans --insecure
# Expected: HTTP 401 Unauthorized

# Invalid token
curl -X GET https://localhost:28503/api/subscription-plans \
  -H "Authorization: Bearer invalid.token.here" \
  --insecure
# Expected: HTTP 401 Unauthorized
```

## Code Verification Checklist

### Endpoint Implementation ✓
- [ ] `GetSubscriptionPlansEndpoint.cs` exists and implements `IEndpoint<IResult, GetSubscriptionPlansRequestDto>`
- [ ] `CreateSubscriptionEndpoint.cs` exists with `[Authorize]` attribute
- [ ] `GetMySubscriptionsEndpoint.cs` exists with `[Authorize]` attribute
- [ ] All three endpoints registered in `AddRoute()` methods

### Configuration ✓
- [ ] `Program.cs` includes Maxio client DI registration
- [ ] `appsettings.json` includes Maxio section with defaults
- [ ] Environment variables are read: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`
- [ ] `MaxioConfiguration.cs` class exists

### Data Model ✓
- [ ] `ApplicationUser.cs` has `MaxioCustomerId` field
- [ ] Migration file exists: `AddMaxioCustomerIdToApplicationUser`
- [ ] Migration applied (check database on startup)

### Error Handling ✓
- [ ] `SdkException<CreateSubscriptionError>` caught in CreateSubscription endpoint
- [ ] `SdkException<RawError>` caught in all endpoints
- [ ] Error responses return appropriate HTTP status codes
- [ ] Secrets not logged in error messages

### Security ✓
- [ ] No Maxio credentials hardcoded in source
- [ ] No API key in appsettings.json
- [ ] JWT authentication required on all subscription endpoints
- [ ] User-scoped subscriptions (can't access other users' subscriptions)

## Expected Flow Verification

### Success Path (With Valid Maxio Credentials)

1. **User Browses Plans**
   - GET `/api/subscription-plans` with JWT token
   - System calls `MaxioClient.Products.ListProducts()`
   - Returns plans from `eshop-subscribe` family
   - Status: HTTP 200 ✓

2. **User Subscribes**
   - POST `/api/subscriptions` with plan handle and JWT token
   - System looks up or creates Maxio customer (idempotent by email)
   - System calls `MaxioClient.Subscriptions.CreateSubscription()`
   - Returns subscription details with ID, state, next billing date
   - Status: HTTP 201 Created ✓

3. **User Views Subscriptions**
   - GET `/api/my-subscriptions` with JWT token
   - System retrieves user's Maxio customer ID from database
   - System calls `MaxioClient.Customers.ListCustomerSubscriptions()`
   - Returns list of subscriptions with all details
   - Status: HTTP 200 ✓

### Failure Path (Invalid/Missing Credentials)

1. **Endpoints return HTTP 500** when Maxio API is unreachable
2. **Endpoints return HTTP 401** when JWT is missing/invalid
3. **Subscriptions endpoint returns HTTP 422** if plan doesn't exist
4. **No subscriptions endpoint returns HTTP 200 with empty list** if customer not found

## Troubleshooting

### Build Fails
- Verify .NET 10 SDK is installed or `rollForward: latestMajor` is set in `global.json`
- Run `dotnet restore eShopOnWeb.sln` to restore packages
- Check that `AsadAli.AdvancedBilling.Sdk` package is installed

### Server Won't Start
- Check that port 28503 is available: `netstat -ano | findstr :28503` (Windows)
- Verify HTTPS dev cert is trusted: `dotnet dev-certs https --check`
- Check for error messages in console output

### Endpoints Return 401
- Verify you have a valid JWT token from `/api/authenticate`
- Check token is passed in `Authorization: Bearer <token>` header
- Token might be expired; get a fresh one

### Endpoints Return 500
- Maxio credentials might be invalid
- Maxio site subdomain might be incorrect
- API key might not have permission for the operations
- Check server console for exception details

### Subscription Creation Fails with 422
- Plan handle must exist in `eshop-subscribe` family
- Check valid handles: `eshop-pro`, `basic-plan`
- User might already have a subscription to that plan (depends on Maxio config)

### "Maxio Customer ID Not Found"
- First subscription creation should have synced customer
- If error occurs, check that user record is being updated with customer ID
- Try creating subscription again (customer should exist on Maxio now)

## Files to Review for Verification

| File | Purpose | Verify |
|------|---------|--------|
| `src/PublicApi/Program.cs` | DI setup | Lines 93-125 contain Maxio client registration |
| `src/PublicApi/appsettings.json` | Config defaults | Contains `Maxio` section |
| `src/PublicApi/SubscriptionEndpoints/*.cs` | Endpoints | Three endpoint files exist and have proper routing |
| `src/Infrastructure/Identity/ApplicationUser.cs` | Data model | Has `MaxioCustomerId` property |
| `src/Infrastructure/Identity/Migrations/*AddMaxio*` | Database | Migration file exists |
| `SUBSCRIPTION_INTEGRATION.md` | Feature docs | Complete documentation |

## Success Criteria

✅ **Build succeeds** with 0 errors  
✅ **Endpoints register** without errors on startup  
✅ **Authentication works** (401 on missing token, 200 with valid token)  
✅ **Plans endpoint responds** (200 with plans if credentials valid, 500 if not)  
✅ **Create subscription endpoint accepts requests** (201 if successful, 422 if validation error)  
✅ **List subscriptions endpoint responds** (200 with empty or populated list)  
✅ **No secrets in repository** (verified: no hardcoded API keys)  
✅ **Error handling works** (appropriate status codes for different failure modes)  

When all criteria are met, the integration is complete and ready for UI integration.
