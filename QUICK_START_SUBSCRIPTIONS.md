# Quick Start: Maxio Subscription Billing

## 5-Minute Setup

### 1. Set Your Maxio Credentials
```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
$env:MAXIO_API_KEY = "your-api-key-here"
$env:MAXIO_SITE_SUBDOMAIN = "your-subdomain"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

### 2. Build and Run
```powershell
cd repo
dotnet build eShopOnWeb.sln -c Release
dotnet run --project src/PublicApi
```

### 3. Get Auth Token (PowerShell)
```powershell
$auth = @{ username = "demouser@microsoft.com"; password = "Pass@word1" } | ConvertTo-Json
$token = (Invoke-RestMethod -Uri "https://localhost:27503/api/authenticate" `
  -Method Post -Body $auth -ContentType application/json -SkipCertificateCheck).token
```

### 4. Get Plans
```powershell
$headers = @{ Authorization = "Bearer $token" }
Invoke-RestMethod "https://localhost:27503/api/subscription-plans" `
  -Headers $headers -SkipCertificateCheck | ConvertTo-Json
```

### 5. Create Subscription
```powershell
$body = @{ productHandle = "eshop-pro" } | ConvertTo-Json
Invoke-RestMethod "https://localhost:27503/api/subscriptions" `
  -Method Post -Headers $headers -Body $body `
  -ContentType application/json -SkipCertificateCheck | ConvertTo-Json
```

### 6. View My Subscriptions
```powershell
Invoke-RestMethod "https://localhost:27503/api/my-subscriptions" `
  -Headers $headers -SkipCertificateCheck | ConvertTo-Json
```

## API Summary

| Endpoint | Method | Purpose | Auth |
|----------|--------|---------|------|
| `/api/subscription-plans` | GET | List available plans | Required |
| `/api/subscriptions` | POST | Create subscription | Required |
| `/api/my-subscriptions` | GET | View your subscriptions | Required |

## Default Credentials
- **Username**: demouser@microsoft.com
- **Password**: Pass@word1

## Troubleshooting

**401 Unauthorized?**
- Get a fresh token from /api/authenticate

**400 Bad Request?**
- Check your Maxio credentials in environment variables
- Verify the subdomain is correct
- Ensure API key has proper permissions

**User already has subscription?**
- Use a different user account or reset the in-memory database

**SSL Certificate Error?**
- This is normal for localhost dev. PowerShell script uses `-SkipCertificateCheck`

## Full Documentation
See `MAXIO_INTEGRATION_VERIFICATION.md` for comprehensive testing guide.
