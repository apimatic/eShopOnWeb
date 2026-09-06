# Maxio Subscription Integration - Verification Guide

## Overview

The eShopOnWeb application now includes recurring subscription billing powered by Maxio Advanced Billing. This guide walks through verifying the complete integration works end-to-end.

## Prerequisites

1. **Environment Variables Set** (from provided credentials):
   - `MAXIO_API_KEY`: API key for Maxio sandbox
   - `MAXIO_SITE_SUBDOMAIN`: Maxio sandbox subdomain (e.g., `cp-exp-4`)
   - `MAXIO_ENVIRONMENT`: Set to `US`
   - `MAXIO_DEFAULT_PRODUCT_FAMILY`: Product family handle (e.g., `eshop-subscribe`)

2. **User Secrets Configured** (already done during setup):
   ```bash
   cd src/PublicApi
   dotnet user-secrets set "Maxio:ApiKey" "<MAXIO_API_KEY>"
   dotnet user-secrets set "Maxio:Subdomain" "<MAXIO_SITE_SUBDOMAIN>"
   dotnet user-secrets set "Maxio:ProductFamilyHandle" "<MAXIO_DEFAULT_PRODUCT_FAMILY>"
   ```

3. **Environment Variables for Runtime**:
   ```bash
   # Set for in-memory database
   $env:UseOnlyInMemoryDatabase = "true"
   
   # Allow .NET 10 runtime
   $env:DOTNET_ROLL_FORWARD = "Major"
   ```

## Step 1: Build the Application

```bash
cd <repo-root>
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet build src/PublicApi/PublicApi.csproj
```

Expected output: "Build succeeded" with no errors.

## Step 2: Start the PublicApi Application

```bash
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi/PublicApi.csproj
```

Expected output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:27343
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

## Step 3: Authenticate (Get JWT Token)

In a new terminal, authenticate as a demo user:

```bash
$authUrl = "https://localhost:27343/api/authenticate"
$authBody = @{
    username = "demouser@microsoft.com"
    password = "Pass@word1"
} | ConvertTo-Json

$authResponse = Invoke-WebRequest -Uri $authUrl `
  -Method Post `
  -Body $authBody `
  -ContentType "application/json" `
  -SkipCertificateCheck

$token = ($authResponse.Content | ConvertFrom-Json).token
Write-Host "JWT Token: $token"
```

Save the token for the following requests.

## Step 4: List Available Subscription Plans

Test the GET endpoint:

```bash
$token = "<your-token-from-step-3>"
$plansUrl = "https://localhost:27343/api/subscription-plans"

$plansResponse = Invoke-WebRequest -Uri $plansUrl `
  -Headers @{ "Authorization" = "Bearer $token" } `
  -SkipCertificateCheck

$plans = $plansResponse.Content | ConvertFrom-Json
Write-Host "Available Plans:"
$plans.plans | ForEach-Object { 
    Write-Host "  - $($_.name): `$$($_.price)/mo (handle: $($_.handle))"
}
```

**Expected response:**
- At least 2 plans returned from Maxio (e.g., "Pro Plan" and "Basic Plan")
- Plans include: id, handle, name, price, description, intervalValue, intervalUnit
- Example: Pro Plan ($299.00/month, handle: `eshop-pro`)

## Step 5: Create a Subscription

Test the POST endpoint:

```bash
$token = "<your-token-from-step-3>"
$subscriptionUrl = "https://localhost:27343/api/subscriptions"

$subscriptionBody = @{
    planHandle = "eshop-pro"  # or "basic-plan" for the Basic Plan
} | ConvertTo-Json

$subscriptionResponse = Invoke-WebRequest -Uri $subscriptionUrl `
  -Method Post `
  -Headers @{ "Authorization" = "Bearer $token" } `
  -Body $subscriptionBody `
  -ContentType "application/json" `
  -SkipCertificateCheck

$subscription = $subscriptionResponse.Content | ConvertFrom-Json
Write-Host "Subscription Created:"
Write-Host "  ID: $($subscription.subscriptionId)"
Write-Host "  Customer ID: $($subscription.customerId)"
Write-Host "  State: $($subscription.state)"
Write-Host "  Price: `$$($subscription.price)/mo"
Write-Host "  Next Billing: $($subscription.currentPeriodEndsAt)"
```

**Expected response:**
- HTTP 201 Created
- Subscription object with:
  - `subscriptionId`: Non-zero ID
  - `customerId`: Non-zero ID  
  - `state`: "active" or "awaiting_signup"
  - `price`: Matches plan price (e.g., 299.00 for Pro Plan)
  - `planHandle`: Matches request (e.g., "eshop-pro")
  - `activatedAt`: Current timestamp
  - `currentPeriodEndsAt`: Future date (one month from now)

## Step 6: Retrieve User's Subscriptions

Test the GET endpoint for user subscriptions:

```bash
$token = "<your-token-from-step-3>"
$mySubsUrl = "https://localhost:27343/api/my-subscriptions"

$mySubsResponse = Invoke-WebRequest -Uri $mySubsUrl `
  -Headers @{ "Authorization" = "Bearer $token" } `
  -SkipCertificateCheck

$subscriptions = $mySubsResponse.Content | ConvertFrom-Json
Write-Host "Your Subscriptions:"
$subscriptions.subscriptions | ForEach-Object {
    Write-Host "  - Subscription ID: $($_.id), Plan: $($_.planHandle), State: $($_.state)"
}
```

**Expected response:**
- HTTP 200 OK
- Array of subscriptions for the authenticated user
- Should include the subscription created in Step 5
- Each subscription includes: id, customerId, state, activatedAt, currentPeriodEndsAt, price, planHandle

## Step 7: Verify Idempotency (Double-Subscribe Same Plan)

Repeat Step 5 with the same plan:

```bash
# Run the same subscription creation request again
# (same token, same planHandle)
```

**Expected behavior:**
- The same customer should be reused (no duplicate customer created)
- A new subscription is created for the same customer
- No errors about duplicate references

## Step 8: Verify Cross-Plan Subscription

Create a subscription to a different plan:

```bash
$token = "<your-token-from-step-3>"
$subscriptionUrl = "https://localhost:27343/api/subscriptions"

$subscriptionBody = @{
    planHandle = "basic-plan"  # Different plan
} | ConvertTo-Json

$subscriptionResponse = Invoke-WebRequest -Uri $subscriptionUrl `
  -Method Post `
  -Headers @{ "Authorization" = "Bearer $token" } `
  -Body $subscriptionBody `
  -ContentType "application/json" `
  -SkipCertificateCheck

$subscription = $subscriptionResponse.Content | ConvertFrom-Json
Write-Host "New subscription to basic-plan created: $($subscription.subscriptionId)"
```

Then verify both subscriptions are listed:

```bash
$token = "<your-token-from-step-3>"
$mySubsUrl = "https://localhost:27343/api/my-subscriptions"

$mySubsResponse = Invoke-WebRequest -Uri $mySubsUrl `
  -Headers @{ "Authorization" = "Bearer $token" } `
  -SkipCertificateCheck

$subscriptions = $mySubsResponse.Content | ConvertFrom-Json
Write-Host "Total subscriptions for user: $($subscriptions.subscriptions.Count)"
```

**Expected:**
- Should have 2+ subscriptions total (from Steps 5-8)

## Step 9: Verify Authentication Required

Test that protected endpoints require authorization:

```bash
# Try to access /api/my-subscriptions without token
$mySubsUrl = "https://localhost:27343/api/my-subscriptions"

try {
    Invoke-WebRequest -Uri $mySubsUrl `
      -SkipCertificateCheck `
      -ErrorAction Stop
} catch {
    Write-Host "Expected error: $($_.Exception.Response.StatusCode)"
}
```

**Expected:**
- HTTP 401 Unauthorized (request rejected without Bearer token)

## Step 10: Verify Swagger Documentation

Open Swagger UI to view endpoint documentation:

```
https://localhost:27343/swagger
```

**Expected:**
- Three new endpoints visible under "SubscriptionEndpoints" tag:
  1. GET /api/subscription-plans
  2. POST /api/subscriptions
  3. GET /api/my-subscriptions
- All endpoints show correct parameters and response schemas
- POST endpoint shows "Authorize" button (OAuth2/JWT)
- Other two endpoints work unauthenticated

## Troubleshooting

### "Cannot convert JSON" Errors
- Verify Maxio credentials are set correctly in user-secrets
- Check that the Maxio sandbox is accessible (network/firewall)
- Review application logs for the actual Maxio API response

### "Customer reference already exists" (422 errors)
- This is expected after the first subscription creation
- Indicates idempotent customer creation is working correctly
- On retry, the same customer should be reused

### Missing Plans
- Verify `Maxio:ProductFamilyHandle` matches the actual family in Maxio sandbox
- Check that plans exist in Maxio under that family
- Default handle is `eshop-subscribe`

### Subscription State is "awaiting_signup" Instead of "active"
- This is normal for sandbox environments
- Plans are configured with "payment method not required"
- State transitions are handled by Maxio based on payment collection method

## Architecture Notes

- **Service Layer**: `src/PublicApi/Services/MaxioService.cs` handles all Maxio API calls
- **Endpoints**: Three endpoints in `src/PublicApi/SubscriptionEndpoints/`
  - `GetSubscriptionPlansEndpoint.cs` - Lists plans from product family
  - `CreateSubscriptionEndpoint.cs` - Creates subscription (idempotent customer)
  - `GetUserSubscriptionsEndpoint.cs` - Lists user's subscriptions
- **Configuration**: Maxio settings loaded from appsettings.json, credentials from user-secrets
- **Authentication**: JWT Bearer token required for write and read-user operations
- **Database**: Subscription data persisted in Maxio, user↔subscription mapping is stateless (no local persistence)

## Cleanup

To stop the PublicApi application:
```bash
# In the terminal running "dotnet run", press Ctrl+C
```

To clear user secrets (if needed):
```bash
cd src/PublicApi
dotnet user-secrets clear
```
