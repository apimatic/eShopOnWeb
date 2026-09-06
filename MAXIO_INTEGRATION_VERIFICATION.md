# Maxio Subscription Billing Integration - Verification Guide

This guide walks through verifying that the Maxio subscription billing feature is working correctly.

## Prerequisites

- .NET 8.0 SDK (or use `dotnet dev-certs https --check` to trust dev cert if not already trusted)
- Maxio sandbox credentials:
  - API Key (from Maxio account settings)
  - Site Subdomain: `cp-exp-2`
  - Product Family Handle: `eshop-subscribe` (already seeded)

## Step 1: Configure Credentials

Set the Maxio credentials as environment variables before running the application:

```powershell
$env:MAXIO_API_KEY = "your-api-key-here"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
```

**IMPORTANT:** Never commit these values to the repository. Use user-secrets instead for local development:

```bash
dotnet user-secrets init --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ApiKey" "your-api-key-here" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe" --project src/PublicApi/PublicApi.csproj
```

## Step 2: Build the Project

```bash
cd C:\path\to\repo
dotnet build src/PublicApi/PublicApi.csproj
```

Expected output: **Build succeeded** (may have warnings about System.Text.Json vulnerabilities - these are from existing code)

## Step 3: Run the PublicApi Server

```bash
cd C:\path\to\repo
dotnet run --project src/PublicApi/PublicApi.csproj
```

Expected output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:27623
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

The Swagger UI will be available at: `https://localhost:27623/swagger/ui/index.html`

## Step 4: Create a Test User and Get JWT Token

Use curl or Postman to authenticate:

**Request:**
```bash
curl -X POST "https://localhost:27623/api/authenticate" `
  -H "Content-Type: application/json" `
  -d @- << 'EOF'
{
  "username": "demouser@microsoft.com",
  "password": "Pass@123"
}
EOF
```

**Expected Response:**
```json
{
  "result": true,
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "username": "demouser@microsoft.com",
  "token": "eyJhbGciOiJIUzI1NiIs..."
}
```

Save the `token` value - you'll use it for the next requests.

## Step 5: List Available Subscription Plans

**Request:**
```bash
curl -X GET "https://localhost:27623/api/subscription-plans" `
  -H "Authorization: Bearer YOUR_TOKEN_HERE" `
  -H "Content-Type: application/json"
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month",
      "description": "Professional subscription plan"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price": 29.00,
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month",
      "description": "Basic subscription plan"
    }
  ]
}
```

## Step 6: Create a Subscription

**Request:**
```bash
curl -X POST "https://localhost:27623/api/subscriptions" `
  -H "Authorization: Bearer YOUR_TOKEN_HERE" `
  -H "Content-Type: application/json" `
  -d @- << 'EOF'
{
  "productHandle": "eshop-pro"
}
EOF
```

**Expected Response:**
```json
{
  "subscription": {
    "id": 12345678,
    "productHandle": "eshop-pro",
    "productName": "Pro Plan",
    "price": 299.00,
    "state": "active",
    "nextBillingAt": "2026-10-07T00:00:00",
    "createdAt": "2026-09-07T12:34:56"
  },
  "message": "Subscription created successfully"
}
```

**What's happening:**
1. User is extracted from JWT token claims
2. Maxio customer is looked up by email reference (or created if not found)
3. Subscription is created on Maxio for that customer + product
4. Subscription mapping is stored in local database

## Step 7: List User's Subscriptions

**Request:**
```bash
curl -X GET "https://localhost:27623/api/my-subscriptions" `
  -H "Authorization: Bearer YOUR_TOKEN_HERE" `
  -H "Content-Type: application/json"
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "price": 299.00,
      "state": "active",
      "nextBillingAt": "2026-10-07T00:00:00",
      "createdAt": "2026-09-07T12:34:56"
    }
  ]
}
```

## Step 8: Verify Idempotency

Try creating another subscription with the same product for the same user:

```bash
curl -X POST "https://localhost:27623/api/subscriptions" `
  -H "Authorization: Bearer YOUR_TOKEN_HERE" `
  -H "Content-Type: application/json" `
  -d @- << 'EOF'
{
  "productHandle": "eshop-pro"
}
EOF
```

**Expected:** Maxio will create a new subscription (not an error - that's the expected behavior for multiple subscriptions on same plan). However, **double-clicking the same create button should not create duplicate customers** — the customer lookup/creation is idempotent via the email reference.

## Step 9: Verify in Maxio Dashboard

1. Go to `https://cp-exp-2.chargify.com` (Maxio sandbox)
2. Log in with your Maxio account
3. Navigate to **Customers** section
4. Search for the user's email (`demouser@microsoft.com`)
5. Verify:
   - Customer record exists with reference = user ID
   - Subscription is linked to the customer
   - Plan shows correct pricing ($299.00/mo for Pro)
   - State is "active"
   - Next billing date is ~30 days from creation

## Troubleshooting

### 401 Unauthorized on subscription endpoints
- Verify the JWT token is valid (check expiry in `/api/authenticate` response)
- Ensure "Authorization: Bearer" header is present with correct token

### 400 Bad Request when creating subscription
- Check that `productHandle` is spelled correctly ("eshop-pro", not "eshop-pro-plan")
- Verify the Maxio credentials are set correctly

### Connection refused to Maxio API
- Verify `MAXIO_SITE_SUBDOMAIN` is "cp-exp-2"
- Verify internet connectivity to `https://cp-exp-2.chargify.com`
- Check that `MAXIO_API_KEY` is not empty

### In-memory database losing data on restart
- Expected behavior with `UseOnlyInMemoryDatabase=true`
- User-subscription mappings persist only during a single run
- Restart the application to clear the in-memory database

## Architecture Summary

```
User (with JWT token)
    ↓
PublicApi Endpoints (HTTP Basic Auth to Maxio)
    ↓
Maxio API (cp-exp-2.chargify.com)
    
Local Database (tracks user ↔ Maxio customer/subscription mapping)
```

## Testing Scenarios

### Scenario A: New User Subscribe to Pro Plan
1. User authenticates
2. Calls `GET /api/subscription-plans` → sees Pro ($299) and Basic ($29)
3. Calls `POST /api/subscriptions` with `{ productHandle: "eshop-pro" }`
4. Calls `GET /api/my-subscriptions` → confirms subscription is active, next billing 30 days out
5. Verifies in Maxio dashboard

### Scenario B: User Subscribes to Multiple Plans
1. User creates Pro subscription (from Scenario A)
2. User creates Basic subscription with `{ productHandle: "basic-plan" }`
3. Calls `GET /api/my-subscriptions` → returns list with both subscriptions
4. Verifies in Maxio both subscriptions show for the same customer

### Scenario C: Idempotent Customer Creation
1. User A creates subscription → customer record created in Maxio
2. User A creates another subscription → same customer record used (lookup by email)
3. Verifies in Maxio there is only one customer for User A (no duplicates)

## Production Considerations

- **Secrets management:** Store Maxio API key in Azure Key Vault or secure secrets manager
- **Error handling:** Consider retry logic with exponential backoff for Maxio API calls
- **Webhooks:** Subscribe to Maxio subscription events (state changes, billing failures) for production usage
- **Logging:** All Maxio API calls are logged - monitor for quota/rate limit issues
- **Database:** Switch from in-memory to SQL Server in production with proper migration management
- **Payment method:** Update plans to require payment method if charging is needed (currently set to not require)
- **Dunning:** Configure Maxio dunning rules for failed payment recovery

## Next Steps

- [x] Core integration complete and verified
- [ ] Add webhook handlers for subscription state changes
- [ ] Add cancellation/pause endpoints
- [ ] Add usage metering for `api-call` component
- [ ] Integrate billing dashboard into web UI
- [ ] Add subscription management portal
