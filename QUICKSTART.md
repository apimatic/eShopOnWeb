# Maxio Subscription Integration - Quick Start

## 1. Build the Project

```bash
cd C:\path\to\repo

# If using .NET 10 on a machine without .NET 8
$env:DOTNET_ROLL_FORWARD = "Major"

dotnet build eShopOnWeb.sln --configuration Debug
```

✅ Expected: `Build succeeded` with 0 errors.

## 2. Configure Maxio Credentials

Set your Maxio sandbox credentials using user-secrets:

```bash
cd src/PublicApi

# Replace with your actual values from Maxio sandbox
dotnet user-secrets set "Maxio:ApiKey" "your-api-key-here"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-4"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: custom base URL
# dotnet user-secrets set "Maxio:BaseUrl" "https://custom.maxio.com"
```

Verify secrets are set:
```bash
dotnet user-secrets list
```

## 3. Run PublicApi

```bash
# From src/PublicApi directory
$env:UseOnlyInMemoryDatabase = "true"
dotnet run
```

The API will start at: `https://localhost:25203`

Swagger UI: https://localhost:25203/swagger

## 4. Quick Test

### Get JWT Token

```powershell
$token = (Invoke-WebRequest -Uri "https://localhost:25203/api/authenticate" `
    -Method Post `
    -Headers @{"Content-Type"="application/json"} `
    -Body '{"username":"demouser@microsoft.com","password":"Pass@word1"}' `
    -SkipCertificateCheck).Content | ConvertFrom-Json | Select-Object -ExpandProperty token
```

### List Plans

```powershell
Invoke-WebRequest -Uri "https://localhost:25203/api/subscription-plans" `
    -Method Get `
    -SkipCertificateCheck | Select-Object -ExpandProperty Content
```

### Create Subscription

```powershell
Invoke-WebRequest -Uri "https://localhost:25203/api/subscriptions" `
    -Method Post `
    -Headers @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
    } `
    -Body '{"planHandle":"eshop-pro"}' `
    -SkipCertificateCheck | Select-Object -ExpandProperty Content
```

### Get My Subscriptions

```powershell
Invoke-WebRequest -Uri "https://localhost:25203/api/my-subscriptions" `
    -Method Get `
    -Headers @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
    } `
    -SkipCertificateCheck | Select-Object -ExpandProperty Content
```

## 5. Run Automated Verification

```powershell
# From repo root
.\verify_subscription_integration.ps1 `
    -ApiUrl "https://localhost:25203" `
    -Username "demouser@microsoft.com" `
    -Password "Pass@word1" `
    -PlanHandle "eshop-pro"
```

## 6. Explore Swagger UI

Open https://localhost:25203/swagger in browser to:
- See all API endpoints
- Test endpoints with "Try it out" button
- View request/response schemas
- Check authentication requirements

## Next Steps

For detailed information, see:
- `MAXIO_INTEGRATION_GUIDE.md` - Full setup and troubleshooting
- `SUBSCRIPTION_IMPLEMENTATION_SUMMARY.md` - Architecture and design decisions

## Troubleshooting

**Build fails with "Services" not found**
- Rebuild: `dotnet clean && dotnet build eShopOnWeb.sln`

**API starts but returns errors**
- Check credentials are set: `cd src/PublicApi && dotnet user-secrets list`
- Verify Maxio sandbox is accessible
- Check logs for HTTP errors

**401 Unauthorized on subscriptions endpoints**
- Token might be expired, get new one
- Verify token is in Authorization header as `Bearer <token>`

**No plans returned**
- Verify product family and plans exist in Maxio sandbox
- Check ProductFamilyHandle matches your Maxio configuration

See `MAXIO_INTEGRATION_GUIDE.md` for more troubleshooting.

## Key Endpoints

| Method | Endpoint | Auth | Purpose |
|--------|----------|------|---------|
| GET | /api/subscription-plans | None | List available plans |
| POST | /api/subscriptions | JWT | Create subscription |
| GET | /api/my-subscriptions | JWT | Get user's subscriptions |
| POST | /api/authenticate | None | Get JWT token (existing) |

## Implementation Files

Core implementation:
- `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs` - Domain entity
- `src/PublicApi/Services/MaxioService.cs` - Maxio API client
- `src/PublicApi/SubscriptionEndpoints/*.cs` - API endpoints
- `src/Infrastructure/Data/Migrations/20260906000000_AddSubscriptionTable.cs` - Database

Configuration:
- `src/PublicApi/MaxioSettings.cs` - Settings model
- `src/PublicApi/appsettings.json` - Configuration keys
- `src/PublicApi/Program.cs` - DI registration

## That's It! 🎉

Your Maxio subscription integration is ready to use. Start with step 1 (build), then step 2 (configure), and you can test immediately.
