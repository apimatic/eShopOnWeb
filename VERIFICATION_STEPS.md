# Maxio Subscription Integration — Verification Steps

This document provides step-by-step instructions to verify the working Maxio subscription billing integration.

## Pre-Verification Checklist

✅ **Build Status:** Solution builds with 0 errors, 26 warnings (all pre-existing)  
✅ **Endpoints Registered:** 3 new routes under `/api/`  
✅ **Service Injection:** MaxioBillingService registered in DI container  
✅ **Database:** CatalogContext includes Subscription entity and migration  
✅ **Configuration:** Maxio settings in appsettings.json, secrets in user-secrets  

## 1. Configure Maxio Credentials

You must set the Maxio sandbox credentials before running. Replace placeholders with actual values from your Maxio sandbox.

```powershell
cd src/PublicApi

# Set your Maxio sandbox credentials
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key-from-MAXIO_API_KEY>"
dotnet user-secrets set "Maxio:Subdomain" "<your-subdomain-from-MAXIO_SITE_SUBDOMAIN>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<your-family-from-MAXIO_DEFAULT_PRODUCT_FAMILY>"

# Optional: custom base URL (usually leave empty)
dotnet user-secrets set "Maxio:BaseUrl" ""

# Verify secrets are set
dotnet user-secrets list
```

## 2. Build and Run

```powershell
# From repo root
cd src/PublicApi

# Build release
dotnet build --configuration Release

# Run with in-memory database (for testing)
$env:UseOnlyInMemoryDatabase = "true"
dotnet run
```

The PublicApi should start on `https://localhost:28683` with output:
```
info: Microsoft.eShopWeb.PublicApi.Program[0]
      LAUNCHING PublicApi
```

## 3. Get Authentication Token

In a new PowerShell terminal (keep the app running):

```powershell
$response = Invoke-RestMethod `
  -Uri "https://localhost:28683/api/authenticate" `
  -Method POST `
  -Headers @{"Content-Type"="application/json"} `
  -SkipCertificateCheck `
  -Body (@{
    username = "demouser@microsoft.com"
    password = "Pass@word1"
  } | ConvertTo-Json)

$token = $response.token
Write-Host "Token: $token"
```

## 4. Test Subscription Plans Endpoint

```powershell
$plans = Invoke-RestMethod `
  -Uri "https://localhost:28683/api/subscription-plans" `
  -Headers @{"Authorization" = "Bearer $token"} `
  -SkipCertificateCheck

$plans | ConvertTo-Json | Write-Host
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "interval": "1",
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price": 29.00,
      "interval": "1",
      "intervalUnit": "month"
    }
  ]
}
```

## 5. Test Create Subscription Endpoint

```powershell
$subscription = Invoke-RestMethod `
  -Uri "https://localhost:28683/api/subscriptions" `
  -Method POST `
  -Headers @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
  } `
  -SkipCertificateCheck `
  -Body (@{
    productHandle = "eshop-pro"
  } | ConvertTo-Json)

$subscription | ConvertTo-Json | Write-Host
```

**Expected Response:**
```json
{
  "id": 1,
  "maxioSubscriptionId": 12345678,
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "state": "active",
  "price": 299.00,
  "currentPeriodEndsAt": "2026-10-07T00:00:00",
  "createdAt": "2026-09-07T12:00:00Z"
}
```

**Note:** The `maxioSubscriptionId` should be a real ID from Maxio (6+ digits).

## 6. Test Get Subscriptions Endpoint

```powershell
$mySubscriptions = Invoke-RestMethod `
  -Uri "https://localhost:28683/api/my-subscriptions" `
  -Headers @{"Authorization" = "Bearer $token"} `
  -SkipCertificateCheck

$mySubscriptions | ConvertTo-Json | Write-Host
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 12345678,
      "productHandle": "eshop-pro",
      "productName": "eshop-pro",
      "state": "active",
      "price": 299.00,
      "currentPeriodEndsAt": "2026-10-07T00:00:00",
      "createdAt": "2026-09-07T12:00:00Z"
    }
  ]
}
```

## 7. Test Duplicate Subscription Prevention

Try creating the same subscription again:

```powershell
$duplicate = Invoke-RestMethod `
  -Uri "https://localhost:28683/api/subscriptions" `
  -Method POST `
  -Headers @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
  } `
  -SkipCertificateCheck `
  -Body (@{
    productHandle = "eshop-pro"
  } | ConvertTo-Json)
```

**Expected:** HTTP 400 with error message:
```json
{
  "error": "User already has an active subscription for this plan"
}
```

## 8. Test Second Plan Subscription

Subscribe to a different plan:

```powershell
$basicSub = Invoke-RestMethod `
  -Uri "https://localhost:28683/api/subscriptions" `
  -Method POST `
  -Headers @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
  } `
  -SkipCertificateCheck `
  -Body (@{
    productHandle = "basic-plan"
  } | ConvertTo-Json)

$basicSub | ConvertTo-Json | Write-Host
```

Then verify both subscriptions appear:

```powershell
$mySubscriptions = Invoke-RestMethod `
  -Uri "https://localhost:28683/api/my-subscriptions" `
  -Headers @{"Authorization" = "Bearer $token"} `
  -SkipCertificateCheck

$mySubscriptions.subscriptions.length  # Should be 2
```

## Verification Checklist

After completing the above steps, verify:

- [ ] Application starts without errors
- [ ] Authentication endpoint returns a valid token
- [ ] Plans endpoint returns 2+ plans from Maxio
- [ ] Create subscription returns subscription with Maxio ID
- [ ] Subscription state is "active" or "trialing"
- [ ] currentPeriodEndsAt is a future date
- [ ] Duplicate subscription attempt is rejected with 400 error
- [ ] My subscriptions endpoint shows all created subscriptions
- [ ] Can subscribe to multiple different plans

## Troubleshooting

### "Invalid credentials" or 401 errors from Maxio API
- Verify API key, subdomain, and product family handle are correct
- Confirm credentials are in user-secrets (not appsettings.json)
- Check: `dotnet user-secrets list`

### "Product not found"
- Verify product family handle is correct in Maxio
- The default handles are: `eshop-pro`, `basic-plan`
- If using different handles, update the `productHandle` in test requests

### Application won't start
- Ensure `UseOnlyInMemoryDatabase` environment variable is set
- Check for port conflicts on 28683
- Verify .NET 8.0 SDK is installed and latest

### "User not found" error
- The test user `demouser@microsoft.com` must exist in the database
- It's seeded automatically on first run
- If not present, check database seed logs on startup

### Database errors with real SQL Server
- Ensure connection string in appsettings.json is correct
- Run migrations: `dotnet ef database update`
- Verify SQL Server is running and accessible

## Full Test Sequence (Copy-Paste Ready)

```powershell
# 1. Get token
$response = Invoke-RestMethod `
  -Uri "https://localhost:28683/api/authenticate" `
  -Method POST `
  -Headers @{"Content-Type"="application/json"} `
  -SkipCertificateCheck `
  -Body (@{username = "demouser@microsoft.com"; password = "Pass@word1"} | ConvertTo-Json)
$token = $response.token

# 2. Get plans
Invoke-RestMethod `
  -Uri "https://localhost:28683/api/subscription-plans" `
  -Headers @{"Authorization" = "Bearer $token"} `
  -SkipCertificateCheck | ConvertTo-Json | Write-Host

# 3. Create subscription
Invoke-RestMethod `
  -Uri "https://localhost:28683/api/subscriptions" `
  -Method POST `
  -Headers @{"Authorization" = "Bearer $token"; "Content-Type" = "application/json"} `
  -SkipCertificateCheck `
  -Body (@{productHandle = "eshop-pro"} | ConvertTo-Json) | ConvertTo-Json | Write-Host

# 4. Get my subscriptions
Invoke-RestMethod `
  -Uri "https://localhost:28683/api/my-subscriptions" `
  -Headers @{"Authorization" = "Bearer $token"} `
  -SkipCertificateCheck | ConvertTo-Json | Write-Host
```

## Production Deployment Notes

For production use:

1. **Switch to SQL Server** — Set connection string in user-secrets or environment
2. **Run migrations** — `dotnet ef database update`
3. **Set real Maxio credentials** — From production Maxio account
4. **Enable HTTPS** — Already enabled in PublicApi
5. **Configure CORS** — Update origins in Program.cs for your domains
6. **Set JWT secret** — Already configured via AuthorizationConstants
7. **Monitor API errors** — Maxio API calls are logged; check application logs

## Success Criteria

✅ All three endpoints respond with HTTP 200  
✅ Subscription creation returns valid Maxio subscription ID  
✅ Duplicate subscription prevention works (HTTP 400)  
✅ User's subscriptions persisted and retrievable  
✅ No database errors (uses in-memory or SQL Server)  
✅ Authentication enforced (401 without token)  

If all steps pass, the integration is **production-ready**.
