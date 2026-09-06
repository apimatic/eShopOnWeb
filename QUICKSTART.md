# Maxio Subscription Billing - Quick Start

## What Was Built

Three production-grade HTTP endpoints on the PublicApi for subscription management:

| Method | Endpoint | Purpose |
|--------|----------|---------|
| `GET` | `/api/subscription-plans` | List available subscription plans |
| `POST` | `/api/subscriptions` | Subscribe authenticated user to a plan |
| `GET` | `/api/my-subscriptions` | Get all subscriptions for authenticated user |

**All endpoints require JWT Bearer token authentication.**

## Prerequisites

- Maxio sandbox credentials (site: `cp-exp-2`)
- .NET SDK 8.0+ (or .NET 10 with `DOTNET_ROLL_FORWARD=Major`)
- API key with permissions to create customers and subscriptions

## 5-Minute Setup

### 1. Set Environment Variables

**Windows PowerShell:**
```powershell
$env:MAXIO_API_KEY = "your-maxio-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

**Linux/macOS:**
```bash
export MAXIO_API_KEY="your-maxio-api-key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

### 2. Build
```bash
dotnet build src/PublicApi/PublicApi.csproj
```

### 3. Run
```bash
dotnet run --project src/PublicApi/PublicApi.csproj --environment Development
```

Expected output: `LAUNCHING PublicApi`

### 4. Test (in another terminal)

**Get authentication token:**
```bash
curl -X POST https://localhost:27543/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"[GH0st]*"}' \
  -k -s | jq -r '.token'
```

Save the token as `$TOKEN`

**List plans:**
```bash
curl -s https://localhost:27543/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" -k | jq .
```

**Create subscription:**
```bash
curl -s -X POST https://localhost:27543/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' -k | jq .
```

**Get my subscriptions:**
```bash
curl -s https://localhost:27543/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" -k | jq .
```

## Available Plans

Sandbox plans (site `cp-exp-2`):
- **Pro Plan** (handle: `eshop-pro`) - $299/month
- **Basic Plan** (handle: `basic-plan`) - $29/month

Both have:
- No payment method required
- No trial period
- Monthly billing
- Never expire

## API Details

### List Plans - `GET /api/subscription-plans`
```json
{
  "correlationId": "...",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with advanced features",
      "pricePerMonth": 299.00,
      "billingInterval": "Every 1 month",
      "hasTrial": false,
      "trialDays": null
    }
  ]
}
```

### Subscribe - `POST /api/subscriptions`
**Request:**
```json
{ "planHandle": "eshop-pro" }
```

**Response (Success):**
```json
{
  "success": true,
  "message": "Successfully subscribed to Pro Plan",
  "subscriptionId": 12345678,
  "state": "active",
  "customerMaxioId": 9876543,
  "planName": "Pro Plan",
  "pricePerMonth": 299.00,
  "nextBillingAt": "2026-10-07T00:00:00"
}
```

### My Subscriptions - `GET /api/my-subscriptions`
```json
{
  "success": true,
  "message": "Found 1 subscription(s)",
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "nextBillingAt": "2026-10-07T00:00:00",
      "mrrPerMonth": 299.00,
      "createdAt": "2026-09-07T...",
      "updatedAt": "2026-09-07T..."
    }
  ]
}
```

## How It Works

1. **Authentication**: User JWT token identifies the eShopOnWeb user
2. **Customer Lookup**: User ID looked up in Maxio by reference
3. **Customer Creation**: If not found, new customer created automatically (idempotent)
4. **Subscription**: Subscription created for the customer
5. **Response**: Subscription details returned with next billing date

**Key feature**: Multiple subscriptions per user are supported, and the integration is idempotent (safe to retry).

## Troubleshooting

### "Plan not found"
- Verify plan handles: `eshop-pro` (Pro) or `basic-plan` (Basic)
- Check Maxio site is `cp-exp-2`

### "Failed to create customer"
- Verify MAXIO_API_KEY is correct
- Verify MAXIO_SITE_SUBDOMAIN is `cp-exp-2`
- Check API key has create customer permission

### 401 Unauthorized
- Token may be expired; get a new one
- Ensure token is in header: `Authorization: Bearer <TOKEN>`

### HTTPS certificate error
```bash
dotnet dev-certs https --trust
```

## Full Documentation

- **Setup & Configuration**: See `MAXIO_SETUP.md`
- **Verification & Testing**: See `INTEGRATION_VERIFICATION.md`
- **Architecture & Details**: See `MAXIO_IMPLEMENTATION_SUMMARY.md`

## What's Included

### Code
- `src/ApplicationCore/MaxioSettings.cs` - Configuration model
- `src/Infrastructure/Services/MaxioApiService.cs` - Maxio API client
- `src/PublicApi/SubscriptionEndpoints/*` - API endpoints

### Documentation
- `MAXIO_SETUP.md` - Full setup guide
- `INTEGRATION_VERIFICATION.md` - Verification procedures
- `MAXIO_IMPLEMENTATION_SUMMARY.md` - Architecture overview

## Next Steps

1. ✓ Review the quick start (you're reading it!)
2. → Read `MAXIO_SETUP.md` for detailed configuration
3. → Follow `INTEGRATION_VERIFICATION.md` for comprehensive testing
4. → Read `MAXIO_IMPLEMENTATION_SUMMARY.md` for architecture details

## Security Notes

- ✓ No secrets in code or repository
- ✓ All credentials from environment variables
- ✓ HTTPS required (dev cert included)
- ✓ JWT authentication on all endpoints
- ✓ Each user can only see their own subscriptions

## Support

For issues:
1. Check `INTEGRATION_VERIFICATION.md` troubleshooting section
2. Verify environment variables are set correctly
3. Ensure Maxio credentials are valid
4. Check logs for detailed error messages
