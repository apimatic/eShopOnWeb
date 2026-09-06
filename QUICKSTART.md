# Quick Start: Maxio Subscription Integration

## 5-Minute Setup

### 1. Configure Credentials

From `src/PublicApi` directory:

```bash
dotnet user-secrets set "Maxio:ApiKey" "your_api_key"
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "your_family_handle"
```

Or set environment variables:
```bash
set MAXIO_API_KEY=your_api_key
set MAXIO_SITE_SUBDOMAIN=your_subdomain
set MAXIO_DEFAULT_PRODUCT_FAMILY=your_family_handle
```

### 2. Run the Application

```bash
cd src/PublicApi
set DOTNET_ROLL_FORWARD=Major
set UseOnlyInMemoryDatabase=true
dotnet run
```

API will be available at: `https://localhost:25883`

### 3. Test the Integration

In another terminal:

```bash
./test-subscriptions.ps1
```

Expected output: "All Tests Completed" without errors

## What Gets Created

Three new API endpoints:

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/subscription-plans` | List available plans from Maxio |
| POST | `/api/subscriptions` | Create subscription for user |
| GET | `/api/my-subscriptions` | Get user's subscriptions |

All endpoints require JWT authentication.

## Key Points

- ✅ No SQL Server LocalDB required (uses in-memory database)
- ✅ Secrets never stored in repository (user-secrets only)
- ✅ Idempotent customer creation (no duplicates)
- ✅ Existing cart/checkout flow unchanged
- ✅ Runs on .NET 10 SDK (rolls forward from 8.0)

## Sandbox Credentials

For testing, you need a Maxio sandbox account with:
- API Key
- Site subdomain (e.g., `cp-exp-2`)
- Product Family containing subscription plans

## Test User

Built-in demo user:
- Username: `demouser@microsoft.com`
- Password: `Pass@word1`

## Verify It Works

```bash
# 1. Application starts without errors
# 2. Swagger UI accessible: https://localhost:25883/swagger
# 3. Test script completes all 4 steps successfully
# 4. Maxio dashboard shows new customer and subscription
```

## Troubleshooting

**"Maxio configuration incomplete"** → Set Maxio credentials (Step 1)

**"Certificate validation error"** → Dev cert trusted: `dotnet dev-certs https --trust`

**"Cannot find plan"** → Verify plan handle matches ProductFamilyHandle config

For more details, see:
- `SUBSCRIPTION_INTEGRATION.md` — Complete guide
- `IMPLEMENTATION_SUMMARY.md` — Architecture overview
- `test-subscriptions.ps1` — Automated test script
