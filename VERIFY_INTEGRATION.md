# Quick Verification Guide - Maxio Subscription Integration

## What Was Built

✅ Three API endpoints for subscription management  
✅ Complete Maxio service integration  
✅ Database tracking for user-customer mappings  
✅ JWT authentication on protected endpoints  
✅ Production-grade error handling  

---

## Verify in 5 Minutes

### 1. Build (30 seconds)
```bash
cd repo
dotnet build -c Release
```
✅ Expected: Build succeeded, 0 errors

### 2. Set Environment Variables
```bash
# PowerShell
$env:MAXIO_API_KEY = "your-actual-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-3"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

### 3. Start PublicApi (1 minute)
```bash
cd src/PublicApi
dotnet run
```
✅ Expected: Server listens on `https://localhost:28543`

### 4. Get Authentication Token (1 minute)

In new terminal:
```bash
curl -X POST https://localhost:5001/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  -k --silent | jq '.token'
```

Save the token: `TOKEN="..."`

### 5. Test Endpoints (2 minutes)

**Test 1: List Plans** (no auth)
```bash
curl https://localhost:28543/api/subscription-plans -k | jq '.plans[0]'
```
✅ Returns: Pro Plan with $299.00/month

**Test 2: Subscribe** (with auth)
```bash
curl -X POST https://localhost:28543/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k | jq '.subscription'
```
✅ Returns: Subscription with state "active"

**Test 3: List Subscriptions** (with auth)
```bash
curl https://localhost:28543/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -k | jq '.subscriptions[0]'
```
✅ Returns: Active subscription for Pro Plan

### 6. Verify in Maxio Dashboard

1. Log in: `https://cp-exp-3.chargify.com`
2. Go to Customers
3. Search for username you used
4. ✅ Should see: New customer with subscription

---

## What Gets Created

**Endpoints Registered:**
- `GET /api/subscription-plans` - Public endpoint
- `POST /api/subscriptions` - Requires JWT auth
- `GET /api/my-subscriptions` - Requires JWT auth

**Database:**
- `UserMaxioCustomers` table created via migration
- Stores mapping of user → Maxio customer ID

**Configuration:**
- Loads from environment variables
- Falls back to appsettings.json

---

## Files Modified/Created

**New (13 files):**
- 3 endpoints + DTOs
- 1 Maxio service (HttpClient integration)
- 1 entity + configuration
- 1 specification class
- 1 database migration

**Modified (6 files):**
- Dependencies.cs, Program.cs, appsettings.json, etc.

**Documentation (5 files):**
- VERIFICATION_COMPLETE.md (full details)
- QUICK_START.md (setup guide)
- MAXIO_SUBSCRIPTION_INTEGRATION.md (complete reference)
- And others...

---

## Common Issues

| Error | Fix |
|-------|-----|
| Build fails | Ensure SDK is .NET 10+, set `DOTNET_ROLL_FORWARD=Major` |
| "Invalid URI" at startup | Set all three Maxio env vars |
| 401 Unauthorized | Add `Authorization: Bearer {TOKEN}` header |
| Endpoint not found | Ensure PublicApi is running on `https://localhost:28543` |

---

## That's It!

The integration is complete and ready. All three endpoints are working and integrated with Maxio's API for managing subscriptions.

For detailed information, see:
- **VERIFICATION_COMPLETE.md** - Complete verification with expected responses
- **MAXIO_SUBSCRIPTION_INTEGRATION.md** - Full technical reference
- **QUICK_START.md** - Step-by-step setup guide
