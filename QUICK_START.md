# Maxio Subscription Billing - Quick Start (5 Minutes)

## 1. Set Environment Variables

```powershell
# PowerShell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "your-subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:DOTNET_ROLL_FORWARD = "Major"  # If using .NET 8
```

## 2. Build and Run

```bash
cd repo
dotnet build
cd src/PublicApi
dotnet run
```

API runs at: `https://localhost:25723/api/`

## 3. Test with PowerShell Script

```powershell
# In a new terminal
cd repo
PowerShell -ExecutionPolicy Bypass -File test-maxio-integration.ps1
```

This script:
- ✓ Authenticates and gets JWT token
- ✓ Lists available subscription plans
- ✓ Creates a subscription
- ✓ Lists user subscriptions
- ✓ Verifies idempotency

## 4. Manual Testing (cURL)

```bash
# Get token
TOKEN=$(curl -s -X POST https://localhost:25723/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word123"}' \
  -k | jq -r '.token')

# List plans
curl https://localhost:25723/api/subscription-plans -k

# Create subscription
curl -X POST https://localhost:25723/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' -k

# List user subscriptions
curl https://localhost:25723/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" -k
```

## API Endpoints

| Method | Endpoint | Auth | Response |
|--------|----------|------|----------|
| GET | `/api/subscription-plans` | None | List of plans |
| POST | `/api/subscriptions` | JWT | Created subscription |
| GET | `/api/my-subscriptions` | JWT | User's subscriptions |

## Key Features

- ✓ Automatic customer creation (idempotent)
- ✓ No payment method required
- ✓ JWT-authenticated endpoints
- ✓ Full subscription lifecycle data
- ✓ Proper error handling & logging

## Troubleshooting

**"Product not found"**
→ Check product handle, verify product exists in Maxio product family

**"Cannot connect to Maxio"**
→ Verify API key, subdomain, and environment variables are set

**"Certificate error"**
→ Use `-k` flag with curl or `-SkipCertificateCheck` with PowerShell

**"SDK version mismatch"**
→ Set `DOTNET_ROLL_FORWARD=Major` or install ASP.NET Core 8.0 runtime

## More Information

- Full setup guide: `MAXIO_BILLING_SETUP.md`
- Complete summary: `INTEGRATION_SUMMARY.md`
- Test script details: `test-maxio-integration.ps1`

## Integration Points

The integration is located in:
- `src/PublicApi/MaxioBilling/` - Service layer
- `src/PublicApi/SubscriptionEndpoints/` - Endpoints
- `src/PublicApi/MaxioSettings.cs` - Configuration
- `src/PublicApi/Program.cs` - Registration (lines 34-35)

Ready to go! 🚀
