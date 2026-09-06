# Quick Start: Verify Maxio Integration Works

## Prerequisites
- .NET 8+ SDK installed (the app will use .NET 10 via rollForward if needed)
- curl or Postman for testing API
- Maxio sandbox credentials available

## 5-Minute Setup

### 1. Configure Credentials (One Time)
Set these environment variables:
```bash
export MAXIO_API_KEY="your_sandbox_api_key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase=true
export DOTNET_ROLL_FORWARD=Major
```

### 2. Start the Application
```bash
cd src/PublicApi
dotnet run --urls="https://localhost:24923"
```

Wait for message: `LAUNCHING PublicApi` - the app is ready when you see this.

### 3. Test in Another Terminal (copy/paste these commands)

#### Step A: Get Authentication Token
```bash
TOKEN=$(curl -s -X POST https://localhost:24923/api/authenticate \
  -H "Content-Type: application/json" \
  -k \
  -d '{"username":"demouser@microsoft.com","password":"Pass@123"}' \
  | grep -o '"token":"[^"]*"' | cut -d'"' -f4)

echo "Token: $TOKEN"
```

#### Step B: List Available Plans
```bash
curl -s https://localhost:24923/api/subscription-plans -k | jq .plans[]
```

**Expected Output**: Shows Pro Plan ($299/mo) and Basic Plan ($29/mo)

#### Step C: Subscribe to a Plan
```bash
curl -s -X POST https://localhost:24923/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -k \
  -d '{"productHandle":"eshop-pro"}' | jq .
```

**Expected Output**: 
```json
{
  "subscriptionId": <number>,
  "customerId": <number>,
  "state": "active",
  "nextBillingAt": "2026-10-06T00:00:00Z",
  "message": "Subscription created successfully..."
}
```

#### Step D: View Your Subscriptions
```bash
curl -s https://localhost:24923/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -k | jq .subscriptions[]
```

**Expected Output**: Shows your active subscription

#### Step E: Try Subscribing Again (Test Idempotency)
```bash
curl -s -X POST https://localhost:24923/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -k \
  -d '{"productHandle":"eshop-basic"}' | jq .customerId
```

**Expected**: Same customerId as Step C, different subscriptionId

## What Just Happened

✅ Listed plans from Maxio
✅ Created/found Maxio customer (linked to your user)
✅ Created subscription with automatic next billing date
✅ Confirmed idempotent customer creation
✅ Retrieved your subscriptions

## If Something Fails

### 401 Unauthorized
Missing or invalid token. Re-run Step A and verify $TOKEN is set:
```bash
echo $TOKEN
```

### 400 Bad Request - "Failed to create subscription"
- Invalid product handle (use "eshop-pro" or "eshop-basic" from Step B)
- Maxio API key not set or invalid
- Network connection to Maxio

### Cannot start application
- Check that UseOnlyInMemoryDatabase=true is set
- Verify .NET SDK: `dotnet --version` should be 8.0.x or higher
- Run `dotnet build src/PublicApi/PublicApi.csproj` to check for build errors

## Architecture Overview

```
You (curl/browser)
    ↓
PublicApi (localhost:24923)
    ├── /api/subscription-plans ←→ Maxio
    ├── /api/subscriptions      ←→ Maxio
    └── /api/my-subscriptions   ←→ Maxio
```

User JWT Token → Identifies you in eShopOnWeb → Linked to Maxio customer via your user ID

## Next Steps

1. **Explore the code**: Check out the three endpoints in `src/PublicApi/SubscriptionEndpoints/`
2. **Try different products**: Use `"eshop-basic"` or other handles from Step B
3. **Test with Postman**: Better for complex requests with headers
4. **Add UI**: Create Blazor components to make this user-friendly
5. **Set up production**: Point to real Maxio account with SQL Server database

## Files You Need to Know

- `src/PublicApi/SubscriptionEndpoints/` - API endpoint implementations
- `src/Infrastructure/Services/MaxioService.cs` - Maxio API client
- `src/PublicApi/appsettings.Development.json` - Configuration
- `MAXIO_SETUP.md` - Detailed setup guide
- `IMPLEMENTATION_SUMMARY.md` - Architecture & design

---

**That's it!** The integration is working when you see subscriptions in Step D.
