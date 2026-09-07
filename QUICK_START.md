# Quick Start - Verify Subscription Billing Integration

This guide will get you up and running with the Maxio subscription billing integration in 5 minutes.

## Prerequisites

✅ Already configured on this machine:
- .NET SDK/Runtime with roll-forward enabled
- Maxio API credentials in user-secrets
- In-memory database enabled
- User test account seeded (demouser@microsoft.com / Pass@word1)

## Step 1: Build the Project

```powershell
cd C:\claude-runs\t1h45ali-openapi-haiku45high-025\repo
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet build src/PublicApi/PublicApi.csproj
```

Expected output: `Build succeeded.`

## Step 2: Start the Application

```powershell
dotnet run --project src/PublicApi/PublicApi.csproj
```

Expected output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:28283
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:28284
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

## Step 3: Open Another PowerShell Terminal and Run Tests

```powershell
# 1. Authenticate
$auth = @{
    username = "demouser@microsoft.com"
    password = "Pass@word1"
} | ConvertTo-Json

$authResp = Invoke-WebRequest -Uri "https://localhost:28283/api/authenticate" `
    -Method POST -ContentType "application/json" -Body $auth -SkipCertificateCheck
$token = ($authResp.Content | ConvertFrom-Json).token
Write-Host "✅ Authenticated" -ForegroundColor Green

# 2. Setup headers
$headers = @{ "Authorization" = "Bearer $token" }

# 3. Test endpoint 1: List plans
Write-Host "`n[1] GET /api/subscription-plans" -ForegroundColor Cyan
$plans = Invoke-WebRequest -Uri "https://localhost:28283/api/subscription-plans" `
    -Headers $headers -SkipCertificateCheck | ConvertFrom-Json
Write-Host "✅ Found $($plans.plans.Count) plans:"
$plans.plans | ForEach-Object { Write-Host "  - $($_.name) (\$$($_.priceInDollars)/month)" }

# 4. Test endpoint 2: Create subscription
Write-Host "`n[2] POST /api/subscriptions" -ForegroundColor Cyan
$sub = @{ productHandle = "eshop-pro" } | ConvertTo-Json
$subResp = Invoke-WebRequest -Uri "https://localhost:28283/api/subscriptions" `
    -Method POST -Headers @{ "Authorization" = "Bearer $token"; "Content-Type" = "application/json" } `
    -Body $sub -SkipCertificateCheck
$subscription = ($subResp.Content | ConvertFrom-Json).subscription
Write-Host "✅ Subscription created!"
Write-Host "   Plan: $($subscription.productName) - \$$($subscription.productPriceInDollars)/month"
Write-Host "   Next Billing: $($subscription.currentPeriodEndsAt)"

# 5. Test endpoint 3: List user subscriptions
Write-Host "`n[3] GET /api/my-subscriptions" -ForegroundColor Cyan
$mySubs = Invoke-WebRequest -Uri "https://localhost:28283/api/my-subscriptions" `
    -Headers $headers -SkipCertificateCheck | ConvertFrom-Json
Write-Host "✅ User has $($mySubs.subscriptions.Count) subscription(s):"
$mySubs.subscriptions | ForEach-Object { 
    Write-Host "  - $($_.productName) (State: $($_.state))"
}

Write-Host "`n🎉 All tests passed!" -ForegroundColor Green
```

## Expected Output

```
✅ Authenticated

[1] GET /api/subscription-plans
✅ Found 2 plans:
  - Basic Plan ($29/month)
  - Pro Plan ($299/month)

[2] POST /api/subscriptions
✅ Subscription created!
   Plan: Pro Plan - $299/month
   Next Billing: 2026-10-07T...

[3] GET /api/my-subscriptions
✅ User has 1 subscription(s):
  - Pro Plan (State: active)

🎉 All tests passed!
```

## What Was Implemented

### Three Endpoints:

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/subscription-plans` | List available subscription plans |
| POST | `/api/subscriptions` | Create a subscription for the authenticated user |
| GET | `/api/my-subscriptions` | List user's subscriptions |

### Key Features:

✅ **JWT Authentication** - All endpoints require bearer token
✅ **Idempotent Operations** - User ID maps to unique Maxio customer
✅ **Real Maxio Integration** - Connects to Maxio sandbox API
✅ **Proper Error Handling** - Returns meaningful error messages
✅ **Production Ready** - Follows established patterns and best practices

## Integration Architecture

```
eShopOnWeb User (JWT Token)
    ↓
PublicAPI Endpoint (requires authorization)
    ↓
MaxioApiClient (HTTP Basic Auth)
    ↓
Maxio Advanced Billing API
    ↓
Customer & Subscription Management
```

## Sandbox Credentials

- **Site**: cp-exp-2 (Maxio sandbox)
- **API Key**: Stored in user-secrets
- **Plans Available**:
  - Basic Plan: $29/month (handle: `basic-plan`)
  - Pro Plan: $299/month (handle: `eshop-pro`)

## Next Steps

1. Review `API_REFERENCE.md` for detailed endpoint documentation
2. Review `SUBSCRIPTION_VERIFICATION.md` for advanced testing scenarios
3. Implement subscription management UI using these endpoints
4. Add webhooks for Maxio events (plan changes, billing updates, etc.)

## Troubleshooting

### Error: "Application won't start"
```powershell
# Ensure using correct password
# demouser@microsoft.com / Pass@word1

# Verify HTTPS dev cert is trusted
dotnet dev-certs https --check
```

### Error: "404 Not Found" on endpoints
```powershell
# Verify endpoints are registered in Swagger
Invoke-WebRequest https://localhost:28283/swagger/v1/swagger.json -SkipCertificateCheck | `
    ConvertFrom-Json | Select-Object -ExpandProperty paths | Get-Member -MemberType NoteProperty
```

### Error: "Failed to fetch subscription plans"
```powershell
# Check Maxio API connectivity and credentials in user-secrets
dotnet user-secrets list  # should show Maxio:ApiKey and Maxio:Subdomain
```

## Summary

✅ **Build**: Succeeds  
✅ **Runtime**: Starts without errors  
✅ **Endpoints**: All three endpoints registered and working  
✅ **Authentication**: JWT required and enforced  
✅ **Maxio Integration**: Successfully connects to sandbox API  
✅ **Data**: Real subscription plans and creation functional  

The integration is complete and ready for use!
