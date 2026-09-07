# Quick Start: Maxio Subscription Integration

## 1. Prepare Environment Variables

Set these before running the application:

```bash
# Linux/Mac/PowerShell
export MAXIO_API_KEY="your-actual-api-key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-3"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
export DOTNET_ROLL_FORWARD="Major"
```

## 2. Build the Project

```bash
cd repo
dotnet build -c Release
```

Expected: Build succeeds with 0 errors

## 3. Run PublicApi

```bash
cd src/PublicApi
dotnet run
```

Expected output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:28543
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to stop, reopen the window, or press CTRL+Break.
```

## 4. Test the Integration (in separate terminal)

### Get Authentication Token

First, authenticate with the Web application (not PublicApi):

```bash
# Terminal 1: Start Web app
cd src/Web
dotnet run
# Navigate to https://localhost:5001 in browser, or use API endpoint

# Terminal 3: Get token via curl
curl -X POST https://localhost:5001/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  -k --silent | jq '.token'
```

### Call PublicApi Endpoints

With the token from above:

```bash
# Save token
TOKEN="your-token-from-above"

# 1. List plans (no auth required)
curl -X GET https://localhost:28543/api/subscription-plans \
  -H "Accept: application/json" -k --silent | jq '.'

# 2. Subscribe (requires auth)
curl -X POST https://localhost:28543/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"planHandle":"eshop-pro"}' \
  -k --silent | jq '.'

# 3. List user's subscriptions (requires auth)
curl -X GET https://localhost:28543/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -k --silent | jq '.'
```

## 5. Verify Responses

### List Plans Response
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "...",
      "price": 299.00,
      "priceFormatted": "$299.00",
      "intervalUnit": 1,
      "intervalUnitText": "month"
    }
  ]
}
```

### Create Subscription Response (201 Created)
```json
{
  "subscription": {
    "id": 123456,
    "customerId": 789,
    "state": "active",
    "productHandle": "eshop-pro",
    "productName": "Pro Plan",
    "nextAssessmentAt": "2026-10-07T...",
    "createdAt": "2026-09-07T...",
    "updatedAt": "2026-09-07T..."
  }
}
```

## 6. Verify in Maxio Dashboard

1. Log in to your Maxio sandbox at `https://cp-exp-3.chargify.com`
2. Navigate to **Customers**
3. Search for the username (email) you used to subscribe
4. Click on customer and verify:
   - Customer was created with your eShopOnWeb username as reference
   - Subscription appears with "Pro Plan" active status
   - Next billing date is 1 month from today

## Troubleshooting

| Issue | Solution |
|-------|----------|
| 401 Unauthorized on /my-subscriptions | Ensure Bearer token is valid and includes `Authorization: Bearer {TOKEN}` header |
| Maxio API errors | Verify `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and network connectivity |
| Build fails | Ensure `DOTNET_ROLL_FORWARD=Major` and .NET 10 SDK is installed |
| "Plan not found" | Verify `MAXIO_DEFAULT_PRODUCT_FAMILY` matches product family in Maxio |
| No database errors with in-memory DB | This is expected - in-memory data is lost on restart |

## Files to Review

- **Integration Logic**: `src/Infrastructure/Services/MaxioSubscriptionService.cs`
- **Endpoints**: `src/PublicApi/SubscriptionEndpoints/*.cs`
- **Configuration**: `src/Infrastructure/Dependencies.cs`
- **Full Guide**: `MAXIO_SUBSCRIPTION_INTEGRATION.md`

## What's Implemented

✅ Get subscription plans  
✅ Create subscriptions (with automatic customer creation)  
✅ List user subscriptions  
✅ JWT authentication  
✅ Idempotent operations  
✅ Error handling  
✅ Database migration  
✅ Configuration from environment  

## What's Included in Future Enhancements

Future additions could include:
- Upgrade/downgrade subscriptions
- Cancel subscriptions
- Webhook handlers for Maxio events
- Subscription management UI
- Usage-based billing (metered components)
- Retry logic for API failures
