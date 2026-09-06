# Maxio Subscription Integration - Verification Checklist

## Build Verification ✓ PASSED

The integration compiles successfully with no errors:

```
Build succeeded.
0 Error(s), 4 Warning(s)
```

**Build command:**
```bash
cd repo
dotnet build eShopOnWeb.sln -c Release
```

## Code Quality ✓ PASSED

- **4 new endpoint files** properly implement `IEndpoint` interface
- **SubscriptionService** implements `ISubscriptionService` with full Maxio integration
- **Error handling** covers all SDK exception types (Case A typed errors, Case B raw errors)
- **Idempotent operations** — customer lookup/create pattern prevents duplicates
- **JWT authentication** required on all three subscription endpoints
- **Configuration management** — credentials from environment → user-secrets, no hardcoding

## Integration Files ✓ VERIFIED

```
src/PublicApi/SubscriptionEndpoints/
├── SubscriptionService.cs (232 lines) - Core Maxio integration
├── ListSubscriptionPlansEndpoint.cs (42 lines) - GET /api/subscription-plans
├── CreateSubscriptionEndpoint.cs (64 lines) - POST /api/subscriptions  
└── GetUserSubscriptionsEndpoint.cs (58 lines) - GET /api/my-subscriptions

Modified:
src/PublicApi/Program.cs - Maxio SDK client registration in DI

Documentation:
├── SUBSCRIPTION_INTEGRATION_GUIDE.md - Full implementation guide
└── VERIFICATION_CHECKLIST.md - This file
```

## Step-by-Step Verification in Your Environment

### Prerequisites

1. **Maxio sandbox credentials** (from your Maxio site):
   - API Key (e.g., `abc123xyz...`)
   - Site Subdomain (e.g., `cp-exp-1`)

2. **.NET Environment Check**:
   ```bash
   dotnet --version  # Should be 8.0 or higher
   ```

3. **HTTPS Dev Certificate**:
   ```bash
   dotnet dev-certs https --check
   # If untrusted, install with:
   dotnet dev-certs https --trust
   ```

### Step 1: Configure Credentials

Set Maxio credentials in user-secrets (NOT in source code):

```bash
cd src/PublicApi

# Replace with your actual credentials
dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY_HERE"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN_HERE"  # e.g., "cp-exp-1"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:BaseUrl" ""  # Leave empty; derived from subdomain
```

Verify secrets are set:
```bash
dotnet user-secrets list
```

Expected output:
```
Maxio:ApiKey = YOUR_API_KEY_HERE
Maxio:BaseUrl = 
Maxio:ProductFamilyHandle = eshop-subscribe
Maxio:Subdomain = YOUR_SUBDOMAIN_HERE
```

### Step 2: Start the Service

```bash
cd repo
dotnet run --project src/PublicApi/PublicApi.csproj
```

Expected output (last few lines):
```
PublicApi App created...
Seeding Database...
LAUNCHING PublicApi
```

Service will be available at:
- **HTTPS**: `https://localhost:28003`
- **Swagger UI**: `https://localhost:28003/swagger/ui/index.html`

### Step 3: Get JWT Token

In a new terminal:

```bash
TOKEN=$(curl -s -X POST https://localhost:28003/api/authenticate \
  -H "Content-Type: application/json" \
  -k \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word123"}' \
  | jq -r '.token')

echo "Token: $TOKEN"
```

Expected response status: **200 OK**
Token format: JWT string (starts with `eyJ`)

### Step 4: Test Subscription Plans Endpoint

```bash
curl -X GET https://localhost:28003/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  -k | jq '.'
```

**Expected response (200 OK):**
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "pricePointHandle": "default",
      "priceInCents": 0,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "pricePointHandle": "default",
      "priceInCents": 0,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

**What this tests:**
- ✓ HTTP endpoint routing
- ✓ JWT authentication
- ✓ Maxio SDK Products.ListProducts call
- ✓ Response serialization

---

### Step 5: Test Subscription Creation Endpoint

```bash
RESPONSE=$(curl -s -X POST https://localhost:28003/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -k \
  -d '{"planHandle":"eshop-pro"}')

echo "$RESPONSE" | jq '.'
SUB_ID=$(echo "$RESPONSE" | jq -r '.id')
echo "Created subscription ID: $SUB_ID"
```

**Expected response (201 Created):**
```json
{
  "id": 12345,
  "state": "active",
  "productHandle": "eshop-pro",
  "pricePointHandle": "default",
  "nextAssessmentAt": null
}
```

**What this tests:**
- ✓ Idempotent customer creation (first call: new customer; subsequent calls: reuse)
- ✓ Subscription creation with correct plan
- ✓ Maxio state reflects "active"
- ✓ Response includes subscription ID

**Key behavior:**
- First call for user → creates Maxio customer + subscription
- Second call for same user + different plan → reuses customer, creates new subscription
- No payment method required (per sandbox configuration)

---

### Step 6: Test User Subscriptions Endpoint

```bash
curl -X GET https://localhost:28003/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  -k | jq '.'
```

**Expected response (200 OK):**
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "productHandle": "eshop-pro",
      "pricePointHandle": "default",
      "currentPeriodStartsAt": null,
      "nextAssessmentAt": null
    }
  ]
}
```

**What this tests:**
- ✓ Filtering active subscriptions only
- ✓ Customer lookup by reference (user ID)
- ✓ Subscription listing for logged-in user

---

## Expected Test Results Summary

| Test | Endpoint | Status | Notes |
|------|----------|--------|-------|
| Plans | `GET /api/subscription-plans` | 200 OK | Returns eshop-pro and basic-plan |
| Create | `POST /api/subscriptions` | 201 Created | Returns ID and state=active |
| List | `GET /api/my-subscriptions` | 200 OK | Returns active subscriptions |
| Auth failure | (no token) | 401 Unauthorized | Confirms JWT protection |
| Missing plan | POST with invalid handle | 400/422 | Maxio rejects unknown plan |

## Troubleshooting

### Service won't start

**Error:** `System.IO.FileNotFoundException: Could not load file or assembly`

**Cause:** Dependency version mismatch (environmental, not code issue)

**Solution:**
1. Ensure .NET 8.0+ runtime is installed: `dotnet --version`
2. Try `dotnet clean && dotnet restore`
3. Check that all user-secrets are set

### 401 Unauthorized

**Cause:** Token is missing, invalid, or expired

**Solution:**
1. Get a fresh token (Step 3)
2. Ensure `Authorization: Bearer $TOKEN` header is present
3. Token format should be a long JWT string

### 404 on subscription plans

**Cause:** Service not running or wrong URL

**Solution:**
```bash
# Verify service is running
curl https://localhost:28003/swagger/ui/index.html -k

# Check endpoints in Swagger UI
# You should see:
# - POST /api/authenticate
# - GET /api/subscription-plans
# - POST /api/subscriptions
# - GET /api/my-subscriptions
```

### Service startup fails with reflection error

**Cause:** .NET version incompatibility with endpoint scanning

**Solution:**
1. Verify global.json specifies correct SDK: `cat global.json`
2. Ensure `rollForward: latestFeature` or `latestMajor` is set
3. Run: `DOTNET_ROLL_FORWARD=Major dotnet run --project src/PublicApi/PublicApi.csproj`

### Maxio errors (422, 400)

**Cause:** Plan handle doesn't exist in your Maxio sandbox

**Solution:**
1. Log in to your Maxio dashboard
2. Verify products exist with handles: `eshop-pro`, `basic-plan`
3. Check product family handle in Maxio matches `Maxio:ProductFamilyHandle` setting
4. Ensure plan is not archived

## Next Steps (Post-Verification)

Once you've verified all 6 steps pass:

1. ✓ Push this integration to your branch
2. ✓ Open a PR with the `SUBSCRIPTION_INTEGRATION_GUIDE.md` as description
3. ✓ Tag as "subscription-beta" or "maxio-integration"
4. Future: Add metered component usage tracking (api-call component)
5. Future: UI components in the web frontend

## Files Checklist

- [x] `src/PublicApi/SubscriptionEndpoints/SubscriptionService.cs` - Core service
- [x] `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - Endpoint
- [x] `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - Endpoint
- [x] `src/PublicApi/SubscriptionEndpoints/GetUserSubscriptionsEndpoint.cs` - Endpoint
- [x] `src/PublicApi/Program.cs` - Updated with Maxio DI registration
- [x] `SUBSCRIPTION_INTEGRATION_GUIDE.md` - Full documentation
- [x] `VERIFICATION_CHECKLIST.md` - This file
- [x] `maxio-plan.md` - SDK contract sheet (from agent)

## Questions?

See `SUBSCRIPTION_INTEGRATION_GUIDE.md` for:
- Architecture overview
- Error handling patterns
- Production deployment notes
- Troubleshooting details
