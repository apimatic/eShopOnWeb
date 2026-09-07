# Maxio Subscription Integration - Quick Start

## 60-Second Setup

### 1. Verify Credentials
```bash
echo $MAXIO_API_KEY
echo $MAXIO_SITE_SUBDOMAIN
```

Should output your sandbox credentials.

### 2. User Secrets Already Configured
User secrets have been set up during implementation:
```bash
cd src/PublicApi
dotnet user-secrets list
# Should show: Maxio:ApiKey, Maxio:Subdomain, Maxio:ProductFamilyHandle
```

### 3. Build
```bash
DOTNET_ROLL_FORWARD=Major dotnet build eShopOnWeb.sln
```
Expected: `Build succeeded` with 0 errors.

### 4. Run PublicApi
```bash
cd src/PublicApi
DOTNET_ROLL_FORWARD=Major dotnet run --project PublicApi.csproj
```

Navigate to: `https://localhost:28823/swagger` to see all endpoints.

## Test the Integration

### Step 1: Get Token
```bash
TOKEN=$(curl -s -X POST https://localhost:28823/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"[email protected]","password":"Pass123$"}' \
  --insecure | jq -r '.token')

echo $TOKEN
```

### Step 2: List Plans
```bash
curl https://localhost:28823/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  --insecure | jq .
```

Expected: Two plans (Pro $299/mo, Basic $29/mo)

### Step 3: Create Subscription
```bash
curl -X POST https://localhost:28823/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure | jq .
```

Expected: 201 Created with subscription details

### Step 4: List Your Subscriptions
```bash
curl https://localhost:28823/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure | jq .
```

Expected: Your subscription from Step 3

## Files to Review

- **Implementation Summary**: `MAXIO_IMPLEMENTATION_SUMMARY.md`
- **Verification Guide**: `MAXIO_INTEGRATION_VERIFICATION.md`
- **Contract Sheet**: `maxio-plan.md` (grounded in SDK map)

## Key Features

✅ **Idempotent** — Create subscription twice, get same result (no duplicates)
✅ **Secure** — JWT authentication required, secrets in user-secrets store
✅ **Production-Grade** — Error handling, typed SDK exceptions, configuration management
✅ **Extensible** — Service layer separates Maxio concerns, easy to add webhooks/persistence

## Troubleshooting

**401 Unauthorized?**
- Token expired — get a fresh one from `/api/authenticate`
- Check JWT is in `Authorization: Bearer <token>` format

**422 Unprocessable Entity?**
- Plan handle invalid — use `eshop-pro` or `basic-plan`
- Check credentials are loaded: `dotnet user-secrets list` in `src/PublicApi`

**Build failed?**
- Use `DOTNET_ROLL_FORWARD=Major` (required for .NET 8 on this machine)
- Clear NuGet cache: `dotnet nuget locals all --clear`

## Next Steps

1. Read `MAXIO_IMPLEMENTATION_SUMMARY.md` for architecture overview
2. Follow `MAXIO_INTEGRATION_VERIFICATION.md` for comprehensive testing
3. Extend with: webhooks, admin dashboard, analytics, local persistence
