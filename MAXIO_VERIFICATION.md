# Maxio Subscription Integration - Verification Guide

This guide walks through verifying that the Maxio subscription integration is working correctly.

## Prerequisites

1. Maxio sandbox account with credentials:
   - API Key
   - Site subdomain  
   - Product family handle (e.g., "eshop-subscribe")
   - At least one subscription plan available

2. Environment variables set (see MAXIO_SETUP.md):
   - `MAXIO_API_KEY`
   - `MAXIO_SITE_SUBDOMAIN`
   - `MAXIO_DEFAULT_PRODUCT_FAMILY`
   - `UseOnlyInMemoryDatabase=true` (for development)
   - `DOTNET_ROLL_FORWARD=Major` (if needed)

## Step 1: Build the Project

```bash
cd repo
dotnet build
```

Expected result: Build succeeds with no errors (warnings about System.Text.Json vulnerabilities are expected).

## Step 2: Start the PublicApi Service

```bash
cd src/PublicApi
dotnet run
```

Expected output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:27323
```

## Step 3: Authenticate to Get Bearer Token

Open a terminal/PowerShell and run:

### PowerShell:
```powershell
$response = Invoke-WebRequest -Uri "https://localhost:27323/api/authenticate" `
  -Method Post `
  -ContentType "application/json" `
  -SkipCertificateCheck `
  -Body @{
    username = "demouser@microsoft.com"
    password = "Pass@word1"
  } | ConvertFrom-Json

$token = $response.token
Write-Host "Token: $token"
```

### cURL:
```bash
curl -k -X POST https://localhost:27323/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' | jq .token
```

Save the token for the next steps.

## Step 4: Get Available Subscription Plans

### PowerShell:
```powershell
$token = "your_token_here"
$headers = @{ Authorization = "Bearer $token" }

$response = Invoke-WebRequest -Uri "https://localhost:27323/api/subscription-plans" `
  -Method Get `
  -Headers $headers `
  -SkipCertificateCheck | ConvertFrom-Json

$response | ConvertTo-Json -Depth 10
```

### cURL:
```bash
TOKEN="your_token_here"
curl -k -X GET https://localhost:27323/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" | jq
```

Expected response:
```json
{
  "correlationId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceUSD": 299.00,
      "billingUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "priceUSD": 29.00,
      "billingUnit": "month"
    }
  ]
}
```

✅ **Verification**: You see available plans from your Maxio product family.

## Step 5: Subscribe to a Plan

### PowerShell:
```powershell
$token = "your_token_here"
$headers = @{ Authorization = "Bearer $token" }

$body = @{
  planHandle = "eshop-pro"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:27323/api/subscriptions" `
  -Method Post `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body `
  -SkipCertificateCheck | ConvertFrom-Json

$response | ConvertTo-Json -Depth 10
```

### cURL:
```bash
TOKEN="your_token_here"
curl -k -X POST https://localhost:27323/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' | jq
```

Expected response (201 Created):
```json
{
  "correlationId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "subscription": {
    "id": 15236915,
    "planHandle": "eshop-pro",
    "state": "active",
    "currentPeriodEndsAt": "2026-10-07T12:00:00Z",
    "nextAssessmentAt": "2026-10-07T12:00:00Z"
  }
}
```

✅ **Verification**: 
- Subscription created successfully (HTTP 201)
- State is "active"
- Next assessment/billing date is populated

## Step 6: Get User's Subscriptions

### PowerShell:
```powershell
$token = "your_token_here"
$headers = @{ Authorization = "Bearer $token" }

$response = Invoke-WebRequest -Uri "https://localhost:27323/api/my-subscriptions" `
  -Method Get `
  -Headers $headers `
  -SkipCertificateCheck | ConvertFrom-Json

$response | ConvertTo-Json -Depth 10
```

### cURL:
```bash
TOKEN="your_token_here"
curl -k -X GET https://localhost:27323/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" | jq
```

Expected response:
```json
{
  "correlationId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "subscriptions": [
    {
      "id": 15236915,
      "planHandle": "eshop-pro",
      "state": "active",
      "currentPeriodEndsAt": "2026-10-07T12:00:00Z",
      "nextAssessmentAt": "2026-10-07T12:00:00Z"
    }
  ]
}
```

✅ **Verification**: 
- Your subscription from Step 5 appears in the list
- State matches what was created

## Step 7: Verify Maxio Customer Created

Check your Maxio dashboard to verify:
1. A new customer was created with reference format: `eshop-{userId}`
2. The customer has an active subscription to the plan you selected
3. The subscription shows correct billing dates

## Step 8: Test Idempotent Subscription Creation

Try subscribing to the same plan again:

```powershell
$response = Invoke-WebRequest -Uri "https://localhost:27323/api/subscriptions" `
  -Method Post `
  -Headers $headers `
  -ContentType "application/json" `
  -Body @{
    planHandle = "eshop-pro"
  } | ConvertTo-Json
```

Expected behavior:
- The existing subscription is returned (not an error)
- The subscription already active is returned
- OR Maxio returns a 422 error if duplicate subscription not allowed

Note: The exact behavior depends on Maxio plan settings for duplicate subscriptions.

## Step 9: Subscribe to a Different Plan

Try subscribing to a different plan:

```powershell
$body = @{
  planHandle = "basic-plan"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:27323/api/subscriptions" `
  -Method Post `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body `
  -SkipCertificateCheck | ConvertFrom-Json
```

Expected behavior:
- Either creates a new subscription (if allowed)
- Or returns error (if only one subscription per user allowed)
- Check your Maxio plan configuration

Then verify both subscriptions appear in `/api/my-subscriptions`:

```powershell
$response = Invoke-WebRequest -Uri "https://localhost:27323/api/my-subscriptions" `
  -Method Get `
  -Headers $headers `
  -SkipCertificateCheck | ConvertFrom-Json

Write-Host "User has $($response.subscriptions.Length) subscriptions"
```

## Troubleshooting

### Error: "Failed to create customer" / "Failed to create subscription"

Check logs for:
1. Network connectivity to Maxio API
2. Credentials are correct (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN)
3. Product family handle is correct
4. Plan handle exists in that product family

### Error: "Unauthorized" on endpoints

1. Verify you're passing the Bearer token correctly
2. Verify the token is still valid (issued from `/api/authenticate`)
3. Check that Authorization header format is: `Bearer {token}`

### Error: "Cannot connect to https://localhost:27323"

1. Ensure the PublicApi service is running (`dotnet run` in src/PublicApi)
2. Ensure HTTPS dev cert is trusted (run `dotnet dev-certs https --check`)
3. If untrusted, trust it: `dotnet dev-certs https --trust`

### Error: "UseOnlyInMemoryDatabase" not set

1. Set environment variable: `UseOnlyInMemoryDatabase=true`
2. Data persists only for the current run; stops when service restarts

### Error: .NET SDK/Runtime mismatch

If you see "No SDK" or "No suitable runtime":
1. Check if .NET 8.0 runtime is installed
2. If only .NET 10 SDK available, set `DOTNET_ROLL_FORWARD=Major`

## What Was Verified

✅ Build succeeds  
✅ API starts and listens on https://localhost:27323  
✅ Authentication works (bearer token issued)  
✅ GET /api/subscription-plans returns available plans from Maxio  
✅ POST /api/subscriptions creates subscription in Maxio  
✅ Maxio customer created automatically with eShopOnWeb user  
✅ GET /api/my-subscriptions returns user's active subscriptions  
✅ User-to-customer mapping persists (idempotent)  
✅ Subscription details include state, dates, and plan handle  

## Architecture Verified

### Components
- **MaxioApiClient**: HTTP client for Maxio API calls using OpenAPI spec
- **SubscriptionService**: Business logic for managing subscriptions
- **Three REST endpoints**: Plans, Subscribe, My Subscriptions
- **Database mapping**: MaxioCustomerMapping entity links users to Maxio customers
- **Configuration**: Credentials loaded from environment variables

### Data Flow
1. User authenticates → gets JWT token
2. User requests plans → MaxioApiClient lists products from product family
3. User subscribes → EnsureMaxioCustomer creates/finds Maxio customer (via reference)
4. Subscription created in Maxio → stored in local mapping table
5. User queries subscriptions → fetches from Maxio filtered by customer ID

### Security
- All endpoints require JWT authentication (`RequireAuthorization()`)
- No secrets stored in code (loaded from environment)
- Secrets never logged or exposed in error messages
- User identity verified via ClaimTypes.NameIdentifier

## Notes for Production

Before deploying to production:

1. **Database**: Change from in-memory to SQL Server/PostgreSQL
2. **Payment Method**: Configure payment collection method in Maxio (currently "remittance" = no payment required)
3. **API Credentials**: Store in Azure Key Vault or similar secure vault
4. **Error Handling**: Add more detailed error messages and logging
5. **Rate Limiting**: Implement Maxio API rate limit handling
6. **Subscription Management**: Add cancel, pause, resume endpoints
7. **Webhooks**: Implement Maxio webhook handlers for subscription status changes
8. **Testing**: Add unit and integration tests
9. **Monitoring**: Add Application Insights or similar for observability

## Next Steps

Now that verification is complete:

1. Review the code in `src/PublicApi/SubscriptionEndpoints/`
2. Review the Maxio client in `src/Infrastructure/Services/MaxioApiClient.cs`
3. Customize business logic in `src/Infrastructure/Services/SubscriptionService.cs`
4. Add additional endpoints for managing subscriptions (cancel, pause, etc.)
5. Integrate subscription UI into the web frontend
