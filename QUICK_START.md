# Maxio Subscription Billing - Quick Start

## 1. Set Environment Variables

```powershell
# PowerShell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
```

## 2. Build

```bash
cd repo
dotnet build src/PublicApi/PublicApi.csproj
```

Expected: **Build succeeded** (only NuGet vulnerability warnings are expected)

## 3. Run

```bash
cd src/PublicApi
dotnet run
```

Expected: Listening on `https://localhost:24363`

## 4. Test

Open a new terminal:

```bash
# 1. Authenticate
curl -X POST https://localhost:24363/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word123"}' \
  -k

# Save the returned "token" value

# 2. List Plans
curl https://localhost:24363/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k

# 3. Create Subscription
curl -X POST https://localhost:24363/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -d '{"productHandle":"eshop-pro"}' \
  -k

# 4. Get My Subscriptions
curl https://localhost:24363/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -k
```

## Files Changed/Added

**New Services**:
- `src/ApplicationCore/Services/MaxioSettings.cs`
- `src/ApplicationCore/Services/MaxioClient.cs`
- `src/ApplicationCore/Services/MaxioDtos.cs`
- `src/ApplicationCore/Services/SubscriptionService.cs`

**New Endpoints**:
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs`

**Configuration**:
- `src/PublicApi/Program.cs` (updated with Maxio DI)
- `src/PublicApi/appsettings.json` (updated with Maxio section)

**Documentation**:
- `MAXIO_SETUP.md` - Full setup and configuration guide
- `VERIFICATION_GUIDE.md` - Detailed testing guide
- `IMPLEMENTATION_SUMMARY.md` - Architecture and design overview
- `QUICK_START.md` - This file

## Next Steps

1. **Detailed Configuration**: Read `MAXIO_SETUP.md`
2. **Complete Testing**: Follow `VERIFICATION_GUIDE.md`
3. **Architecture Review**: Read `IMPLEMENTATION_SUMMARY.md`
4. **Frontend Integration**: Call the three endpoints from your UI

## Sandbox Reference

- **Site**: cp-exp-2
- **Pro Plan Handle**: eshop-pro ($299/mo)
- **Basic Plan Handle**: basic-plan ($29/mo)
- **Test User**: demouser@microsoft.com / Pass@word123

## Success Criteria

✅ All three endpoints respond to authenticated requests  
✅ Subscriptions appear in Maxio dashboard  
✅ Same customer is reused for multiple subscriptions  
✅ User cannot see other users' subscriptions  

## Troubleshooting

| Issue | Fix |
|-------|-----|
| "Failed to load subscription plans" | Verify MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN |
| "401 Unauthorized" | Include valid JWT token in Authorization header |
| "Failed to create customer" | Verify email is in JWT token claims |
| Build fails | Ensure .NET 8.0 SDK is installed (`dotnet --version`) |

See `VERIFICATION_GUIDE.md` for complete troubleshooting.
