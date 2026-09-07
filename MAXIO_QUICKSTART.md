# Quick Start - Maxio Integration Testing

Get the integration working in 5 minutes.

## 1. Configure Credentials

```bash
# From repository root
cd src/PublicApi

# Set Maxio sandbox credentials
dotnet user-secrets set "MAXIO_API_KEY" "your-sandbox-api-key"
```

Replace `your-sandbox-api-key` with your actual Maxio sandbox API key.

## 2. Build & Run

```bash
# Build the solution
dotnet build Everything.sln

# Run PublicApi service
cd src/PublicApi
set DOTNET_ROLL_FORWARD=Major
set UseOnlyInMemoryDatabase=true
dotnet run
```

Service will start on `https://localhost:28763`

## 3. Test the Endpoints

### Get Auth Token (PowerShell)

```powershell
$token = (Invoke-WebRequest -Uri "https://localhost:28763/api/authenticate" `
  -Method POST `
  -Headers @{"Content-Type"="application/json"} `
  -Body '{"username":"demouser@microsoft.com","password":"Pass@word1"}' `
  -SkipCertificateCheck).Content | ConvertFrom-Json | Select-Object -ExpandProperty token

Write-Host "Token: $token"
```

### List Plans

```powershell
$headers = @{"Authorization"="Bearer $token"; "Content-Type"="application/json"}
Invoke-WebRequest -Uri "https://localhost:28763/api/subscription-plans" `
  -Method GET -Headers $headers -SkipCertificateCheck | ConvertFrom-Json
```

Expected: JSON array with Pro Plan ($299/mo) and Basic Plan ($29/mo)

### Create Subscription

```powershell
$headers = @{"Authorization"="Bearer $token"; "Content-Type"="application/json"}
$body = '{"productHandle":"eshop-pro"}'
Invoke-WebRequest -Uri "https://localhost:28763/api/subscriptions" `
  -Method POST -Headers $headers -Body $body -SkipCertificateCheck | ConvertFrom-Json
```

Expected: Subscription ID, "active" state, $299.00/month, next billing date

### List User Subscriptions

```powershell
$headers = @{"Authorization"="Bearer $token"; "Content-Type"="application/json"}
Invoke-WebRequest -Uri "https://localhost:28763/api/my-subscriptions" `
  -Method GET -Headers $headers -SkipCertificateCheck | ConvertFrom-Json
```

Expected: Array with the subscription you just created

## 4. Verify in Maxio Dashboard

1. Go to `https://cp-exp-2.chargify.com/`
2. Log in with your Maxio sandbox account
3. Navigate to Customers
4. Find customer with email matching `demouser@microsoft.com`
5. Click customer to see their subscription(s)
6. Verify:
   - Subscription exists
   - Product is "Pro Plan"
   - Status is "Active"

## Troubleshooting Quick Fixes

| Issue | Fix |
|-------|-----|
| Build fails with SDK error | `set DOTNET_ROLL_FORWARD=Major` |
| 401 Unauthorized | Token expired - get new one from authenticate endpoint |
| 400 Bad Request | Check product handle (should be "eshop-pro" or "basic-plan") |
| Connection refused | Verify service is running and using right port |
| No data in Maxio | Check API key is correct for `cp-exp-2` site |

## Next Steps

1. Read `MAXIO_INTEGRATION_SUMMARY.md` for architecture overview
2. Read `MAXIO_INTEGRATION_VERIFICATION.md` for comprehensive test scenarios
3. Review code in `src/PublicApi/Maxio/` and `src/PublicApi/SubscriptionEndpoints/`
4. Integrate UI components in the storefront if needed

---

**Status**: ✅ Ready to test
