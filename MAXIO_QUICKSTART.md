# Maxio Integration — Quick Start

Get the Maxio subscription billing integration running in 5 minutes.

## Prerequisites

- .NET SDK 8.0+ (or .NET 10 with `DOTNET_ROLL_FORWARD=Major`)
- Maxio Advanced Billing sandbox account
- Seeded product family `eshop-subscribe` with plans `eshop-pro` and `basic-plan`

## Step 1: Configure Credentials

Navigate to the PublicApi project and set user-secrets:

```bash
cd src/PublicApi

# Set your Maxio credentials (replace with actual values)
dotnet user-secrets set "Maxio:ApiKey" "your_api_key_here"
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain_here"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

## Step 2: Build

```bash
cd ../..
dotnet build eShopOnWeb.sln
```

## Step 3: Run

**Option A: In-Memory Database (for quick testing)**

```powershell
# PowerShell
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --project src/PublicApi/PublicApi.csproj
```

**Option B: SQL Server LocalDB (persisted data)**

```powershell
# Ensure SQL Server LocalDB is installed
# Update connection strings in src/PublicApi/appsettings.json if needed
dotnet run --project src/PublicApi/PublicApi.csproj
```

## Step 4: Test

Open a new terminal and test the endpoints:

### List subscription plans
```bash
curl -X GET "https://localhost:24723/api/subscription-plans" \
  -H "Accept: application/json" \
  -k
```

### Get JWT token
```bash
curl -X POST "https://localhost:24723/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "P@ssw0rd!"
  }' \
  -k
```

Copy the `token` from the response.

### Create subscription (replace TOKEN with actual token)
```bash
curl -X POST "https://localhost:24723/api/subscriptions" \
  -H "Authorization: Bearer TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}' \
  -k
```

### Get user's subscriptions (replace TOKEN with actual token)
```bash
curl -X GET "https://localhost:24723/api/my-subscriptions" \
  -H "Authorization: Bearer TOKEN" \
  -k
```

## What Happens

1. **GET /api/subscription-plans** fetches available plans from Maxio
2. **POST /api/subscriptions** creates a Maxio customer (if new) and subscribes them to a plan
3. **GET /api/my-subscriptions** lists the user's subscriptions from the local database

Each subscription is stored locally and linked to the authenticated user.

## Troubleshooting

**"Cannot find Maxio API"**
- Check credentials are set correctly: `dotnet user-secrets list`
- Verify subdomain and API key match your Maxio sandbox account
- Ensure network connectivity to Maxio

**"Database error: LocalDB not found"**
- Set `UseOnlyInMemoryDatabase=true` to use in-memory database
- Or install SQL Server Express with LocalDB

**"ASP.NET Core 8.0 runtime not found"**
```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --project src/PublicApi/PublicApi.csproj
```

**"Unauthorized" on subscription endpoints**
- Ensure JWT token is included: `Authorization: Bearer <token>`
- Re-authenticate if token expired
- Check token is not malformed

## Next Steps

- See `MAXIO_INTEGRATION_VERIFICATION.md` for detailed testing guide
- See `IMPLEMENTATION_SUMMARY.md` for architecture overview
- Visit https://developers.maxio.com for Maxio API reference

## Notes

- **Secrets**: Never commit your Maxio API key. User-secrets are stored locally in Windows user profile
- **Database**: In-memory database loses data on restart. Use LocalDB for persistence
- **Ports**: PublicApi runs on port 24723 (HTTPS) and 24724 (HTTP)
- **Auth**: Subscription endpoints require JWT token from `/api/authenticate`

Happy subscribing! 🎉
