# Maxio Subscription Billing - Quick Start

## In 5 Minutes

### 1. Set Maxio Credentials
```bash
# Option A: Environment Variables
$env:Maxio:ApiKey = "your_api_key"
$env:Maxio:Subdomain = "your_subdomain" 
$env:Maxio:ProductFamilyHandle = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"

# Option B: User Secrets
dotnet user-secrets set "Maxio:ApiKey" "your_api_key"
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### 2. Build & Run
```bash
# Build
dotnet build eShopOnWeb.sln

# Run PublicApi
cd src/PublicApi
dotnet run
# Opens https://localhost:25583
```

### 3. Test the Endpoints

**Get JWT Token:**
```bash
$token = (Invoke-RestMethod -Uri "https://localhost:25583/api/authenticate" -Method Post `
  -ContentType "application/json" -SkipCertificateCheck `
  -Body '{"username":"demouser@example.com","password":"DemoPassword123!"}').token

$h = @{ Authorization = "Bearer $token" }
```

**List Plans:**
```bash
Invoke-RestMethod "https://localhost:25583/api/subscription-plans" -Headers $h -SkipCertificateCheck
```

**Create Subscription:**
```bash
Invoke-RestMethod "https://localhost:25583/api/subscriptions" -Method Post -Headers $h `
  -ContentType "application/json" -SkipCertificateCheck `
  -Body '{"productHandle":"eshop-pro"}'
```

**List My Subscriptions:**
```bash
Invoke-RestMethod "https://localhost:25583/api/my-subscriptions" -Headers $h -SkipCertificateCheck
```

## What's New

| Endpoint | Method | Auth | Purpose |
|----------|--------|------|---------|
| `/api/subscription-plans` | GET | JWT | List plans |
| `/api/subscriptions` | POST | JWT | Create subscription |
| `/api/my-subscriptions` | GET | JWT | View subscriptions |

## Files

- **IMPLEMENTATION_COMPLETE.md** - Full implementation details
- **MAXIO_INTEGRATION_GUIDE.md** - Comprehensive setup & troubleshooting
- **QUICK_START.md** - This file (quick reference)

## For Help

See **MAXIO_INTEGRATION_GUIDE.md** for:
- Detailed configuration steps
- Troubleshooting
- Production deployment
- API references
