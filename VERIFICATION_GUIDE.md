# Maxio Subscription Billing - Verification Guide

This guide provides step-by-step instructions to verify the Maxio subscription billing integration is working correctly.

## Pre-Verification Checklist

Before testing, ensure you have:

- [ ] Maxio Advanced Billing sandbox account with site subdomain
- [ ] API credentials (API Key from Maxio dashboard)
- [ ] Product family handle set up in Maxio (e.g., `eshop-subscribe`)
- [ ] At least one subscription product (e.g., `eshop-pro`) in that family
- [ ] .NET 8.0+ SDK installed
- [ ] HTTPS dev certificate trusted (`dotnet dev-certs https --check`)

## Step 1: Prepare Environment

### Option A: Using Environment Variables (Recommended)

```powershell
# PowerShell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

### Option B: Using .NET User Secrets (Development Only)

```bash
cd src/PublicApi

# Initialize user secrets (if not already done)
dotnet user-secrets init

# Set secrets
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

## Step 2: Build the Solution

```bash
cd repo/src
dotnet build PublicApi/PublicApi.csproj --configuration Debug
```

**Expected Result**: Build succeeds with only NuGet vulnerability warnings (expected for System.Text.Json).

## Step 3: Start the Application

```bash
cd repo/src/PublicApi
dotnet run --configuration Debug
```

**Expected Output**:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:24363
```

Keep this terminal open for testing.

## Step 4: Authenticate

Open a new terminal and authenticate:

```bash
# Get a token
curl -X POST https://localhost:24363/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word123"}' \
  -k
```

**Expected Response**:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "result": true,
  "username": "demouser@microsoft.com"
}
```

Save the `token` value for the next tests.

## Step 5: Test List Subscription Plans Endpoint

### Request

```bash
# Replace YOUR_TOKEN with the token from Step 4
curl https://localhost:24363/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

### Expected Response

```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "$299 Plan",
      "handle": "eshop-pro",
      "description": "Professional plan",
      "price": 299.0,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Verification Checklist

- [ ] Status code is 200
- [ ] Response includes at least one plan
- [ ] Each plan has: id, name, handle, price, interval, intervalUnit
- [ ] Price is in dollars (PriceInCents / 100)

## Step 6: Test Create Subscription Endpoint

### Request

```bash
curl -X POST https://localhost:24363/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{"productHandle":"eshop-pro"}' \
  -k
```

### Expected Response

```json
{
  "subscription": {
    "id": 123456789,
    "state": "active",
    "productHandle": "eshop-pro",
    "customerId": 987654321,
    "currentPeriodEndsAt": "2024-10-06T14:30:00Z",
    "nextAssessmentAt": "2024-10-06T14:30:00Z",
    "activatedAt": "2024-09-06T14:30:00Z",
    "createdAt": "2024-09-06T14:30:00Z"
  }
}
```

### Verification Checklist

- [ ] Status code is 201 Created
- [ ] Subscription has a valid ID
- [ ] Subscription state is "active"
- [ ] CustomerId is present (customer was created)
- [ ] Dates are in ISO 8601 format

### Maxio Dashboard Verification

1. Log into your Maxio sandbox
2. Go to Customers
3. Search for "demouser@microsoft.com"
4. Verify customer was created with reference = user ID
5. Click on the customer and verify subscription is listed with status "Active"

## Step 7: Test Get My Subscriptions Endpoint

### Request

```bash
curl https://localhost:24363/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

### Expected Response

```json
{
  "subscriptions": [
    {
      "id": 123456789,
      "state": "active",
      "productHandle": "eshop-pro",
      "customerId": 987654321,
      "currentPeriodEndsAt": "2024-10-06T14:30:00Z",
      "nextAssessmentAt": "2024-10-06T14:30:00Z",
      "activatedAt": "2024-09-06T14:30:00Z",
      "createdAt": "2024-09-06T14:30:00Z"
    }
  ]
}
```

### Verification Checklist

- [ ] Status code is 200
- [ ] Subscriptions array includes the subscription created in Step 6
- [ ] Subscription details match what was created

## Step 8: Test Subscription to Different Plan

### Request

```bash
curl -X POST https://localhost:24363/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{"productHandle":"basic-plan"}' \
  -k
```

### Expected Result

- [ ] Status code is 201 Created
- [ ] A second subscription is created
- [ ] Same customerId as first subscription (reusing existing customer)

### Verify

Calling Get My Subscriptions again should now return 2 subscriptions.

## Step 9: Test Error Cases

### Missing Product Handle

```bash
curl -X POST https://localhost:24363/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{"productHandle":""}' \
  -k
```

**Expected**: 400 Bad Request with error message

### Invalid Product Handle

```bash
curl -X POST https://localhost:24363/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{"productHandle":"nonexistent-product"}' \
  -k
```

**Expected**: 400 Bad Request with error from Maxio API

### Missing Authorization

```bash
curl https://localhost:24363/api/subscription-plans \
  -k
```

**Expected**: 401 Unauthorized

## Step 10: Production Readiness Checks

### Code Quality

- [ ] No hardcoded API keys or secrets in code
- [ ] All configuration comes from environment variables
- [ ] Error messages are informative but don't leak sensitive data
- [ ] Logging is appropriate (no sensitive data logged)

### Security

- [ ] JWT authentication is enforced on all subscription endpoints
- [ ] HTTPS is required (no plain HTTP)
- [ ] API key is transmitted via Basic Auth over HTTPS
- [ ] Customer reference uses user ID (not email)
- [ ] No cross-user data leakage (user can only access their own subscriptions)

### Architecture

- [ ] Maxio client is properly abstracted behind `IMaxioClient` interface
- [ ] Business logic is in `ISubscriptionService` service
- [ ] Endpoints follow existing eShopOnWeb patterns
- [ ] Dependency injection is properly configured
- [ ] Error handling is consistent

### Integration Points

- [ ] Configuration section: `Maxio:*` keys in appsettings
- [ ] Environment variable mapping works correctly
- [ ] Base URL construction (from subdomain or override) is correct
- [ ] Request/Response DTOs match Maxio API spec

## Troubleshooting

### "Failed to load subscription plans: HTTP error..."

**Cause**: API credentials or endpoint incorrect

**Fix**:
1. Verify API key is correct
2. Verify subdomain matches your Maxio site
3. Check Maxio API status
4. Ensure network can reach Maxio API

### "Failed to create customer: ... cannot be blank"

**Cause**: Missing required customer fields

**Fix**:
1. Verify email is in authentication token
2. Verify token is being parsed correctly
3. Check that customer attributes are being set in request

### "Subscription state not 'active'"

**Cause**: Subscription created but not activated

**Fix**:
1. Check if subscription requires payment
2. Verify product settings in Maxio (should have `payment_collection_method: remittance`)
3. Check Maxio logs for subscription creation details

### In-Memory Database Resets

**Symptom**: Customer is created but doesn't appear on next run

**Cause**: Using in-memory database

**Fix**: 
1. Use SQL Server for persistent testing
2. Or re-run subscription creation on each app restart

## Summary

Once all steps pass, the Maxio subscription billing integration is ready for:

1. **Frontend Integration**: UI can now call `/api/subscription-plans` and `/api/subscriptions`
2. **Customer Testing**: Sandbox subscriptions are created with real Maxio data
3. **Production Deployment**: Environment variables control credentials, no code changes needed

All endpoints are:
- ✅ Properly authenticated with JWT
- ✅ Integrated with Maxio Advanced Billing API
- ✅ Following eShopOnWeb conventions
- ✅ Production-grade error handling
- ✅ Fully documented
