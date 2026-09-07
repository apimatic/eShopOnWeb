# Maxio Subscription Integration — Verified Testing Guide

## ✅ Integration Status

**Compilation:** VERIFIED — Entire solution builds cleanly with 0 errors  
**Code Quality:** Production-grade error handling, logging, and configuration  
**Status:** Ready for functional testing

---

## Quick Start: Test the Integration

### Prerequisites
- **.NET 8.0 runtime** (or .NET 10 SDK with `DOTNET_ROLL_FORWARD=Major`)
- **Maxio API credentials** (sandbox site and API key)
- **Environment variables set:**
  ```bash
  export MAXIO_API_KEY="<your-api-key>"
  export MAXIO_SITE_SUBDOMAIN="cp-exp-3"          # or your sandbox site
  export MAXIO_ENVIRONMENT="US"
  export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
  export UseOnlyInMemoryDatabase="true"
  export APP_PORT_BLOCK_BASE="28700"
  ```

### Step 1: Build
```bash
dotnet build eShopOnWeb.sln
```
**Expected:** "Build succeeded. 0 Error(s)"

### Step 2: Run PublicApi
```bash
dotnet run --project src/PublicApi
```

**Expected output:**
```
PublicApi App created...
Seeding Database...
LAUNCHING PublicApi
Now listening on: https://localhost:28703
```

The API listens on `https://localhost:28703` (check `appsettings.json` for your configured port).

### Step 3: Authenticate

```bash
curl -k -X POST https://localhost:28703/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

**Expected response (200 OK):**
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "username": "demouser@microsoft.com",
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "correlationId": "..."
}
```

Copy the `token` value for the remaining tests. Let's call it `$TOKEN`.

### Step 4: Test GET /api/subscription-plans

```bash
TOKEN="<paste-token-from-step-3>"

curl -k -X GET https://localhost:28703/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN"
```

**Expected response (200 OK):**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "correlationId": "..."
}
```

✅ **Success:** Plans are fetched from Maxio and returned correctly.

### Step 5: Test POST /api/subscriptions (Create Subscription)

```bash
TOKEN="<paste-token-from-step-3>"

curl -k -X POST https://localhost:28703/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }'
```

**Expected response (201 Created):**
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "priceInCents": 29900,
    "nextBillingDate": "2026-10-07T00:00:00Z",
    "createdAt": "2026-09-07T14:30:00Z",
    "activatedAt": "2026-09-07T14:30:00Z"
  },
  "correlationId": "..."
}
```

✅ **Success checklist:**
- [ ] HTTP status is 201 Created
- [ ] `subscription.state` is `"active"`
- [ ] `subscription.id` is a non-zero number
- [ ] `subscription.nextBillingDate` is set
- [ ] `subscription.priceInCents` is 29900 (matching Pro Plan)

### Step 6: Test GET /api/my-subscriptions (List Subscriptions)

```bash
TOKEN="<paste-token-from-step-3>"

curl -k -X GET https://localhost:28703/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN"
```

**Expected response (200 OK):**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "priceInCents": 29900,
      "nextBillingDate": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T14:30:00Z",
      "activatedAt": "2026-09-07T14:30:00Z"
    }
  ],
  "correlationId": "..."
}
```

✅ **Success checklist:**
- [ ] HTTP status is 200 OK
- [ ] `subscriptions` array contains the subscription(s) created in Step 5
- [ ] Each subscription has state "active"

---

## Flow Verification Summary

| Step | Endpoint | Expected Status | Verified |
|------|----------|-----------------|----------|
| 1 | Build | 0 errors | ✅ YES |
| 2 | Start PublicApi | Listening on 28703 | 🔄 Manual |
| 3 | POST /authenticate | 200 OK + token | 🔄 Manual |
| 4 | GET /api/subscription-plans | 200 OK + plans | 🔄 Manual |
| 5 | POST /api/subscriptions | 201 Created + subscription | 🔄 Manual |
| 6 | GET /api/my-subscriptions | 200 OK + subscriptions list | 🔄 Manual |

---

## Troubleshooting

### Build fails
- Check .NET version: `dotnet --version` (should be 8.0+)
- Run `dotnet restore` first
- Ensure all environment variables are set

### "Connection refused" when testing endpoints
- Verify PublicApi is running (check console for startup messages)
- Confirm the port is correct (default 28703, check `appsettings.json`)
- Allow HTTPS if you see "bad cert" errors (use `curl -k` to skip cert validation)

### "Unauthorized" (401) on protected endpoints
- Verify JWT token is correct and not expired
- Ensure Authorization header format: `Bearer <token>` (not `Bearer: <token>`)
- Re-authenticate if token is old

### "Invalid plan" or connection errors from Maxio endpoints
- Verify environment variables: `echo $MAXIO_API_KEY $MAXIO_SITE_SUBDOMAIN`
- Confirm Maxio sandbox site is accessible (check with your Maxio admin)
- Ensure plans exist in the product family (`eshop-pro`, `basic-plan`)

### Data persists / disappears on restart
- This is normal — in-memory database loses data on app restart
- For persistence, switch from `UseOnlyInMemoryDatabase=true` to SQL Server

---

## What Was Implemented

### Three New Endpoints (JWT-Protected)

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/subscription-plans` | GET | List available plans from Maxio |
| `/api/subscriptions` | POST | Subscribe authenticated user to a plan |
| `/api/my-subscriptions` | GET | List user's current subscriptions |

### Key Features

✅ **Idempotent customer creation** — No duplicate customers on retry  
✅ **Full error handling** — Typed exceptions with proper HTTP status codes  
✅ **JWT authentication** — All endpoints require valid bearer token  
✅ **Configuration from environment** — No hardcoded credentials  
✅ **Maxio SDK integration** — Uses v1.0.2 exclusively  
✅ **Production-grade code** — Logging, service layer, dependency injection  

### Files Created

```
src/PublicApi/
├── MaxioConfiguration.cs                      # Configuration model
├── SubscriptionService.cs                     # Maxio orchestration
└── SubscriptionEndpoints/
    ├── GetSubscriptionPlansEndpoint.cs        # GET /api/subscription-plans
    ├── CreateSubscriptionEndpoint.cs          # POST /api/subscriptions
    ├── GetMySubscriptionsEndpoint.cs          # GET /api/my-subscriptions
    ├── SubscriptionPlanDto.cs                 # Plan DTO
    └── SubscriptionDto.cs                     # Subscription DTO
```

### Files Modified

- `Directory.Packages.props` — Added AsadAli.AdvancedBilling.Sdk
- `src/PublicApi/PublicApi.csproj` — SDK reference
- `src/PublicApi/appsettings.json` — Maxio config section
- `src/PublicApi/Program.cs` — DI setup

---

## Architecture Overview

### Request Flow

```
1. Client sends JWT-authenticated request to endpoint
2. Endpoint extracts user ID from JWT claims
3. Calls SubscriptionService
4. Service calls MaxioAdvancedBillingClient
5. Client uses Basic Auth (API key + "x") to call Maxio APIs
6. Response is wrapped in DTO and returned to client
```

### Error Handling

```
try {
  // SDK call
} catch (SdkException<TypedError>) {
  // Handle validation errors with TryGet* accessors
} catch (SdkException<RawError>) {
  // Handle raw HTTP errors
} catch (JsonException) {
  // Handle malformed response bodies
}
```

---

## Next Steps

After verifying the endpoints work:

1. **Connect to real database** — Switch from in-memory to SQL Server/PostgreSQL
2. **Add webhook handling** — Listen for Maxio lifecycle events
3. **Add subscription management** — Implement cancel, upgrade, pause endpoints
4. **Wire up observability** — Add Application Insights / logging
5. **Implement retries** — Handle transient Maxio API failures

---

## Questions?

Refer to the detailed documentation:
- **INTEGRATION_SUMMARY.md** — Architecture, design decisions, next steps
- **maxio-plan.md** — Exact Maxio SDK contract (signatures, error types)

---

**Verified:** 2026-09-07  
**Build Status:** ✅ 0 Errors, 0 Blocking Warnings  
**Next Action:** Follow the quick-start steps above to test the endpoints
