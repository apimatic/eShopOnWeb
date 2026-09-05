# Quick Verification Guide

## 1. Confirm Build Succeeds

```bash
cd C:\claude-runs\t1h45ali-maxio-docs-mcp-haiku45high-005\repo
DOTNET_ROLL_FORWARD=Major dotnet build src/PublicApi/PublicApi.csproj
```

✅ Expected: "Build succeeded."

## 2. Start PublicApi with Credentials

Set environment variables with your Maxio sandbox credentials, then run:

```bash
$env:MAXIO_API_KEY = "<your-api-key>"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"  # or your site
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"

cd C:\claude-runs\t1h45ali-maxio-docs-mcp-haiku45high-005\repo
dotnet run --project src/PublicApi
```

✅ Expected: Application starts, shows "LAUNCHING PublicApi"

Note the HTTPS port from the startup messages (e.g., `https://localhost:5001`)

## 3. Test: Get Subscription Plans (Public Endpoint)

In another terminal, test the public endpoint:

```bash
curl -X GET https://localhost:5001/api/subscription-plans `
  -H "Content-Type: application/json" `
  -SkipCertificateCheck
```

✅ Expected: JSON response with plans list including `eshop-pro` and `basic-plan`

```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "pricePerMonth": 299.00,
      "description": "..."
    },
    ...
  ]
}
```

## 4. Test: Authenticate

Get a JWT token (using default seed user):

```bash
$token = curl -X POST https://localhost:5001/api/authenticate `
  -H "Content-Type: application/json" `
  -Body '{"username":"demouser@microsoft.com","password":"Pass@word123"}' `
  -SkipCertificateCheck | ConvertFrom-Json

$token.token
```

✅ Expected: Long JWT string starting with `eyJ`

## 5. Test: Create Subscription (Authenticated)

```bash
$bearerToken = $token.token

curl -X POST https://localhost:5001/api/subscriptions `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $bearerToken" `
  -Body '{"planHandle":"eshop-pro"}' `
  -SkipCertificateCheck
```

✅ Expected: Subscription created in Maxio, response shows:
- `"subscriptionId"`: unique Maxio subscription ID
- `"state": "active"`
- `"productName": "Pro Plan"`
- `"pricePerMonth": 299.0`
- `"nextBillingDate"`: ISO date 1 month from now

## 6. Test: Get My Subscriptions (Authenticated)

```bash
curl -X GET https://localhost:5001/api/my-subscriptions `
  -H "Authorization: Bearer $bearerToken" `
  -SkipCertificateCheck
```

✅ Expected: Array containing the subscription just created with all details

## 7. Verify Idempotency

Run the create subscription test again (Step 5) with the same token:

```bash
curl -X POST https://localhost:5001/api/subscriptions `
  -H "Content-Type: application/json" `
  -H "Authorization: Bearer $bearerToken" `
  -Body '{"planHandle":"eshop-pro"}' `
  -SkipCertificateCheck
```

✅ Expected: Same subscription ID returns (no duplicate created in Maxio)
   - Idempotent behavior confirmed ✓

## Summary

If all tests pass, the integration is **working correctly**:

- ✅ Plans endpoint returns sandbox product family
- ✅ Authentication integrates with existing JWT system
- ✅ Create subscription endpoint works with Maxio API
- ✅ Subscriptions persist in Maxio and can be retrieved
- ✅ Idempotency prevents duplicate subscriptions
- ✅ JWT authentication properly protects endpoints

## Documentation Files

For detailed information, see:

- **IMPLEMENTATION_SUMMARY.md** - Architecture, design decisions, future work
- **MAXIO_SUBSCRIPTION_INTEGRATION.md** - Full API documentation, troubleshooting, production setup

## Code Locations

| Component | Location |
|-----------|----------|
| Configuration | `src/Infrastructure/MaxioConfiguration.cs` |
| HTTP Client | `src/Infrastructure/Services/MaxioHttpClient.cs` |
| Business Logic | `src/Infrastructure/Services/MaxioSubscriptionService.cs` |
| Service Interface | `src/ApplicationCore/Interfaces/IMaxioSubscriptionService.cs` |
| Endpoints | `src/PublicApi/SubscriptionEndpoints/*.cs` |
| DI Setup | `src/Infrastructure/Dependencies.cs` |
| App Configuration | `src/PublicApi/Program.cs` |
