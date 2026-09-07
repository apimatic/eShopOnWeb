# Verification Guide: Maxio Subscription Billing Integration

This guide walks you through verifying that the subscription billing integration is properly implemented and working.

## Quick Start Verification (5 minutes)

### 1. Build the Solution

```bash
cd C:\claude-runs\t1h45ali-openapi-haiku45high-026\repo
dotnet build eShopOnWeb.sln
```

✅ **Expected**: Build succeeds with 0 errors

### 2. Verify File Structure

Check that all subscription-related files exist:

```bash
# Maxio integration files
src/PublicApi/Maxio/
  - MaxioSettings.cs
  - MaxioApiClient.cs
  - ISubscriptionService.cs
  - SubscriptionService.cs
  - MaxioBillingDbContext.cs
  - MaxioCustomerMapping.cs

# API Endpoints
src/PublicApi/SubscriptionEndpoints/
  - SubscriptionEndpoints.cs
  - SubscriptionDto.cs
  - SubscriptionPlanDto.cs

# Configuration
src/PublicApi/appsettings.json     (contains Maxio section)
src/PublicApi/appsettings.Development.json (UseOnlyInMemoryDatabase: true)

# Setup script
setup-maxio-secrets.ps1

# Documentation
SUBSCRIPTION_BILLING_INTEGRATION.md
VERIFY_INTEGRATION.md (this file)
```

### 3. Check Configuration

Verify that appsettings.json contains the Maxio configuration section:

```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "",
    "Environment": "sandbox",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": ""
  }
}
```

✅ **Expected**: Configuration section exists with correct structure

### 4. Verify Endpoint Registration

Check that Program.cs properly registers subscription endpoints:

```csharp
// In Program.cs:
app.MapSubscriptionEndpoints();  // Added after app.MapEndpoints()
```

✅ **Expected**: Extension method called in Program.cs

### 5. Check JWT Claims Enhancement

Verify that IdentityTokenClaimService includes user ID and email:

```csharp
// In IdentityTokenClaimService.GetTokenAsync():
var claims = new List<Claim>
{
    new Claim(ClaimTypes.Name, userName),
    new Claim(ClaimTypes.NameIdentifier, user.Id),  // User ID
    new Claim(ClaimTypes.Email, user.Email ?? string.Empty)  // Email
};
```

✅ **Expected**: All three claims are added to the token

## Full Integration Test (requires Maxio credentials)

### Prerequisites

1. Maxio Advanced Billing sandbox account
2. Create a product family with handle: `eshop-subscribe`
3. Create at least one product (plan) in that family
4. Get your Maxio API key and site subdomain

### Step 1: Set Environment Variables

```powershell
$env:MAXIO_API_KEY = "your_api_key_here"
$env:MAXIO_SITE_SUBDOMAIN = "your_site_subdomain"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

### Step 2: Initialize User-Secrets

```powershell
.\setup-maxio-secrets.ps1
```

✅ **Expected**: Script outputs "Maxio secrets configured successfully!"

### Step 3: Start the Application

```bash
cd src/PublicApi
dotnet run
```

✅ **Expected**: 
- Application starts without errors
- Swagger UI available at https://localhost:28363/swagger
- Subscription endpoints visible in Swagger under "SubscriptionEndpoints" tag

### Step 4: Test GET /api/subscription-plans

```bash
curl -X GET "https://localhost:28363/api/subscription-plans" \
  -H "Accept: application/json" \
  --insecure
```

✅ **Expected Response (200 OK)**:
```json
{
  "correlationId": "uuid",
  "plans": [
    {
      "handle": "plan-handle",
      "name": "Plan Name",
      "price": 29.99,
      "currency": "USD",
      "billingCycle": "1 month(s)"
    }
  ]
}
```

### Step 5: Get JWT Token

```bash
curl -X POST "https://localhost:28363/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word123"}' \
  --insecure
```

✅ **Expected Response (200 OK)**:
```json
{
  "username": "demouser@microsoft.com",
  "token": "eyJhbGc...",
  "result": true
}
```

Save the token value for the next test.

### Step 6: Test POST /api/subscriptions

```bash
# Replace TOKEN_HERE with the actual token from Step 5
curl -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure
```

✅ **Expected Response (201 Created)**:
```json
{
  "correlationId": "uuid",
  "subscriptionId": 12345678,
  "state": "active",
  "planName": "Plan Name",
  "planHandle": "eshop-pro",
  "price": 29.99,
  "billingCycle": "month",
  "currentPeriodEndsAt": "2026-10-07T...",
  "nextAssessmentAt": "2026-10-07T...",
  "activatedAt": "2026-09-07T...",
  "createdAt": "2026-09-07T..."
}
```

### Step 7: Test GET /api/my-subscriptions

```bash
curl -X GET "https://localhost:28363/api/my-subscriptions" \
  -H "Authorization: Bearer TOKEN_HERE" \
  -H "Accept: application/json" \
  --insecure
```

✅ **Expected Response (200 OK)**:
```json
{
  "correlationId": "uuid",
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "planName": "Plan Name",
      "planHandle": "eshop-pro",
      "price": 29.99,
      "billingCycle": "month",
      "currentPeriodEndsAt": "2026-10-07T...",
      "nextAssessmentAt": "2026-10-07T...",
      "activatedAt": "2026-09-07T...",
      "createdAt": "2026-09-07T..."
    }
  ]
}
```

### Step 8: Verify Idempotent Customer Creation

Create another subscription with the same user. The Maxio customer should reuse the existing customer (not create a duplicate).

```bash
curl -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"basic-plan"}' \
  --insecure
```

✅ **Expected**: 
- New subscription created successfully
- Only one Maxio customer exists for this user
- Different subscription IDs but same customer ID

Check Maxio admin console to verify only one customer was created.

## Error Scenarios to Test

### Missing Authorization Header

```bash
curl -X POST "https://localhost:28363/api/subscriptions" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure
```

✅ **Expected Response (401 Unauthorized)**

### Invalid JWT Token

```bash
curl -X GET "https://localhost:28363/api/my-subscriptions" \
  -H "Authorization: Bearer INVALID_TOKEN" \
  --insecure
```

✅ **Expected Response (401 Unauthorized)**

### Missing Plan Handle

```bash
curl -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{}' \
  --insecure
```

✅ **Expected Response (400 Bad Request)**:
```json
{
  "error": "Plan handle is required"
}
```

### Invalid Plan Handle

```bash
curl -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"nonexistent-plan"}' \
  --insecure
```

✅ **Expected Response (500 Internal Server Error)** - Maxio API returns error, appropriately propagated

## Code Quality Checks

### 1. No Secrets in Code

Verify that no sensitive data is committed:

```bash
# Check for hardcoded keys or secrets
git log --all -p | grep -i "MAXIO_API_KEY\|api.key\|secret" | grep -v "Binary"
```

✅ **Expected**: No results (only setup scripts and docs referencing env var names)

### 2. Build Warnings

```bash
dotnet build src/PublicApi/PublicApi.csproj 2>&1 | grep "warning CS"
```

✅ **Expected**: No C# compiler warnings (only dependency vulnerability warnings are acceptable)

### 3. API Endpoint Documentation

```bash
curl -s https://localhost:28363/swagger/v1/swagger.json --insecure | jq '.paths | keys[] | select(contains("subscription"))'
```

✅ **Expected**:
```
/api/subscription-plans
/api/subscriptions
/api/my-subscriptions
```

## Database Verification

### Check In-Memory Database Initialization

The application should initialize MaxioBillingDbContext and create the schema:

```csharp
// In Program.cs seeding section:
var maxioBillingContext = scopedProvider.GetRequiredService<MaxioBillingDbContext>();
await maxioBillingContext.Database.EnsureCreatedAsync();
```

✅ **Expected**: Application logs indicate successful database creation

### Verify Customer Mapping Storage

After creating a subscription, check that the user-to-customer mapping is stored:

```bash
# This would require accessing the DbContext directly, typically via:
# 1. Database explorer if using SQL Server
# 2. Logging table contents if using in-memory
```

✅ **Expected**: MaxioCustomerMappings table contains the user ID and Maxio customer ID

## Production Readiness Checklist

- [ ] All compilation errors resolved
- [ ] No hardcoded secrets in code
- [ ] User-secrets setup script works
- [ ] Endpoints properly authenticated (require JWT)
- [ ] Configuration follows security best practices
- [ ] Error handling returns appropriate HTTP status codes
- [ ] DTOs properly map Maxio API responses
- [ ] Customer creation is idempotent
- [ ] API documentation generated (Swagger)
- [ ] Build succeeds with no compiler warnings
- [ ] Integration guide complete and tested
- [ ] Setup script functional

## Troubleshooting

### Build Fails with "The type or namespace name 'DateTime' could not be found"

**Cause**: Missing `using System;` statements
**Solution**: Check all new .cs files have required using statements

### Endpoints Not Showing in Swagger

**Cause**: Endpoints not registered in Program.cs
**Solution**: Ensure `app.MapSubscriptionEndpoints();` is called in Program.cs

### 401 Errors on Authenticated Endpoints

**Cause**: JWT token missing required claims
**Solution**: Verify IdentityTokenClaimService includes NameIdentifier and Email claims

### Maxio API Connection Errors

**Cause**: Incorrect credentials or subdomain
**Solution**: 
1. Verify environment variables are set correctly
2. Check credentials in Maxio admin console
3. Test API key using curl directly against Maxio

## Next Steps

Once verification is complete:

1. Commit all changes to the branch
2. Create a pull request with the integration changes
3. Deploy to staging environment for end-to-end testing
4. Implement additional features:
   - Subscription cancellation
   - Upgrade/downgrade functionality
   - Maxio webhook handlers
   - Usage-based billing
   - Invoicing and payment history

---

**Integration Date**: September 7, 2026
**Status**: Complete and Ready for Testing
