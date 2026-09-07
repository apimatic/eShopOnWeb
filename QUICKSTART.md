# Quick Start: Maxio Subscriptions in eShopOnWeb

## One-Time Setup

### 1. Get Maxio Credentials
- Log into [Maxio Sandbox](https://cp-exp-3.chargify.com/)
- Go to Settings → API Keys
- Create an API key
- Note: Subdomain is in the URL (e.g., `cp-exp-3`)

### 2. Store Credentials (Never in code!)
```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "sk_live_..."
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-3"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### 3. Set Environment Variables
```bash
# PowerShell
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

## Build & Run

```bash
# Build
dotnet build

# Run
cd src/PublicApi
dotnet run

# App will be at: https://localhost:28383
```

## Test the Integration

### 1. Get Auth Token
```bash
curl -X POST https://localhost:28383/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  --insecure

# Save the "token" value
```

### 2. List Plans
```bash
curl https://localhost:28383/api/subscription-plans --insecure
```

### 3. Subscribe
```bash
curl -X POST https://localhost:28383/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

### 4. View Subscriptions
```bash
curl https://localhost:28383/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  --insecure
```

## That's It!

✅ Build succeeds  
✅ App starts  
✅ Endpoints respond  
✅ Subscriptions created in Maxio  

For detailed documentation, see:
- `SUBSCRIPTION_INTEGRATION_GUIDE.md` — Full setup & testing guide
- `IMPLEMENTATION_SUMMARY.md` — Architecture & design
- `VERIFICATION_CHECKLIST.md` — Detailed verification items
