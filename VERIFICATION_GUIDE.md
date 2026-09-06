# Maxio Subscription Billing Integration - Verification Guide

This guide provides step-by-step instructions to verify the complete Maxio subscription billing integration.

## Pre-Flight Checks ✓

- [x] **Build Verification**: Entire solution (`eShopOnWeb.sln`) compiles with zero errors
- [x] **Application Startup**: PublicApi launches successfully on configured ports
- [x] **Endpoint Registration**: All three subscription endpoints are registered and accessible
- [x] **Database**: In-memory database initialized on startup

## Step-by-Step Verification

### Part 1: Prepare Environment

**1.1 Get Maxio Sandbox Credentials**

From your Maxio sandbox account (site `cp-exp-2`), obtain:
- API Key (from Settings → API Tokens)
- Site Subdomain (e.g., "cp-exp-2")
- Product Family Handle: `eshop-subscribe`

**1.2 Set Environment Variables**

Replace values with your actual Maxio credentials:

```bash
# Windows PowerShell
$env:MAXIO_API_KEY = "your-actual-api-key-here"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"

# Or Windows Command Prompt
set MAXIO_API_KEY=your-actual-api-key-here
set MAXIO_SITE_SUBDOMAIN=cp-exp-2
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
set UseOnlyInMemoryDatabase=true
set DOTNET_ROLL_FORWARD=Major

# Or Linux/macOS
export MAXIO_API_KEY="your-actual-api-key-here"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
export DOTNET_ROLL_FORWARD="Major"
```

### Part 2: Run and Test the Application

**2.1 Start PublicApi**

From the repository root:
```bash
dotnet run --project src/PublicApi/PublicApi.csproj
```

Expected output:
```
info: PublicApi[0]
      PublicApi App created...
info: PublicApi[0]
      Seeding Database...
info: PublicApi[0]
      LAUNCHING PublicApi
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:27383
```

The API will be available at `https://localhost:27383`

**2.2 Test Endpoint 1: Get Subscription Plans (Unauthenticated)**

Open PowerShell or terminal and run:

```powershell
# PowerShell
$response = Invoke-WebRequest -Uri "https://localhost:27383/api/subscription-plans" `
  -Method GET `
  -Headers @{"Accept" = "application/json"} `
  -SkipCertificateCheck
Write-Host $response.Content
```

Or using curl:
```bash
curl -k https://localhost:27383/api/subscription-plans \
  -H "Accept: application/json"
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": null,
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": null,
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

**2.3 Test Endpoint 2: Authenticate and Get JWT Token**

```powershell
$authResponse = Invoke-WebRequest -Uri "https://localhost:27383/api/authenticate" `
  -Method POST `
  -Headers @{"Content-Type" = "application/json"} `
  -Body '{"username":"demouser@microsoft.com","password":"Pass@word1"}' `
  -SkipCertificateCheck
$authData = $authResponse.Content | ConvertFrom-Json
$token = $authData.token
Write-Host "Token: $token"
```

Or using curl:
```bash
curl -k -X POST https://localhost:27383/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

**Expected Response:**
```json
{
  "token": "eyJhbGc...",
  "result": true,
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "username": "demouser@microsoft.com",
  "correlationId": "..."
}
```

Save the `token` value for the next steps.

**2.4 Test Endpoint 3: Create Subscription (Authenticated)**

```powershell
$subscriptionResponse = Invoke-WebRequest -Uri "https://localhost:27383/api/subscriptions" `
  -Method POST `
  -Headers @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
  } `
  -Body '{"productHandle":"eshop-pro"}' `
  -SkipCertificateCheck
Write-Host $subscriptionResponse.Content
```

Or using curl:
```bash
curl -k -X POST https://localhost:27383/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

**Expected Response:**
```json
{
  "subscription": {
    "id": 123456789,
    "state": "active",
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "nextBillingAt": "2026-10-07T00:00:00Z",
    "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
    "createdAt": "2026-09-07T14:30:00Z"
  }
}
```

**2.5 Test Endpoint 4: Get User's Subscriptions (Authenticated)**

```powershell
$subscriptionsResponse = Invoke-WebRequest -Uri "https://localhost:27383/api/my-subscriptions" `
  -Method GET `
  -Headers @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
  } `
  -SkipCertificateCheck
Write-Host $subscriptionsResponse.Content
```

Or using curl:
```bash
curl -k https://localhost:27383/api/my-subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Accept: application/json"
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 123456789,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "nextBillingAt": "2026-10-07T00:00:00Z",
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T14:30:00Z"
    }
  ]
}
```

### Part 3: Verify Key Features

**3.1 Idempotent Customer Creation**

Run the create subscription request twice with the same token:
```bash
# First request
curl -k -X POST https://localhost:27383/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"basic-plan"}'

# Second request (same data)
curl -k -X POST https://localhost:27383/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"basic-plan"}'
```

**Expected Behavior:**
- First request: Creates subscription successfully
- Second request: Creates new subscription (different product) without creating duplicate customer
- In Maxio dashboard: Single customer entry for the eShop user, multiple subscriptions

**3.2 Verify Authentication**

Test the protected endpoint without a token:
```bash
curl -k https://localhost:27383/api/my-subscriptions
```

**Expected Response:** `401 Unauthorized`

Test with an invalid token:
```bash
curl -k https://localhost:27383/api/my-subscriptions \
  -H "Authorization: Bearer invalid.token.here"
```

**Expected Response:** `401 Unauthorized`

### Part 4: Verify in Maxio Dashboard

1. Log in to your Maxio sandbox (https://cp-exp-2.chargify.com)
2. Navigate to **Customers**
3. Look for customer with reference pattern: `eshop-demouser`
4. Verify subscriptions are listed under that customer with:
   - Correct product (Pro Plan or Basic Plan)
   - State: Active
   - Next Billing Date: 30 days from subscription date

## Success Criteria

✓ All three endpoints are accessible  
✓ Subscription plans endpoint returns list of plans from Maxio  
✓ Authentication endpoint returns valid JWT tokens  
✓ Create subscription endpoint creates subscriptions in Maxio  
✓ Get subscriptions endpoint retrieves user's subscriptions  
✓ Customers are created idempotently (no duplicates)  
✓ Protected endpoints reject requests without valid JWT  
✓ Changes are reflected in Maxio dashboard  

## Troubleshooting

**Issue: "Could not connect to Maxio"**
- Verify `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN` environment variables are set correctly
- Check that your Maxio sandbox is active and accessible
- Verify the API key has not been revoked

**Issue: "Product family not found"**
- Verify `MAXIO_DEFAULT_PRODUCT_FAMILY` is set to `eshop-subscribe`
- Confirm the product family exists in your Maxio sandbox
- Check the product family handle in Maxio dashboard (Settings → Catalog → Product Families)

**Issue: "Customer with reference already exists"**
- This should not occur; the service checks for existing customers before creating
- If it happens, verify the customer lookup is working (check logs)

**Issue: 401 Unauthorized on protected endpoints**
- Verify you're passing the JWT token in the `Authorization: Bearer <token>` header
- Verify the token is still valid (tokens expire after 7 days)
- Get a fresh token using the authenticate endpoint

## Code Review Checklist

- [x] MaxioBillingService correctly implements IMaxioBillingService interface
- [x] All three endpoints follow Ardalis.ApiEndpoints pattern
- [x] Configuration uses environment variables (never hardcoded secrets)
- [x] Error handling returns appropriate HTTP status codes
- [x] JWT authentication is enforced on protected endpoints
- [x] Customer creation is idempotent
- [x] JSON parsing handles nullable fields correctly
- [x] Application builds without errors
- [x] All dependencies are properly injected

## Files Modified

1. **src/ApplicationCore/MaxioSettings.cs** - New configuration class
2. **src/Infrastructure/Services/MaxioBillingService.cs** - New API client (~450 lines)
3. **src/PublicApi/SubscriptionEndpoints/*.cs** - Three new endpoints
4. **src/PublicApi/Program.cs** - Service registration and configuration
5. **src/PublicApi/appsettings.json** - Maxio config section added
6. **global.json** - SDK rollForward updated
7. **MAXIO_INTEGRATION.md** - Integration documentation

Total: ~900 lines of new production-grade code
