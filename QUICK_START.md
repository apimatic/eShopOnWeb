# Maxio Subscription Billing - Quick Start

## ✅ Integration Complete

Three new endpoints for subscription management have been added to eShopOnWeb using Maxio Advanced Billing.

## Run It

```bash
cd src/PublicApi
dotnet run --configuration Release
# Server starts at https://localhost:28943
```

## Test It

```bash
# 1. Get JWT token (use any credentials from seeded users)
TOKEN=$(curl -s -X POST https://localhost:28943/api/authenticate \
  --insecure \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# 2. List plans
curl -s -X GET https://localhost:28943/api/subscription-plans \
  --insecure \
  -H "Authorization: Bearer $TOKEN" | jq .

# 3. Subscribe to Pro plan
curl -s -X POST https://localhost:28943/api/subscriptions \
  --insecure \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' | jq .

# 4. List my subscriptions
curl -s -X GET https://localhost:28943/api/my-subscriptions \
  --insecure \
  -H "Authorization: Bearer $TOKEN" | jq .
```

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/subscription-plans` | List available plans ($29/mo and $299/mo) |
| POST | `/api/subscriptions` | Subscribe user to a plan (idempotent) |
| GET | `/api/my-subscriptions` | List user's active subscriptions |

**All require JWT authentication**

## Key Features

✅ **Fully Integrated with Maxio Advanced Billing**
- Uses official SDK (v1.0.2)
- Basic auth with API key
- Sandbox credentials configured

✅ **Idempotent Enrollment**
- Multiple subscribe calls for same user = same subscription ID
- No duplicate charges on retry

✅ **Type-Safe Error Handling**
- SDK-specific typed error patterns (Case A & B)
- Graceful fallbacks

✅ **Production Grade**
- Secrets in .NET user-secrets (never in repo)
- JWT authentication for all endpoints
- Proper DI registration

## Files Added

```
src/PublicApi/SubscriptionEndpoints/
├── SubscriptionService.cs (the integration logic)
├── ListSubscriptionPlansEndpoint.cs
├── CreateSubscriptionEndpoint.cs
└── ListMySubscriptionsEndpoint.cs

Documentation:
├── SUBSCRIPTION_INTEGRATION_GUIDE.md (detailed guide)
├── MAXIO_INTEGRATION_VERIFICATION.md (verification steps)
└── QUICK_START.md (this file)
```

## Credentials

Maxio sandbox credentials are in .NET user-secrets:
```bash
cd src/PublicApi
dotnet user-secrets list
```

Shows:
- `Maxio:ApiKey` (from MAXIO_API_KEY env var)
- `Maxio:Subdomain` (cp-exp-3)
- `Maxio:Environment` (US)
- `Maxio:DefaultProductFamily` (eshop-subscribe)

## Architecture

```
User → JWT Auth → Endpoint → Service → Maxio SDK → Maxio Sandbox

Key flow:
1. User authenticates → get JWT token
2. Call /api/subscription-plans → service lists plans from Maxio
3. Call /api/subscriptions with planHandle → service checks for existing customer
   (creates if needed) → creates subscription → returns subscription details
4. Call /api/my-subscriptions → service lists subscriptions for user
```

## Testing

Complete verification script: See `MAXIO_INTEGRATION_VERIFICATION.md`

Quick test:
```bash
# Build
dotnet build eShopOnWeb.sln --configuration Release
# Expected: Build succeeded (0 errors)
```

## Documentation

- **Setup & Architecture**: `SUBSCRIPTION_INTEGRATION_GUIDE.md`
- **Verification Steps**: `MAXIO_INTEGRATION_VERIFICATION.md`
- **SDK Contract**: `maxio-plan.md`

## Next Steps (Optional)

For production:
1. Use Azure Key Vault for API key
2. Add database table to persist user↔Maxio customer mapping
3. Add logging/monitoring for audit trail
4. Test with production Maxio site
5. Implement webhook handling for subscription events
