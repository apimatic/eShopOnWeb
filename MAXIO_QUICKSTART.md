# Maxio Integration Quick Start

## 5-Minute Setup

### 1. Set Environment Variables
```bash
# Windows (PowerShell)
$env:MAXIO_API_KEY = "your_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"  
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"

# macOS/Linux
export MAXIO_API_KEY="your_key"
export MAXIO_SITE_SUBDOMAIN="your_subdomain"
export UseOnlyInMemoryDatabase="true"
```

### 2. Build
```bash
cd repo
dotnet build src/PublicApi/PublicApi.csproj
```

### 3. Run PublicApi
```bash
cd src/PublicApi
dotnet run
# Starts on https://localhost:27303
```

## Quick Test

### Get Token
```bash
curl -X POST https://localhost:27303/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"buyer","password":"Pass@word1"}' -k
# Copy the "token" value
```

### List Plans
```bash
TOKEN="your_token_here"
curl https://localhost:27303/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" -k
```

### Create Subscription
```bash
curl -X POST https://localhost:27303/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' -k
```

### List My Subscriptions
```bash
curl https://localhost:27303/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" -k
```

## Key Files

| File | Purpose |
|------|---------|
| `src/ApplicationCore/MaxioConfiguration.cs` | Config model |
| `src/Infrastructure/Services/MaxioApiClient.cs` | HTTP client |
| `src/Infrastructure/Services/MaxioService.cs` | Business logic |
| `src/PublicApi/SubscriptionEndpoints/*.cs` | API endpoints |
| `src/PublicApi/appsettings.json` | Configuration section |
| `MAXIO_SETUP_GUIDE.md` | Full setup & verification |
| `MAXIO_IMPLEMENTATION.md` | Architecture details |

## Three Endpoints

1. **GET /api/subscription-plans** — List available plans
2. **POST /api/subscriptions** — Subscribe to a plan
3. **GET /api/my-subscriptions** — List user's subscriptions

All require JWT bearer token (get via `/api/authenticate`).

## Sandbox Plans

- **eshop-pro**: $299/mo (Professional)
- **basic-plan**: $29/mo (Basic)

Both in product family: **eshop-subscribe**

## Important Notes

- ✅ No payment required (plans configured with payment_method_not_required=true)
- ✅ No database schema changes
- ✅ Works with in-memory database
- ✅ Idempotent customer creation (same user = same Maxio customer)
- ✅ Completely separate from cart/checkout flow

## Troubleshooting

| Issue | Fix |
|-------|-----|
| "Unauthorized" | Get token from `/api/authenticate` first |
| "Failed to create subscription" | Check MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN |
| Empty plans list | Verify MAXIO_DEFAULT_PRODUCT_FAMILY env var or appsettings |
| Build errors | Run `dotnet clean && dotnet build` |

## What Was Built

✅ Maxio configuration model  
✅ HTTP client with auth headers  
✅ Business service layer (idempotent customer creation)  
✅ 3 REST endpoints (JWT-authenticated)  
✅ Full documentation & setup guides  
✅ No secrets in repository  
✅ Production-grade error handling  
✅ Works with existing eShopOnWeb infrastructure  

## Next Steps

1. **Read** `MAXIO_SETUP_GUIDE.md` for complete setup & verification
2. **Read** `MAXIO_IMPLEMENTATION.md` for architecture deep-dive
3. **Test** the endpoints using curl examples above
4. **Verify** in Maxio sandbox portal that customers/subscriptions are created
5. **Integrate** into web frontend UI

See main documents for advanced topics (webhooks, caching, extending with new endpoints).
