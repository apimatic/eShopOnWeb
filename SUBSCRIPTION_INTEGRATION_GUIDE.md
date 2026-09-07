# Maxio Subscription Billing Integration - Verification Guide

## Overview
This guide walks you through verifying the Maxio subscription billing integration added to eShopOnWeb. The integration adds three new endpoints to the PublicApi project for managing recurring subscriptions.

## Endpoints

### 1. GET /api/subscription-plans
Lists available subscription plans from Maxio.

**Authentication**: JWT Bearer token required  
**Response**: List of subscription plans with pricing and interval details

### 2. POST /api/subscriptions
Creates a new subscription for the authenticated user.

**Authentication**: JWT Bearer token required  
**Request Body**:
```json
{
  "planHandle": "eshop-pro"
}
```

**Response**: Subscription details including subscription ID, state, price, and next billing date

### 3. GET /api/my-subscriptions
Lists all active subscriptions for the authenticated user.

**Authentication**: JWT Bearer token required  
**Response**: Array of user's subscriptions with details

## Prerequisites for Testing

### Environment Setup
1. Ensure Maxio credentials are stored in user-secrets:
   - `Maxio:ApiKey` - API key for Maxio sandbox
   - `Maxio:Subdomain` - Maxio site subdomain (e.g., "cp-exp-4")
   - `Maxio:ProductFamilyHandle` - Product family handle (e.g., "eshop-subscribe")

2. Verify credentials are set (they should already be configured from environment variables):
   ```powershell
   cd src/PublicApi
   dotnet user-secrets list
   ```

### Database Configuration
- Set `UseOnlyInMemoryDatabase=true` in appsettings (in-memory DB for testing)
- Database is auto-seeded on startup

### Run the PublicApi
```powershell
cd src/PublicApi
dotnet run
```

The API will start on: `https://localhost:5001`  
Swagger UI available at: `https://localhost:5001/swagger`

## Step-by-Step Verification

### 1. Generate a Test JWT Token

First, authenticate to get a JWT token:

```powershell
# Authenticate
$response = Invoke-WebRequest -Uri "https://localhost:5001/api/authenticate" `
  -Method POST `
  -ContentType "application/json" `
  -Body '{"username":"test@example.com","password":"Pass@word123"}' `
  -SkipCertificateCheck

$token = ($response.Content | ConvertFrom-Json).token
Write-Host "Token: $token"
```

Or use the Swagger UI to authenticate and test directly.

### 2. Test Get Subscription Plans

```powershell
$headers = @{
    Authorization = "Bearer $token"
    "Content-Type" = "application/json"
}

$response = Invoke-WebRequest -Uri "https://localhost:5001/api/subscription-plans" `
  -Method GET `
  -Headers $headers `
  -SkipCertificateCheck

Write-Host "Plans Response:`n"
$response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

**Expected Response**:
- List of 2 plans: `eshop-pro` ($299.00/mo) and `basic-plan` ($29.00/mo)
- Each plan includes name, handle, description, price, interval info

### 3. Test Create Subscription

```powershell
$subscriptionBody = @{
    planHandle = "eshop-pro"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:5001/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -Body $subscriptionBody `
  -SkipCertificateCheck

Write-Host "Subscription Created:`n"
$response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

**Expected Response**:
- Subscription ID (from Maxio)
- Customer ID (from Maxio)
- Plan handle: "eshop-pro"
- State: "active"
- Price per month: 299.00
- Next billing date: ~30 days from now
- Message: "Successfully subscribed to eshop-pro"

**Important**: The first subscription creation for a user will:
1. Create a customer in Maxio using the user ID as reference
2. Subscribe that customer to the selected plan
3. Store the subscription locally in the database

### 4. Test List User Subscriptions

```powershell
$response = Invoke-WebRequest -Uri "https://localhost:5001/api/my-subscriptions" `
  -Method GET `
  -Headers $headers `
  -SkipCertificateCheck

Write-Host "User Subscriptions:`n"
$response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

**Expected Response**:
- Array with 1 subscription (the one just created)
- Subscription details match the creation response

### 5. Test Idempotency

Try creating the same subscription again:

```powershell
$subscriptionBody = @{
    planHandle = "eshop-pro"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:5001/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -Body $subscriptionBody `
  -SkipCertificateCheck

Write-Host "Second Subscription Creation:`n"
$response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
```

**Expected Behavior**:
- Returns success with the existing subscription details
- Does not create a duplicate customer or subscription in Maxio
- Uses the same Maxio customer ID as before

### 6. Try Different Plan

```powershell
# Test with a different plan
$subscriptionBody = @{
    planHandle = "basic-plan"
} | ConvertTo-Json

# Use a different test user for this (or just note that trying to subscribe
# the same user to two different plans will succeed as the API supports
# multiple subscriptions per customer)
```

## Key Behaviors

### Idempotent Customer Creation
- Users are identified by their JWT claim `NameIdentifier` or `sub`
- Customer reference in Maxio: `eshop-{userId}`
- If a customer already exists with that reference, it's reused
- Prevents duplicate customers in Maxio

### Subscription State
- Subscriptions created with `payment_collection_method: "invoice"`
- No payment method required (matches seeded plans in Maxio)
- Subscription state is returned from Maxio (typically "active")

### Local Storage
- Each subscription is also stored in the local database
- Links eShopOnWeb user to Maxio subscription
- Preserves subscription history even if Maxio data is re-seeded

## Troubleshooting

### 401 Unauthorized
- Check that the JWT token is valid and not expired
- Ensure `Authorization: Bearer $token` header is present

### 404 Not Found
- Verify endpoints are registered (they route under `/api/`)
- Check that MinimalApi.Endpoint is scanning the assembly

### 400 Bad Request - "User email not found in token"
- The JWT token must include the `email` claim
- Verify the authentication endpoint returns this claim

### Maxio API Errors
- Check that Maxio credentials are set correctly in user-secrets
- Verify the `cp-exp-4` sandbox site is accessible
- Check the `eshop-subscribe` product family and plans exist in Maxio

### Database Errors
- Ensure `UseOnlyInMemoryDatabase=true` is set
- Note: In-memory database loses data on restart
- For production, use actual SQL Server

## Architecture

### Projects Modified
- **ApplicationCore**: Added `Subscription` entity (aggregate root)
- **Infrastructure**: 
  - `MaxioService` - Handles all Maxio API interactions
  - `Dependencies.cs` - Registered HttpClient and MaxioService
- **PublicApi**: 
  - Three new endpoints for subscription management
  - Uses JWT authentication via bearer scheme

### Key Design Decisions
1. **Idempotent Operations**: User reference-based customer creation prevents duplicates
2. **Service Layer**: MaxioService encapsulates all Maxio API logic
3. **Local Tracking**: Database stores subscription details for audit trail
4. **No Circular Dependencies**: Endpoints use injected services cleanly
5. **Production-Grade Logging**: MaxioService includes error logging

## Next Steps for Production

1. **Payment Methods**: Implement payment method capture (currently disabled per Maxio config)
2. **Webhooks**: Add Maxio webhook handlers for subscription events (created, canceled, etc.)
3. **Subscription Management**: Add endpoints for updating/canceling subscriptions
4. **Persistence**: Switch from in-memory to SQL Server for production
5. **Monitoring**: Add telemetry for subscription creation/cancellation rates
6. **UI Integration**: Build subscription selection UI on the web frontend
