# Quick Verification: Maxio Subscription Billing Integration

## Step 1: Verify Build (Already Done ✅)
```bash
cd C:\path\to\repo
dotnet build eShopOnWeb.sln -c Debug
# Result: Build succeeded. 0 Error(s) ✅
```

## Step 2: Environment Setup (Required First)

**Issue:** Current machine has .NET 10 SDK only; .NET 8 runtime is missing.

**Fix:** Choose ONE:
- **Option A (Recommended):** Install ASP.NET Core 8.0 runtime (x64)
- **Option B:** Run on machine with .NET 8.0 SDK or later
- **Option C:** If using .NET 10 SDK only, set environment variable before running:
  ```powershell
  $env:DOTNET_ROLL_FORWARD = "latestMajor"
  ```

## Step 3: Start PublicApi
```bash
cd src/PublicApi
dotnet run
# Expected output: "listening on https://localhost:5100" (or similar port)
```

## Step 4: Get Authentication Token
```bash
$token = curl -s -X POST https://localhost:5100/api/authenticate `
  -H "Content-Type: application/json" `
  -d '{"username":"demouser@microsoft.com","password":"Pass@123"}' `
  --insecure | jq -r '.token'

echo $token  # Should output long JWT string
```

## Step 5: Test Three Endpoints

### 5a. List Subscription Plans
```bash
curl -X GET https://localhost:5100/api/subscription-plans `
  -H "Authorization: Bearer $token" --insecure | jq .
```
**Expect:** 200 OK with Pro Plan ($299/mo) and Basic Plan ($29/mo)

### 5b. Create Subscription
```bash
curl -X POST https://localhost:5100/api/subscriptions `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{"productHandle":"eshop-pro"}' `
  --insecure | jq .
```
**Expect:** 201 Created with subscription state "active" and next billing date ~1 month out

### 5c. Get User's Subscriptions
```bash
curl -X GET https://localhost:5100/api/my-subscriptions `
  -H "Authorization: Bearer $token" --insecure | jq .
```
**Expect:** 200 OK with array containing the subscription created in 5b

## Step 6: Test Idempotency

Run Step 5b again with same token/productHandle.
**Expect:** Second subscription succeeds, but Step 5c shows only ONE Pro subscription (idempotent).

Run Step 5b with `productHandle: "basic-plan"`.
**Expect:** New subscription created, Step 5c now shows TWO subscriptions (Pro + Basic).

## What Was Built

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/subscription-plans` | GET | List available plans (Pro $299, Basic $29/month) |
| `/api/subscriptions` | POST | Subscribe to plan (idempotent customer creation) |
| `/api/my-subscriptions` | GET | User's active subscriptions |

All endpoints:
- Require JWT bearer token authentication
- Interact with Maxio sandbox (`cp-exp-3`)
- Handle errors with proper exception boundaries

## Expected Outcomes

✅ **Compilation:** 0 errors, all endpoints registered  
✅ **Endpoints:** All three routes respond correctly to authenticated requests  
✅ **Maxio Integration:** Customer lookup/creation + subscription enrollment works  
✅ **Idempotency:** Same user can subscribe multiple times without duplicating customers  
✅ **State:** Subscription state reflects Maxio's "active" status with next billing date

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| Port 5100 already in use | Another instance running | Check `netstat -ano \| find "5100"` or change launchSettings.json |
| 401 Unauthorized | No/bad JWT token | Get new token via Step 4 |
| 400 Bad Request on POST | Invalid productHandle | Use "eshop-pro" or "basic-plan" from Step 5a |
| Connection refused | PublicApi not running | Verify Step 3 output shows "listening" |
| Runtime assembly errors | .NET version mismatch | Follow Step 2 fix options |

## Files Involved

**Core Implementation:**
- `src/PublicApi/Services/MaxioSubscriptionService.cs` (245 lines)
- `src/PublicApi/SubscriptionEndpoints/*Endpoint.cs` (3 files)
- `src/PublicApi/Program.cs` (Maxio client DI + routing)

**Configuration:**
- User secrets: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment`, `Maxio:ProductFamilyHandle`
- Global settings: `global.json` (rollForward: latestMajor)

**Reference:**
- Full testing guide: `SUBSCRIPTION_INTEGRATION_VERIFICATION.md`
- Build status: `MAXIO_INTEGRATION_STATUS.md`
