# Quick Start - Verify Maxio Subscription Integration

## In 5 Minutes

### 1. Set Up Credentials (30 seconds)

```powershell
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:Environment" "sandbox"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
cd ..\..
```

### 2. Build Solution (45 seconds)

```bash
dotnet build eShopOnWeb.sln
```

Expected: Build succeeds with no errors.

### 3. Run PublicApi (start in separate terminal)

```bash
$env:DOTNET_ROLL_FORWARD = "Major"
cd src/PublicApi
dotnet run
```

Expected: App starts and listens on `https://localhost:28623`

### 4. Test Endpoints (in another terminal)

```powershell
# 4a. Authenticate
$auth = @{username="demouser@microsoft.com"; password="Pass@word1"} | ConvertTo-Json
$resp = Invoke-RestMethod -Uri "https://localhost:28623/api/authenticate" `
  -Method Post -ContentType "application/json" -Body $auth -SkipCertificateCheck
$token = $resp.token
Write-Host "Token: $($token.Substring(0,20))..."

# 4b. List subscription plans
$headers = @{"Authorization"="Bearer $token"}
Invoke-RestMethod -Uri "https://localhost:28623/api/subscription-plans" `
  -Method Get -Headers $headers -SkipCertificateCheck | ConvertTo-Json

# 4c. Get my subscriptions
Invoke-RestMethod -Uri "https://localhost:28623/api/my-subscriptions" `
  -Method Get -Headers $headers -SkipCertificateCheck | ConvertTo-Json
```

### 5. Expected Results

- ✅ **Authenticate**: Returns JWT token
- ✅ **List Plans**: Returns `{"plans": []}` (empty with placeholder credentials)
- ✅ **My Subscriptions**: Returns `{"subscriptions": [], "message": "No subscriptions found"}`

---

## Key Files

| File | Purpose |
|------|---------|
| `src/PublicApi/SubscriptionEndpoints/*.cs` | Three API endpoints |
| `src/Infrastructure/Services/MaxioApiService.cs` | Maxio API integration |
| `src/Infrastructure/Identity/MaxioCustomerMapping.cs` | User-to-customer mapping |
| `VERIFICATION_GUIDE.md` | Complete testing details |

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/subscription-plans` | List available plans |
| POST | `/api/subscriptions` | Create subscription (`{"planHandle":"..."}`) |
| GET | `/api/my-subscriptions` | Get user's subscriptions |

All require JWT Bearer authentication.

## Next Steps

1. With real Maxio credentials:
   - Update user secrets
   - Plans will populate from Maxio
   - Subscriptions can be created

2. See `VERIFICATION_GUIDE.md` for complete testing scenarios

3. See `IMPLEMENTATION_SUMMARY.md` for architecture details
