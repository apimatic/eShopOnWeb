# Maxio Subscription Billing Integration - Verification Guide

This guide provides step-by-step instructions to verify that the Maxio subscription billing integration for eShopOnWeb is working correctly.

## Prerequisites

1. **Environment Variables**: Set the following environment variables with your Maxio sandbox credentials:
   - `MAXIO_API_KEY`: Your Maxio API key
   - `MAXIO_SITE_SUBDOMAIN`: Your Maxio site subdomain (e.g., "cp-exp-4")
   - `MAXIO_ENVIRONMENT`: Maxio environment (e.g., "sandbox")
   - `MAXIO_DEFAULT_PRODUCT_FAMILY`: Default product family handle (e.g., "eshop-subscribe")

2. **Maxio Sandbox Setup**: The following entities should already exist in your Maxio sandbox:
   - Product Family: `eshop-subscribe`
   - Plans:
     - `eshop-pro`: $299.00/mo (Pro Plan)
     - `basic-plan`: $29.00/mo (Basic Plan)
   - Metered Component: `api-call` ($0.01/unit)

3. **.NET Environment**: 
   - .NET 8.0 SDK or later installed
   - `DOTNET_ROLL_FORWARD=Major` environment variable set
   - `UseOnlyInMemoryDatabase=true` environment variable set (for development/testing)

## Architecture Overview

### New Components

1. **MaxioSettings** (`src/ApplicationCore/MaxioSettings.cs`)
   - Configuration class for Maxio API credentials
   - Loads from `Maxio:` section in appsettings.json or environment variables

2. **IMaxioBillingService & MaxioBillingService** 
   - Interface: `src/ApplicationCore/Interfaces/IMaxioBillingService.cs`
   - Implementation: `src/Infrastructure/Services/MaxioBillingService.cs`
   - Handles all communication with Maxio API using HTTP Basic Auth

3. **Subscription Endpoints**
   - `src/PublicApi/SubscriptionEndpoints/SubscriptionPlansListEndpoint.cs`
   - `src/PublicApi/SubscriptionEndpoints/SubscriptionCreateEndpoint.cs`
   - `src/PublicApi/SubscriptionEndpoints/SubscriptionListEndpoint.cs`

4. **Database Modifications**
   - Added `MaxioCustomerId` field to `ApplicationUser` entity
   - Migration: `src/Infrastructure/Identity/Migrations/20260907000000_AddMaxioCustomerId.cs`

### Integration Points

- **User-Maxio Mapping**: The `MaxioCustomerId` field on `ApplicationUser` stores the Maxio customer ID
- **Idempotent Customer Creation**: Uses the user's ID as the Maxio customer reference to prevent duplicate customers
- **JWT Authentication**: All subscription endpoints are JWT-protected using the same authentication as other PublicApi endpoints

## API Endpoints

### 1. Get Subscription Plans
**Endpoint**: `GET /api/subscription-plans`
**Authentication**: Required (Bearer token)
**Description**: Lists all available subscription plans from Maxio

**Example Request**:
```bash
curl -X GET "https://localhost:27503/api/subscription-plans" \
  -H "Authorization: Bearer <JWT_TOKEN>" \
  -H "Content-Type: application/json"
```

**Example Response**:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription plan",
      "price": 299.00
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic subscription plan",
      "price": 29.00
    }
  ],
  "success": true,
  "correlationId": "..."
}
```

### 2. Create Subscription
**Endpoint**: `POST /api/subscriptions`
**Authentication**: Required (Bearer token)
**Description**: Creates a new subscription for the authenticated user

**Request Body**:
```json
{
  "productHandle": "eshop-pro"
}
```

**Example Request**:
```bash
curl -X POST "https://localhost:27503/api/subscriptions" \
  -H "Authorization: Bearer <JWT_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}'
```

**Example Response**:
```json
{
  "success": true,
  "subscriptionId": 12345,
  "customerId": 98765,
  "state": "active",
  "nextBillingDate": "2026-10-07T00:00:00Z",
  "correlationId": "..."
}
```

**Error Cases**:
- `401 Unauthorized`: No valid JWT token provided
- `404 Not Found`: User not found in the system
- `400 Bad Request`: User already has an active subscription or other validation errors

### 3. Get My Subscriptions
**Endpoint**: `GET /api/my-subscriptions`
**Authentication**: Required (Bearer token)
**Description**: Gets all subscriptions for the authenticated user

**Example Request**:
```bash
curl -X GET "https://localhost:27503/api/my-subscriptions" \
  -H "Authorization: Bearer <JWT_TOKEN>" \
  -H "Content-Type: application/json"
```

**Example Response**:
```json
{
  "success": true,
  "subscriptions": [
    {
      "id": 12345,
      "customerId": 98765,
      "productId": 7126957,
      "state": "active",
      "currentPeriodStartsAt": "2026-09-07T00:00:00Z",
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
      "nextAssessmentAt": "2026-10-07T00:00:00Z",
      "totalRecurringCustom": 299.00
    }
  ],
  "correlationId": "..."
}
```

## Step-by-Step Verification Procedure

### Step 1: Set Environment Variables
```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
$env:MAXIO_API_KEY = "<your-api-key>"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-4"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

### Step 2: Build the Project
```powershell
dotnet build eShopOnWeb.sln -c Release
```

### Step 3: Run the PublicApi Service
```powershell
dotnet run --project src/PublicApi -c Release
```
The API will start on `https://localhost:27503`

### Step 4: Get an Authentication Token
First, authenticate a user using the existing authentication endpoint:

```powershell
$response = Invoke-RestMethod -Uri "https://localhost:27503/api/authenticate" `
  -Method Post `
  -ContentType "application/json" `
  -SkipCertificateCheck `
  -Body @{
    username = "demouser@example.com"
    password = "Pass123!"
  } | ConvertFrom-Json

$token = $response.token
Write-Host "Token: $token"
```

**Note**: Use an existing user from the seeded database. If you need to create a user first, use the Web application.

### Step 5: Test Get Subscription Plans
```powershell
$headers = @{
  "Authorization" = "Bearer $token"
  "Content-Type" = "application/json"
}

$plans = Invoke-RestMethod -Uri "https://localhost:27503/api/subscription-plans" `
  -Method Get `
  -Headers $headers `
  -SkipCertificateCheck

$plans | ConvertTo-Json | Write-Host
```

**Expected Result**: Returns a list of available subscription plans with their details.

### Step 6: Test Create Subscription
```powershell
$subscriptionBody = @{
  productHandle = "eshop-pro"
} | ConvertTo-Json

$subscription = Invoke-RestMethod -Uri "https://localhost:27503/api/subscriptions" `
  -Method Post `
  -Headers $headers `
  -Body $subscriptionBody `
  -SkipCertificateCheck

$subscription | ConvertTo-Json | Write-Host
```

**Expected Result**: Returns subscription details including:
- `subscriptionId`: The Maxio subscription ID
- `customerId`: The Maxio customer ID
- `state`: Should be "active"
- `nextBillingDate`: Date of next billing cycle

**Verify in Maxio**: Log into your Maxio sandbox admin panel and verify that:
1. A new customer was created with your user's email
2. An active subscription exists for that customer with the selected plan
3. The customer reference matches your eShopOnWeb user ID

### Step 7: Test Get My Subscriptions
```powershell
$mySubscriptions = Invoke-RestMethod -Uri "https://localhost:27503/api/my-subscriptions" `
  -Method Get `
  -Headers $headers `
  -SkipCertificateCheck

$mySubscriptions | ConvertTo-Json | Write-Host
```

**Expected Result**: Returns an array containing the subscription created in Step 6, with:
- All subscription details
- Billing period information
- Current state

### Step 8: Test Idempotency
Try to create another subscription for the same user with a different plan:

```powershell
$subscriptionBody2 = @{
  productHandle = "basic-plan"
} | ConvertTo-Json

$result = Invoke-RestMethod -Uri "https://localhost:27503/api/subscriptions" `
  -Method Post `
  -Headers $headers `
  -Body $subscriptionBody2 `
  -SkipCertificateCheck -ErrorAction SilentlyContinue
```

**Expected Result**: Should return an error message indicating "User already has an active subscription" (or similar), preventing duplicate subscriptions.

### Step 9: Verify Database State
Connect to the in-memory database (if using a real database, connect via SQL Server Management Studio):

```csharp
// In debug mode, you can inspect the ApplicationUser entity
var user = userManager.FindByNameAsync("demouser@example.com").Result;
var maxioCustomerId = user.MaxioCustomerId; // Should be populated with the Maxio customer ID
```

**Expected Result**: The `MaxioCustomerId` field on the user should contain the Maxio customer ID.

## Troubleshooting

### 1. 401 Unauthorized on Protected Endpoints
**Cause**: No valid JWT token or token has expired
**Solution**: Get a fresh token using the authenticate endpoint

### 2. 400 Bad Request from Maxio Service
**Cause**: Invalid Maxio credentials or API configuration
**Solution**: 
- Verify environment variables are set correctly
- Check that the Maxio API key has appropriate permissions
- Ensure the subdomain is correct

### 3. "User not found" Error
**Cause**: Username doesn't match any user in the database
**Solution**: Use an existing user or create a new one through the Web application

### 4. "User already has an active subscription" Error
**Cause**: User has already created a subscription
**Solution**: Use a different user account to test, or test in a fresh instance with UseOnlyInMemoryDatabase=true

### 5. Maxio Customer Creation Fails with 422 Error
**Cause**: Customer with the same reference (user ID) already exists in Maxio
**Solution**: 
- Try creating a subscription for a different user
- Or delete the customer from Maxio sandbox and retry
- Check the reference field uniqueness in Maxio

## Security Notes

1. **No Hardcoded Secrets**: All Maxio credentials come from environment variables, never committed to the repository
2. **JWT Authentication**: All subscription endpoints require valid JWT tokens
3. **User Isolation**: Users can only access their own subscription data
4. **HTTP Basic Auth to Maxio**: The service uses HTTP Basic Auth to authenticate with Maxio API. Ensure HTTPS is used in production
5. **User Secrets in Production**: For production deployment, store Maxio credentials in:
   - Azure Key Vault
   - AWS Secrets Manager
   - Environment variables (via deployment platform)
   - Never in appsettings.json

## Production Deployment Checklist

- [ ] Verify Maxio production credentials are correct
- [ ] Update `Maxio:BaseUrl` if not using the standard Maxio URL
- [ ] Configure HTTPS certificates properly
- [ ] Set up error logging for Maxio API failures
- [ ] Implement retry logic for transient failures (optional, recommended)
- [ ] Set up monitoring/alerting for subscription creation failures
- [ ] Test subscription renewal and billing workflows in Maxio
- [ ] Verify email notifications are configured in Maxio
- [ ] Document the subscription management workflow for support team
- [ ] Test disaster recovery scenarios (e.g., Maxio API unavailable)

## References

- Maxio Advanced Billing API Documentation: https://developers.maxio.com/
- eShopOnWeb Repository: https://github.com/dotnet-architecture/eShopOnWeb
- Maxio Sandbox Site: `https://<subdomain>.maxio.com` (login with sandbox credentials)

---

**Integration Completed**: The Maxio subscription billing system is now integrated into eShopOnWeb. Users can browse plans, subscribe to plans, and manage their subscriptions through the PublicApi endpoints.
