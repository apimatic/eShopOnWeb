# Subscription Integration Quick Start

Get the Maxio subscription billing integration working in 5 minutes.

## Prerequisites

- .NET 8.0+ (or .NET 10 with rollforward enabled)
- Maxio Advanced Billing sandbox account
- curl or Postman (for testing)

## Step 1: Configure Maxio Credentials (2 min)

Get your credentials from Maxio console:

1. Log into your Maxio sandbox account
2. Go to **Config → Integrations → API Keys**
3. Copy your **API Key** and **Subdomain**
4. Get your **Product Family Handle** from your product family settings

Then set up user-secrets:

```bash
cd src/PublicApi

# Set the three required secrets
dotnet user-secrets set "Maxio:ApiKey" "your-api-key-here"
dotnet user-secrets set "Maxio:Subdomain" "your-subdomain-here"  
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Verify they were set
dotnet user-secrets list
```

## Step 2: Run the Application (1 min)

```bash
cd src/PublicApi
dotnet run
```

Wait for the output:
```
LAUNCHING PublicApi
```

Then open: `https://localhost:25043/swagger`

## Step 3: Authenticate (1 min)

In Swagger UI:

1. Click the **Authorize** button (top right)
2. Go to the **AuthenticateEndpoint** section
3. Click **Try it out**
4. Use default credentials:
   - Username: `user@example.com`
   - Password: `Pass@word1`
5. Copy the **token** from the response

Then:

1. Click the **Authorize** button again
2. In the Authorization field, paste: `Bearer <token>`
3. Click **Authorize** then **Close**

## Step 4: Test the Endpoints (1 min)

### 1. List Available Plans

```
GET /api/subscription-plans
```

You'll see plans like:
- `eshop-pro` - Pro Plan ($299/month)
- `basic-plan` - Basic Plan ($29/month)

### 2. Create a Subscription

```
POST /api/subscriptions
```

Request body:
```json
{
  "productHandle": "eshop-pro"
}
```

Response will show the subscription ID and details.

### 3. View Your Subscriptions

```
GET /api/my-subscriptions
```

You should see the subscription you just created.

## Verify in Maxio

1. Log into your Maxio sandbox
2. Go to **Customers**
3. You should see a new customer with:
   - Your email
   - Reference = your user ID (from eShopOnWeb)
4. Click the customer
5. Under **Subscriptions**, you should see the active subscription

## Troubleshooting

### Error: "Invalid API Key"
- Check you copied the key correctly
- Ensure you're using the right Maxio environment (sandbox)
- Verify the secret was set: `dotnet user-secrets list`

### Error: "Product handle not found"
- Check the product family handle is correct
- Verify the product exists in your product family
- Ensure it's not archived

### Error: 401 Unauthorized
- Make sure you included the Bearer token
- Token format should be: `Bearer eyJhbGc...`
- Tokens expire after 7 days

### Application won't start
- Check if port 25043 is already in use
- If .NET 10 but need .NET 8: `export DOTNET_ROLL_FORWARD=Major`
- Check logs for detailed error messages

## Next Steps

- Read [SUBSCRIPTION_INTEGRATION.md](./SUBSCRIPTION_INTEGRATION.md) for full documentation
- Review [SUBSCRIPTION_VERIFICATION_CHECKLIST.md](./SUBSCRIPTION_VERIFICATION_CHECKLIST.md) for thorough testing
- Check the integration by examining the code:
  - Endpoints: `src/PublicApi/SubscriptionEndpoints/`
  - Services: `src/PublicApi/MaxioIntegration/`

## Key Concepts

| Term | Meaning |
|------|---------|
| **Product Family** | A grouping of related plans (e.g., "eshop-subscribe") |
| **Product Handle** | Unique identifier for a plan (e.g., "eshop-pro") |
| **Subscription** | Active plan for a user |
| **Maxio Customer** | Account in Maxio linked to eShopOnWeb user |
| **Bearer Token** | JWT authentication token for API calls |

## Example API Calls

### List Plans (no auth needed)
```bash
curl https://localhost:25043/api/subscription-plans
```

### Create Subscription (needs token)
```bash
curl -X POST https://localhost:25043/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"productHandle": "eshop-pro"}'
```

### Get My Subscriptions (needs token)
```bash
curl -H "Authorization: Bearer $TOKEN" \
  https://localhost:25043/api/my-subscriptions
```

## Common Workflows

### User Signs Up and Subscribes
1. User registers account
2. User browses plans (GET /api/subscription-plans)
3. User selects plan and subscribes (POST /api/subscriptions)
4. System creates Maxio customer and subscription
5. Success page shows subscription details

### View Subscription History
1. Logged-in user navigates to "My Subscriptions"
2. Call GET /api/my-subscriptions
3. Display list with current plans and next billing dates

### Enable Billing Portal
1. Future: Create a custom billing portal using the subscription data
2. Show invoices, update payment methods, cancel subscriptions
3. Use Maxio's hosted portal or build custom UI

## Useful Links

- 📖 [Full Documentation](./SUBSCRIPTION_INTEGRATION.md)
- ✅ [Verification Checklist](./SUBSCRIPTION_VERIFICATION_CHECKLIST.md)
- 🔌 [Maxio API Docs](https://developers.maxio.com/)
- 💬 [Maxio Support](https://support.maxio.com/)
- 📦 [eShopOnWeb Repo](https://github.com/dotnet-architecture/eShopOnWeb)
