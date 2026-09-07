# Maxio Subscription Integration - Verification Steps

## Quick Start Verification

### Step 1: Verify Build
```powershell
cd C:\path\to\eShopOnWeb
dotnet build eShopOnWeb.sln
# Should complete with 0 errors
```

### Step 2: Configure Maxio Credentials
Replace with your actual Maxio sandbox credentials:

```powershell
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "your-actual-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-sandbox-subdomain"
dotnet user-secrets list
```

Expected output:
```
Maxio:Subdomain = your-sandbox-subdomain
Maxio:ProductFamilyHandle = eshop-subscribe
Maxio:ApiKey = your-actual-api-key
```

### Step 3: Run the PublicApi
```powershell
dotnet run --project src/PublicApi/PublicApi.csproj
```

Expected output (after ~5 seconds):
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:28983
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:28984
```

### Step 4: Access Swagger Documentation
Open browser to: `https://localhost:28983/swagger`

You should see three new subscription endpoints:
- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

### Step 5: Get Authentication Token
Open Swagger UI and expand the "POST /api/authenticate" endpoint.

Click "Try it out" and use:
```json
{
  "username": "demouser@microsoft.com",
  "password": "Pass@word1"
}
```

Copy the returned `token` value (looks like: `eyJ0eXAiOiJKV1QiLCJh...`).

### Step 6: Test Subscription Plans Endpoint
1. Expand `GET /api/subscription-plans`
2. Click "Authorize" button
3. Paste your token: `Bearer YOUR_TOKEN_HERE`
4. Click "Try it out" → "Execute"

Expected response (200 OK):
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "...",
      "priceInCents": 29900,
      "price": 299,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "...",
      "priceInCents": 2900,
      "price": 29,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "success": true,
  "message": null
}
```

If you get an error, verify:
- Maxio API key is correct
- Maxio subdomain is correct
- Product family "eshop-subscribe" exists in Maxio
- Product handles "eshop-pro" and "basic-plan" exist in the family

### Step 7: Test Create Subscription
1. Expand `POST /api/subscriptions`
2. Click "Authorize" with your token
3. Click "Try it out"
4. Enter request body:
```json
{
  "planHandle": "eshop-pro"
}
```
5. Click "Execute"

Expected response (201 Created):
```json
{
  "success": true,
  "subscriptionId": 123456,
  "customerId": 98765,
  "state": "active",
  "createdAt": "2026-09-07T15:30:45.123Z",
  "nextBillingAt": "2026-10-07T15:30:45.123Z",
  "planName": "Pro Plan",
  "priceInCents": 29900,
  "price": 299
}
```

Verify in Maxio dashboard:
- New customer created with reference `eshop-demouser@microsoft.com` (or similar)
- New subscription linked to that customer
- Subscription in "active" state
- Next billing date is ~1 month away

### Step 8: Test Get My Subscriptions
1. Expand `GET /api/my-subscriptions`
2. Click "Authorize" with your token
3. Click "Try it out" → "Execute"

Expected response (200 OK):
```json
{
  "success": true,
  "subscriptions": [
    {
      "subscriptionId": 123456,
      "customerId": 98765,
      "planName": "Pro Plan",
      "planHandle": "eshop-pro",
      "state": "active",
      "priceInCents": 29900,
      "price": 299,
      "createdAt": "2026-09-07T15:30:45.123Z",
      "updatedAt": "2026-09-07T15:30:45.123Z",
      "nextBillingAt": "2026-10-07T15:30:45.123Z"
    }
  ]
}
```

## Testing Without Authentication

To test without going through Swagger, use curl:

```bash
# 1. Get token
TOKEN=$(curl -s -X POST https://localhost:28984/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  -k | jq -r '.token')

# 2. Get plans
curl -X GET https://localhost:28983/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  -k

# 3. Create subscription
curl -X POST https://localhost:28983/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k

# 4. Get my subscriptions
curl -X GET https://localhost:28983/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -k
```

## Troubleshooting

### Error: "Maxio API credentials not configured"
- Check secrets: `dotnet user-secrets list --project src/PublicApi/PublicApi.csproj`
- Verify they match your actual Maxio sandbox account credentials

### Error: "401 Unauthorized" when calling endpoints
- Your token may be expired (valid for ~24 hours)
- Get a fresh token from the authenticate endpoint

### Error: "404 Not Found" on plans endpoint
- Product family handle may be wrong (default: "eshop-subscribe")
- Verify the family exists in your Maxio sandbox

### Error: "Failed to create subscription"
- Plan handle may not exist in the product family
- Check Maxio UI for correct handles
- Verify plan has no required payment method (should be optional)

### Empty subscriptions list
- Customer may not have been created successfully
- Check Maxio Customers page for reference like "eshop-{userId}"
- Create a subscription first, then check the list

## Integration Files

- **Maxio Service**: `src/Infrastructure/Services/Maxio/`
- **Endpoints**: `src/PublicApi/SubscriptionEndpoints/`
- **Configuration**: `src/PublicApi/appsettings.json` + user-secrets
- **Documentation**: `SUBSCRIPTION_INTEGRATION_GUIDE.md`

## Next Steps After Verification

1. Test with different users (create multiple subscriptions)
2. Test subscription state transitions in Maxio UI
3. Verify next billing dates are correct
4. Add subscription management endpoints (cancel, upgrade)
5. Integrate subscription UI into web storefront
6. Set up Maxio webhooks for real-time updates
7. Add metered usage tracking if needed

