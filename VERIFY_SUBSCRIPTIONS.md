# Maxio Subscription Integration - Verification Guide

This guide provides step-by-step instructions to verify the Maxio subscription billing integration works correctly.

## Prerequisites

- .NET SDK with `rollForward: latestMajor` configured (see `global.json`)
- Maxio sandbox credentials:
  - API Key
  - Site Subdomain
  - Product Family Handle (default: `eshop-subscribe`)
- A test plan handle in Maxio (default: `eshop-pro` or `basic-plan`)

## Step 1: Set Environment Variables

Open PowerShell and set the Maxio sandbox credentials:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
$env:MAXIO_API_KEY = "YOUR_MAXIO_API_KEY"
$env:MAXIO_SITE_SUBDOMAIN = "YOUR_SITE_SUBDOMAIN"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

**Important:** Do NOT commit these values to git. They are stored in environment variables only.

## Step 2: Build the Solution

```powershell
cd C:\claude-runs\t1h45ali-openapi-haiku45high-023\repo
dotnet build Everything.sln -c Debug
```

Expected output: `Build succeeded.` with 0 errors

## Step 3: Run PublicApi Service

In a PowerShell terminal, start the PublicApi:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
$env:MAXIO_API_KEY = "YOUR_MAXIO_API_KEY"
$env:MAXIO_SITE_SUBDOMAIN = "YOUR_SITE_SUBDOMAIN"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"

cd C:\claude-runs\t1h45ali-openapi-haiku45high-023\repo
dotnet run --project src/PublicApi/PublicApi.csproj
```

Wait for startup message: "LAUNCHING PublicApi"

The service will be available at: `https://localhost:27483`

## Step 4: Run Integration Tests

In a new PowerShell terminal (keep PublicApi running):

```powershell
cd C:\claude-runs\t1h45ali-openapi-haiku45high-023\repo
.\test-subscriptions.ps1 -BaseUrl "https://localhost:27483"
```

### Expected Test Flow:

1. **Get Subscription Plans** ✓
   - Lists available plans from Maxio
   - Should show plans like `eshop-pro` ($299/mo) and `basic-plan` ($29/mo)

2. **Authenticate** ✓
   - Gets JWT token for user `demouser@microsoft.com`
   - Returns token with user details

3. **List Subscriptions (Before)** ✓
   - Should be empty (user has no subscriptions yet)
   - Returns empty array with `success: true`

4. **Create Subscription** ✓
   - Creates new subscription for authenticated user
   - Returns subscription details with:
     - `subscriptionId` (numeric ID from Maxio)
     - `state: "active"`
     - `planName` and `priceInCents`
     - `nextBillingDate` (calculated by Maxio)

5. **List Subscriptions (After)** ✓
   - Now shows the newly created subscription
   - Includes billing cycle dates and next billing date

### Example Successful Response:

```json
{
  "correlationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "success": true,
  "subscriptionId": 12345678,
  "state": "active",
  "planName": "$299 Pro Plan",
  "priceInCents": 29900,
  "nextBillingDate": "2026-10-07T00:00:00Z"
}
```

## Step 5: Verify API Endpoints via Swagger

1. Navigate to: `https://localhost:27483/swagger`
2. Authorize with the JWT token from Step 4
3. Test each endpoint:
   - `GET /api/subscription-plans` - No auth required
   - `POST /api/subscriptions` - Requires auth
   - `GET /api/my-subscriptions` - Requires auth

## Step 6: Manual cURL Tests (Optional)

### List Plans (No Auth)
```powershell
curl -X GET https://localhost:27483/api/subscription-plans `
  -Headers @{ "Accept" = "application/json" } `
  -SkipCertificateCheck
```

### Authenticate
```powershell
$body = @{
    username = "demouser@microsoft.com"
    password = "Pass@word1"
} | ConvertTo-Json

$response = curl -X POST https://localhost:27483/api/authenticate `
  -ContentType "application/json" `
  -Body $body `
  -SkipCertificateCheck

$token = $response.token
```

### Create Subscription (With Token)
```powershell
$body = @{
    productHandle = "eshop-pro"
} | ConvertTo-Json

curl -X POST https://localhost:27483/api/subscriptions `
  -ContentType "application/json" `
  -Body $body `
  -Headers @{ "Authorization" = "Bearer $token" } `
  -SkipCertificateCheck
```

## Troubleshooting

### Issue: "Maxio API error: 401"
- Verify `MAXIO_API_KEY` is correct
- Verify `MAXIO_SITE_SUBDOMAIN` is correct
- Check credentials are for sandbox environment

### Issue: "Maxio API error: 404"
- Verify product handle exists in your Maxio site
- Check `MAXIO_DEFAULT_PRODUCT_FAMILY` is correct
- Verify plans are published (not draft)

### Issue: "Failed to create Maxio customer"
- Ensure API key has permissions to create customers and subscriptions
- Check Maxio site settings for subscription restrictions

### Issue: "User not authenticated"
- Ensure JWT token is included in `Authorization: Bearer` header
- Token format should be: `Bearer eyJhbG...`

### Issue: BuildFailed - "SDK version mismatch"
- Ensure `DOTNET_ROLL_FORWARD=Major` is set
- Run: `dotnet --version` to verify .NET 10 SDK is installed
- Check `global.json` has `rollForward: latestMajor`

## Verification Checklist

- [ ] Solution builds without errors
- [ ] PublicApi service starts successfully
- [ ] Can list subscription plans
- [ ] Can authenticate and get JWT token
- [ ] Can create subscription (customer is created in Maxio)
- [ ] Can list user subscriptions after creation
- [ ] Subscription state shows "active"
- [ ] Next billing date is calculated correctly
- [ ] All API responses include `success: true/false` flag
- [ ] Endpoints return appropriate HTTP status codes

## Implementation Details

### Files Created
- `src/ApplicationCore/Services/MaxioSettings.cs` - Configuration
- `src/ApplicationCore/Services/MaxioClient.cs` - HTTP client with Basic auth
- `src/ApplicationCore/Services/MaxioDtos.cs` - Request/response models
- `src/ApplicationCore/Services/SubscriptionService.cs` - Business logic
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/MySubscriptionsEndpoint.cs`

### Files Modified
- `src/PublicApi/Program.cs` - Maxio service registration
- `src/PublicApi/appsettings.json` - Maxio configuration section
- `global.json` - SDK rollForward setting

### Key Features
- ✓ JWT authentication on subscription endpoints
- ✓ Idempotent customer creation (safe to retry)
- ✓ Basic auth with Maxio (API key:x)
- ✓ Error logging and resilience
- ✓ Automatic customer reference generation
- ✓ Support for dynamic Maxio endpoints
- ✓ In-memory database support for development

## Next Steps

1. **Deploy to Production:**
   - Set environment variables on deployment server
   - Use production Maxio credentials
   - Consider implementing subscription persistence in database

2. **Additional Endpoints:**
   - Add cancel subscription endpoint
   - Add webhook handling for lifecycle events
   - Add subscription modification (plan changes)

3. **Frontend Integration:**
   - Create subscription selection UI
   - Add subscription management dashboard
   - Handle payment method capture if needed

## Support

For issues related to:
- **Maxio API**: Consult `maxio-spec/openapi.yaml` (authoritative contract)
- **eShopOnWeb**: Check existing endpoint patterns in PublicApi
- **JWT Auth**: Review `src/PublicApi/AuthEndpoints/AuthenticateEndpoint.cs`
