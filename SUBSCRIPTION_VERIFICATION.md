# Maxio Subscription Integration - Verification Checklist

This document provides a comprehensive verification checklist to ensure the Maxio subscription billing integration is working correctly.

## Pre-Verification Checklist

- [ ] .NET 10 SDK (or 8+ with rollForward) installed
- [ ] ASP.NET Core 8.0 runtime installed
- [ ] Maxio sandbox account credentials obtained
- [ ] Environment variables set (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, etc.)
- [ ] User-secrets configured (see SUBSCRIPTION_SETUP.md)
- [ ] Solution builds successfully: `dotnet build eShopOnWeb.sln -c Debug`

## Build Verification

### Check Build Output

```bash
cd repo-root
dotnet build eShopOnWeb.sln -c Debug
```

**Expected Result**: ✅ Build succeeds with no errors

**Verify these assemblies exist**:
- [ ] `src/ApplicationCore/bin/Debug/net8.0/Microsoft.eShopWeb.ApplicationCore.dll`
- [ ] `src/Infrastructure/bin/Debug/net8.0/Microsoft.eShopWeb.Infrastructure.dll`
- [ ] `src/PublicApi/bin/Debug/net8.0/Microsoft.eShopWeb.PublicApi.dll`

## Code Structure Verification

### ApplicationCore Changes

**File**: `src/ApplicationCore/MaxioSettings.cs`
```csharp
✅ Exists
✅ Contains: ApiKey, Subdomain, ProductFamilyHandle, BaseUrl properties
✅ Contains: GetBaseUrl() method to construct API URL
```

**File**: `src/ApplicationCore/Interfaces/IMaxioClient.cs`
```csharp
✅ Interface defines:
  - CreateOrGetCustomerAsync(string email, string firstName, string lastName)
  - LookupCustomerByEmailAsync(string email)
  - CreateSubscriptionAsync(int customerId, string productHandle)
  - GetProductsAsync(string productFamilyHandle)
  - GetProductByHandleAsync(string productHandle)
  - GetCustomerSubscriptionsAsync(int customerId)
✅ DTO classes: MaxioCustomer, MaxioProduct, MaxioSubscription
```

### Infrastructure Changes

**File**: `src/Infrastructure/Services/MaxioClient.cs`
```csharp
✅ Implements IMaxioClient
✅ Uses HttpClient with Basic Auth (API_KEY:x)
✅ SetAuthHeader() method creates proper Authorization header
✅ LookupCustomerByEmailAsync() - lookup without create
✅ GetProductByHandleAsync() - fetch individual product by handle
✅ All methods return appropriate types or null on error
✅ JSON parsing with proper error handling
```

**File**: `src/Infrastructure/Dependencies.cs`
```csharp
✅ Registers MaxioSettings singleton
✅ Registers HttpClient factory: AddHttpClient<IMaxioClient, MaxioClient>()
```

### PublicApi Changes

**File**: `src/PublicApi/Program.cs`
```csharp
✅ Added: builder.Services.AddHttpContextAccessor()
```

**File**: `src/PublicApi/appsettings.json`
```json
✅ Contains Maxio section with ApiKey, Subdomain, ProductFamilyHandle, BaseUrl
```

**File**: `src/PublicApi/appsettings.Development.json`
```json
✅ UseOnlyInMemoryDatabase: true (for local dev without SQL Server)
```

**Directory**: `src/PublicApi/SubscriptionEndpoints/`

**Endpoint 1**: GetSubscriptionPlansEndpoint
```csharp
✅ File: GetSubscriptionPlansEndpoint.cs
✅ File: GetSubscriptionPlansEndpoint.GetRequest.cs
✅ File: GetSubscriptionPlansEndpoint.GetResponse.cs
✅ Route: GET /api/subscription-plans
✅ Auth: [Authorize] with JWT
✅ Returns: List of SubscriptionPlanDto with id, handle, name, price, description
```

**Endpoint 2**: CreateSubscriptionEndpoint
```csharp
✅ File: CreateSubscriptionEndpoint.cs
✅ File: CreateSubscriptionEndpoint.CreateRequest.cs (PlanHandle property)
✅ File: CreateSubscriptionEndpoint.CreateResponse.cs
✅ Route: POST /api/subscriptions
✅ Auth: [Authorize] with JWT
✅ Logic: 
  - Extract user email from JWT claims
  - Create or get Maxio customer (idempotent)
  - Create subscription
  - Fetch product details for pricing
  - Return: SubscriptionId, CustomerId, State, ActivatedAt, NextBillingDate, MonthlyPrice
```

**Endpoint 3**: GetMySubscriptionsEndpoint
```csharp
✅ File: GetMySubscriptionsEndpoint.cs
✅ File: GetMySubscriptionsEndpoint.GetRequest.cs
✅ File: GetMySubscriptionsEndpoint.GetResponse.cs
✅ Route: GET /api/my-subscriptions
✅ Auth: [Authorize] with JWT
✅ Logic:
  - Extract user email from JWT
  - Lookup customer by email (lookup only, no create)
  - Get customer's subscriptions
  - Return: List of UserSubscriptionDto with id, state, activatedAt, nextBillingDate
```

**Supporting DTO**: `SubscriptionPlanDto.cs`
```csharp
✅ Contains: Id, Handle, Name, Price, Description
```

## Runtime Verification

### Step 1: Start the API

```bash
cd src/PublicApi
dotnet run --environment Development
```

**Expected Output**:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:27403
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to stop, sending SIGTERM...
```

**Verify**:
- [ ] No connection errors
- [ ] No "Maxio" API key errors
- [ ] Listening on https://localhost:27403

### Step 2: Verify Swagger UI

Navigate to: `https://localhost:27403/swagger`

**Verify**:
- [ ] Swagger UI loads
- [ ] Three new endpoints visible under "SubscriptionEndpoints":
  - [ ] GET /api/subscription-plans
  - [ ] POST /api/subscriptions
  - [ ] GET /api/my-subscriptions
- [ ] Authorize button is available
- [ ] Each endpoint shows [Authorize] badge

### Step 3: Get Authentication Token

```bash
curl -X POST https://localhost:27403/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  -k  # ignore cert validation for local dev
```

**Expected Response** (200 OK):
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com"
}
```

**Verify**:
- [ ] Status is 200 OK
- [ ] `result` is `true`
- [ ] `token` is a non-empty JWT string
- [ ] `username` matches request

### Step 4: Test Get Subscription Plans

```bash
# Save token from previous step
$TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."

curl -X GET https://localhost:27403/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  -k
```

**Expected Response** (200 OK):
```json
{
  "correlationId": "...",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00,
      "description": "Professional plan"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00,
      "description": "Basic plan"
    }
  ]
}
```

**Verify**:
- [ ] Status is 200 OK
- [ ] Plans array is not empty
- [ ] Each plan has: id, handle, name, price, description
- [ ] Prices match Maxio seeded plans

### Step 5: Test Create Subscription (Hero Flow)

```bash
curl -X POST https://localhost:27403/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k
```

**Expected Response** (200 OK):
```json
{
  "correlationId": "...",
  "subscriptionId": 12345678,
  "customerId": 98765432,
  "state": "active",
  "activatedAt": "2026-09-07T12:34:56Z",
  "nextBillingDate": "2026-10-07T12:34:56Z",
  "monthlyPrice": 299.00
}
```

**Verify**:
- [ ] Status is 200 OK (not 400 or 500)
- [ ] subscriptionId is a positive integer
- [ ] customerId is a positive integer
- [ ] state is "active"
- [ ] activatedAt is a valid ISO 8601 datetime
- [ ] nextBillingDate is a valid ISO 8601 datetime
- [ ] monthlyPrice matches the plan price (299.00 for pro plan)
- [ ] **Idempotency Check**: Call again with same token - should still succeed and return same or different subscription

### Step 6: Test Get My Subscriptions

```bash
curl -X GET https://localhost:27403/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  -k
```

**Expected Response** (200 OK):
```json
{
  "correlationId": "...",
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "activatedAt": "2026-09-07T12:34:56Z",
      "nextBillingDate": "2026-10-07T12:34:56Z"
    }
  ]
}
```

**Verify**:
- [ ] Status is 200 OK
- [ ] subscriptions array contains at least the subscription created in Step 5
- [ ] Each subscription has: id, state, activatedAt, nextBillingDate
- [ ] IDs match what was returned from Step 5

### Step 7: Test Authentication Requirement

Try endpoints WITHOUT Authorization header:

```bash
curl -X GET https://localhost:27403/api/subscription-plans \
  -H "Accept: application/json" \
  -k
```

**Expected Response** (401 Unauthorized):
```json
{"type":"https://tools.ietf.org/html/rfc7235#section-3.1","title":"Unauthorized","status":401}
```

**Verify**:
- [ ] Status is 401 Unauthorized
- [ ] Error message is appropriate

## Error Scenario Testing

### Scenario 1: Invalid Product Handle

```bash
curl -X POST https://localhost:27403/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"invalid-plan-handle"}' \
  -k
```

**Expected**: 400 Bad Request with error message
**Verify**:
- [ ] Status is 400 or 500 (appropriate error code)
- [ ] Response contains error message explaining the issue

### Scenario 2: Missing Maxio Credentials

(Temporarily unset MAXIO_API_KEY)

```bash
# Restart API without credentials
dotnet run --environment Development
```

**Expected**: Startup error or 500 when calling subscription endpoints
**Verify**:
- [ ] Error indicates missing credentials
- [ ] Application indicates configuration issue

## Maxio Dashboard Verification

Log into Maxio sandbox at: https://app.chargify.com

**Verify**:
- [ ] Customers created (one for each subscription call)
- [ ] Subscriptions created in correct product family
- [ ] Customer emails match test user email
- [ ] Subscriptions show as "active"
- [ ] Plan assignments are correct

## Security Verification

- [ ] JWT token required for all subscription endpoints
- [ ] Secrets never logged or output
- [ ] User-secrets used instead of appsettings for credentials
- [ ] Credentials in .gitignore (verify not in git)
- [ ] HTTP -> HTTPS redirect configured
- [ ] CORS properly configured

## Performance Verification

- [ ] API responds to requests within 2 seconds
- [ ] No N+1 query issues
- [ ] Maxio API calls are efficient

## Production Readiness Checklist

- [ ] Error handling covers all Maxio API failure scenarios
- [ ] Logging added for debugging (optional enhancement)
- [ ] Database migration path identified (optional enhancement)
- [ ] Rate limiting considered (optional enhancement)
- [ ] Subscription cancellation endpoint considered (optional enhancement)
- [ ] Webhook event handling considered (optional enhancement)

## Summary

After completing this checklist:
- ✅ Integration builds successfully
- ✅ Endpoints are discoverable via Swagger
- ✅ Hero flow works (subscribe, list, confirm)
- ✅ Authentication is enforced
- ✅ Idempotency works (duplicate subscriptions handled)
- ✅ Error scenarios are handled gracefully
- ✅ Maxio API integration is confirmed
- ✅ Ready for user testing and production deployment

## Troubleshooting Guide

| Issue | Cause | Solution |
|-------|-------|----------|
| Build fails | Missing dependencies | Run `dotnet restore` |
| 401 on endpoints | No JWT token | Get token from authenticate endpoint |
| "Customer not found" | Wrong Maxio creds | Verify user-secrets: `dotnet user-secrets list` |
| Swagger doesn't show endpoints | Endpoints not registered | Verify `app.MapEndpoints()` in Program.cs |
| 500 on create subscription | Maxio API error | Check Maxio credentials and network |
| MonthlyPrice is 0 | Product not found | Verify plan handle is correct |

## Next Steps After Verification

1. ✅ Confirm all tests pass
2. ✅ Commit changes to git
3. ✅ Push to main branch
4. ✅ Deploy to staging environment
5. ✅ Run integration tests
6. ✅ Perform user acceptance testing (UAT)
7. ✅ Deploy to production
