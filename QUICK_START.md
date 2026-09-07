# Quick Start: Maxio Subscription Billing

## 30-Second Setup

```bash
# 1. Set Maxio credentials (user-secrets)
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-subdomain"

# 2. Build & run
cd ../..
$env:DOTNET_ROLL_FORWARD="Major"
dotnet build eShopOnWeb.sln
dotnet run --project src/PublicApi

# 3. Note the HTTPS port (usually 7200), then in another terminal:
curl -X POST "https://localhost:7200/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@example.com","password":"password"}' \
  --insecure

# 4. Copy the token and test subscription endpoints
curl -X GET "https://localhost:7200/api/subscription-plans" \
  -H "Authorization: Bearer YOUR_TOKEN" --insecure
```

## The Three Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/subscription-plans` | GET | List available plans |
| `/api/subscriptions` | POST | Subscribe to a plan |
| `/api/my-subscriptions` | GET | List user's subscriptions |

**All require JWT bearer token in `Authorization` header.**

## What Was Added

```
✅ SubscriptionService — handles all Maxio SDK calls + error handling
✅ 3 RESTful endpoints — list plans, create subscription, list user subscriptions  
✅ JWT authentication — all endpoints require authentication
✅ Idempotent customer creation — same user never gets duplicate Maxio customer
✅ Comprehensive error handling — typed errors, JSON exceptions, transport failures
✅ Configuration from env vars — credentials never hardcoded
✅ Full documentation — setup guide, verification guide, architecture summary
✅ Builds successfully — zero compile errors, ready to test
```

## Verify It Works

```bash
# 1. Confirm build succeeds
dotnet build eShopOnWeb.sln  # Should show "Build succeeded"

# 2. Follow VERIFICATION_GUIDE.md steps (copy/paste curl commands)
# Endpoint 1: List plans
# Endpoint 2: Create subscription
# Endpoint 3: List my subscriptions

# Expected results:
# ✓ Plans from Maxio are returned
# ✓ Subscription is created in Maxio
# ✓ Same user never gets duplicate Maxio customer
# ✓ User's subscriptions are listed correctly
# ✓ Errors (401, 404, 422, 500) return appropriate HTTP status
```

## Documentation

- **MAXIO_SETUP.md** — Detailed setup, configuration, environment variables
- **VERIFICATION_GUIDE.md** — Step-by-step testing with curl examples
- **INTEGRATION_SUMMARY.md** — Architecture, design decisions, implementation details
- **maxio-plan.md** — SDK contract sheet (signatures, wire names, enums)

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Build fails ("Cannot find package") | Run `dotnet restore eShopOnWeb.sln` |
| Endpoints return 401 | Check JWT token is valid, check Authorization header format |
| Endpoints return 500 | Check Maxio credentials (ApiKey, Subdomain) in user-secrets |
| "Customer not found" on subscribe | Expected on first call; creates customer automatically |
| Port is not 7200 | Check the launchSettings.json or console output for actual port |

## Next Steps

1. ✅ **Test locally** — Follow VERIFICATION_GUIDE.md
2. 🔄 **Integrate frontend** — Call `/api/subscriptions` from Blazor/React UI
3. 📦 **Deploy** — Set env vars in production (see MAXIO_SETUP.md)
4. 🎉 **Monitor** — Watch subscription creation/renewal in Maxio dashboard

## Key Files

| File | Purpose |
|------|---------|
| `src/PublicApi/SubscriptionEndpoints/SubscriptionService.cs` | All Maxio SDK calls + error boundary |
| `src/PublicApi/Program.cs` | DI setup for MaxioAdvancedBillingClient |
| `src/PublicApi/appsettings.json` | Maxio config section (placeholder values) |
| `maxio-plan.md` | SDK contract sheet (all signatures & enums) |

## API Examples

### List Plans
```bash
curl -H "Authorization: Bearer $TOKEN" \
  https://localhost:7200/api/subscription-plans --insecure
```
**Response:** Plans from Maxio product family (id, name, handle, price, interval)

### Subscribe
```bash
curl -X POST -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  https://localhost:7200/api/subscriptions --insecure
```
**Response:** Subscription details (id, customerId, state, createdAt)

### List My Subscriptions
```bash
curl -H "Authorization: Bearer $TOKEN" \
  https://localhost:7200/api/my-subscriptions --insecure
```
**Response:** Array of user's subscriptions

## Credentials (Important!)

Never commit credentials to the repository.

```bash
# ✅ Correct: Use user-secrets or environment variables
dotnet user-secrets set "Maxio:ApiKey" "..."

# ❌ Wrong: Don't hardcode in appsettings.json
"Maxio": { "ApiKey": "..." }  // BAD!

# ❌ Wrong: Don't commit to git
git add appsettings.json  // Don't do this with secrets!
```

For production, use a secrets manager (Azure Key Vault, AWS Secrets Manager, etc.) and inject via environment variables.

---

**Questions?** See VERIFICATION_GUIDE.md or INTEGRATION_SUMMARY.md for detailed docs.
