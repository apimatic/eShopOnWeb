# Maxio Subscription Billing - Quick Start Guide

The Maxio Advanced Billing integration has been successfully implemented and is ready to use. This guide gets you up and running in 5 minutes.

## What's Implemented

✓ **Three new API endpoints** for subscription management:
- `GET /api/subscription-plans` - List available plans
- `POST /api/subscriptions` - Create a subscription
- `GET /api/my-subscriptions` - Get user's subscriptions

✓ **Maxio integration layer** with:
- Secure credential management via environment variables
- Idempotent customer creation
- Full subscription lifecycle support

✓ **Production-ready** with:
- JWT authentication on all endpoints
- Proper error handling
- No hardcoded secrets

## Requirements

- .NET 8 SDK (or .NET 10 with `DOTNET_ROLL_FORWARD=Major`)
- Maxio sandbox credentials (API Key and Site Subdomain)
- Windows/Linux/Mac environment

## 5-Minute Setup

### 1. Get Maxio Credentials (1 min)

You need two pieces of information from your Maxio sandbox:
- **API Key** - Available in Maxio dashboard settings
- **Site Subdomain** - Usually `cp-exp-2` for sandbox environments

If you don't have these, contact your Maxio administrator.

### 2. Set Environment Variables (1 min)

**Windows PowerShell:**
```powershell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

**Windows Command Prompt:**
```cmd
set MAXIO_API_KEY=your-api-key
set MAXIO_SITE_SUBDOMAIN=cp-exp-2
set UseOnlyInMemoryDatabase=true
set DOTNET_ROLL_FORWARD=Major
```

### 3. Start the Application (1 min)

```bash
cd src/PublicApi
dotnet run
```

Wait for:
```
Application started. Press Ctrl+C to exit.
Now listening on: https://localhost:25083
```

### 4. Test with Swagger (1 min)

Open your browser to: `https://localhost:25083/swagger`

You should see the new endpoints in the "SubscriptionEndpoints" section.

### 5. Make Your First API Call (1 min)

First, get an auth token:

**PowerShell:**
```powershell
$auth = @{
    username = "demouser@microsoft.com"
    password = "Pass@word123"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:25083/api/authenticate" `
    -Method Post `
    -ContentType "application/json" `
    -Body $auth `
    -SkipCertificateCheck

$token = ($response.Content | ConvertFrom-Json).token
Write-Host "Token: $token"
```

Then get subscription plans:

```powershell
$headers = @{ Authorization = "Bearer $token" }
$plans = Invoke-WebRequest -Uri "https://localhost:25083/api/subscription-plans" `
    -Method Get `
    -Headers $headers `
    -SkipCertificateCheck

$plans.Content | ConvertFrom-Json | ConvertTo-Json
```

**Success!** You now have working subscription endpoints.

## Next Steps

### For Testing
See detailed testing instructions in `TEST_MAXIO_INTEGRATION.md`

### For Understanding the Architecture
Read `MAXIO_INTEGRATION_SUMMARY.md` for:
- Complete system design
- How the endpoints work
- Production considerations

### For Verification
Follow the comprehensive verification checklist in `VERIFICATION_STEPS.md`

## API Endpoints Reference

### Get Available Plans
```
GET /api/subscription-plans
Authorization: Bearer <token>
```

Returns list of subscription plans available in your Maxio sandbox.

### Create a Subscription
```
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

Creates a subscription for the authenticated user to the specified plan.

### Get User's Subscriptions
```
GET /api/my-subscriptions
Authorization: Bearer <token>
```

Returns all active subscriptions for the authenticated user.

## Sandbox Plans Available

| Plan | Handle | Price | Billing |
|------|--------|-------|---------|
| Pro Plan | `eshop-pro` | $299/month | Monthly recurring |
| Basic Plan | `basic-plan` | $29/month | Monthly recurring |

**Note:** No payment method required for sandbox testing.

## Troubleshooting

### Build fails
```bash
# Make sure you're using the right .NET version
dotnet --version
# Should show 8.x.x or higher

# Force forward compatibility if needed
set DOTNET_ROLL_FORWARD=Major
```

### "Unauthorized" error from Maxio
```
Check:
1. MAXIO_API_KEY is correct
2. API key has permission to create subscriptions
3. Sandbox is active
```

### Connection refused on port 25083
```bash
# Verify PublicApi is running
# Check firewall isn't blocking localhost
# Ensure no other app uses this port
```

### Token expired
```
Just re-run the authenticate endpoint to get a fresh token
```

## File Structure

```
Added Files:
- src/ApplicationCore/Services/MaxioConfiguration.cs
- src/ApplicationCore/Services/MaxioApiClient.cs
- src/PublicApi/SubscriptionEndpoints/SubscriptionPlansEndpoint.cs
- src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs
- src/PublicApi/SubscriptionEndpoints/MySubscriptionsEndpoint.cs

Modified Files:
- src/PublicApi/Program.cs (added Maxio configuration)
- src/PublicApi/appsettings.json (added Maxio config section)
- src/PublicApi/Properties/launchSettings.json (added env vars)

Documentation:
- MAXIO_QUICKSTART.md (this file)
- MAXIO_INTEGRATION_SUMMARY.md (architecture & design)
- TEST_MAXIO_INTEGRATION.md (detailed testing)
- VERIFICATION_STEPS.md (verification checklist)
```

## Security Notes

✓ **No secrets in code** - All credentials loaded from environment variables
✓ **JWT authentication** - All endpoints require valid bearer token
✓ **Idempotent operations** - Creating multiple subscriptions for same user is safe
✓ **Error messages** - Safe error responses without exposing internals

## Key Features

1. **Idempotent Customer Creation**
   - Uses user ID as customer reference
   - Calling multiple times creates only one customer
   - Safe for retries and re-execution

2. **Multiple Subscriptions Per User**
   - Users can subscribe to multiple plans
   - All subscriptions tied to their user account
   - `/api/my-subscriptions` lists all

3. **Real-time Maxio Sync**
   - Subscription data always fresh from Maxio
   - No database sync needed
   - Changes in Maxio immediately visible in API

4. **Production-Ready Error Handling**
   - Proper HTTP status codes (200, 201, 400, 401)
   - Meaningful error messages
   - Request correlation IDs for tracking

## What's NOT Included (Future Work)

The following features can be added in future versions:

- Subscription management (cancel, pause, upgrade, downgrade)
- Webhook handlers for Maxio events
- Billing portal integration
- Invoice tracking and retrieval
- Payment/retry management
- Discount and coupon support
- Tax configuration
- Metered billing for usage-based pricing

## Getting Help

**API Documentation:** Visit `https://localhost:25083/swagger` when app is running

**Code Examples:** See `TEST_MAXIO_INTEGRATION.md` for curl and PowerShell examples

**Architecture Details:** Read `MAXIO_INTEGRATION_SUMMARY.md`

**Verification:** Follow `VERIFICATION_STEPS.md` to test each component

## Support Resources

- Maxio API Documentation: https://developers.maxio.com
- eShopOnWeb GitHub: https://github.com/dotnet-architecture/eShopOnWeb
- .NET Documentation: https://docs.microsoft.com/dotnet

---

**Status:** ✓ Ready for Production Testing

The integration builds successfully, follows all eShopOnWeb patterns, includes no hardcoded secrets, and is documented for easy verification and maintenance.
