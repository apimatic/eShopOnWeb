# Maxio Subscription Billing Integration — Verification Guide

This guide walks through setting up and testing the Maxio Advanced Billing integration with eShopOnWeb.

## Setup

### 1. Configure Maxio Credentials

Set the following environment variables using user-secrets or your deployment environment:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "YOUR_MAXIO_PRODUCT_FAMILY_HANDLE"
```

**For sandbox testing with the seeded `cp-exp-4` site:**
- Product Family Handle: `eshop-subscribe`
- Pro Plan Handle: `eshop-pro`
- Basic Plan Handle: `basic-plan`
- API Key: Available from your Maxio sandbox account

Alternatively, set environment variables:
```powershell
$env:MAXIO_API_KEY = "YOUR_API_KEY"
$env:MAXIO_SITE_SUBDOMAIN = "YOUR_SUBDOMAIN"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

### 2. Database Setup

The integration supports two database configurations:

**Option A: In-Memory Database (for testing)**
```powershell
$env:UseOnlyInMemoryDatabase = "true"
```

**Option B: SQL Server LocalDB (persisted)**
- Requires: SQL Server Express with LocalDB
- Connection strings are in `appsettings.json`

### 3. Build and Run

```bash
# Build the solution
dotnet build eShopOnWeb.sln

# Run PublicApi (subscription endpoints available here)
cd src/PublicApi
dotnet run

# In another terminal, run the Web project for authentication (optional)
cd src/Web
dotnet run
```

**Application URLs:**
- PublicApi: `https://localhost:24723` (subscription endpoints)
- Web: `https://localhost:5001` (storefront, for authentication)

## Testing the Integration

### Step 1: Get Subscription Plans (No Auth Required)

```bash
curl -X GET "https://localhost:24723/api/subscription-plans" \
  -H "Accept: application/json"
```

**Expected Response:**
```json
{
  "success": true,
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00,
      "interval": "month",
      "intervalCount": 1
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00,
      "interval": "month",
      "intervalCount": 1
    }
  ],
  "error": ""
}
```

### Step 2: Authenticate and Get JWT Token

**Option A: Using Web Storefront**
1. Navigate to `https://localhost:5001`
2. Create an account or log in
3. The browser cookie will be set (but not usable for PublicApi)

**Option B: Using PublicApi Authenticate Endpoint**

```bash
curl -X POST "https://localhost:24723/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "P@ssw0rd!"
  }'
```

**Expected Response:**
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com"
}
```

Save the token — you'll need it for authenticated requests.

### Step 3: Create a Subscription

```bash
curl -X POST "https://localhost:24723/api/subscriptions" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }'
```

**Expected Response:**
```json
{
  "success": true,
  "subscriptionId": 12345678,
  "state": "active",
  "createdAt": "2026-09-06T10:30:00Z",
  "currentPeriodEndsAt": "2026-10-06T10:30:00Z",
  "planHandle": "eshop-pro",
  "error": ""
}
```

**What Happens Behind the Scenes:**
1. The service checks if a Maxio customer already exists for this user (using userId as reference)
2. If not, a new customer is created with the user's email
3. A subscription is created for the customer on the "Pro Plan"
4. A local record is stored in the eShopOnWeb database linking the user to their Maxio subscription
5. The subscription state and next billing date are returned to the client

### Step 4: Get User's Subscriptions

```bash
curl -X GET "https://localhost:24723/api/my-subscriptions" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

**Expected Response:**
```json
{
  "success": true,
  "subscriptions": [
    {
      "id": 12345678,
      "planHandle": "eshop-pro",
      "state": "active",
      "createdAt": "2026-09-06T10:30:00Z",
      "currentPeriodEndsAt": "2026-10-06T10:30:00Z"
    }
  ],
  "error": ""
}
```

### Step 5: Verify in Maxio (Optional)

Log in to your Maxio sandbox account at `https://cp-exp-4.chargify.com`:
1. Navigate to **Customers**
2. Search for the customer by email (e.g., `demouser@microsoft.com`)
3. Verify the subscription exists with:
   - Product: "eshop-subscribe"
   - Plan: "Pro Plan" (or "Basic Plan")
   - State: "Active"
   - Next billing date set correctly

## Error Handling & Troubleshooting

### Common Issues

**"Failed to get subscription plans"**
- Check Maxio credentials are correctly set
- Verify the product family handle matches your Maxio configuration
- Ensure network connectivity to Maxio API (https://{subdomain}.chargify.com)

**"Unauthorized" on authenticated endpoints**
- Token may have expired (JWT is valid for a period)
- Re-authenticate using the authenticate endpoint
- Ensure Bearer token is in Authorization header: `Authorization: Bearer <token>`

**"Failed to create subscription"**
- Plan handle may not exist in the product family
- Customer may already have a subscription to the same product
- Check Maxio sandbox for plan handles: "eshop-pro", "basic-plan"

**Database errors with in-memory database**
- In-memory database loses all data on app restart
- Subscriptions created won't persist across restarts
- Use SQL Server LocalDB for persistence

### Logging

Errors are logged to the console. Check the PublicApi output for detailed error messages when API calls fail.

## Architecture

### Data Flow

1. **Client** → calls subscription endpoints with JWT token
2. **PublicApi** → validates token, extracts user identity
3. **MaxioService** → calls Maxio API (Basic Auth with API key)
4. **Maxio** → creates/retrieves customers and subscriptions
5. **CatalogContext** → stores local subscription records
6. **Response** → returned to client with subscription details

### Key Files

- `src/PublicApi/Services/MaxioService.cs` — Maxio API integration
- `src/PublicApi/SubscriptionEndpoints/` — REST endpoints
- `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs` — Domain entity
- `src/Infrastructure/Data/Config/SubscriptionConfiguration.cs` — EF entity mapping
- `src/ApplicationCore/Specifications/UserSubscriptionsSpec.cs` — Query specification

### Configuration

Configuration keys in `appsettings.json` and via environment variables:
- `Maxio:ApiKey` (env: `MAXIO_API_KEY`)
- `Maxio:Subdomain` (env: `MAXIO_SITE_SUBDOMAIN`)
- `Maxio:ProductFamilyHandle` (env: `MAXIO_DEFAULT_PRODUCT_FAMILY`)
- `Maxio:BaseUrl` (optional override; defaults to https://{subdomain}.chargify.com)

## Security Notes

- **Secrets never enter the repository**: Credentials are loaded from user-secrets or environment variables
- **JWT-authenticated endpoints**: Subscription management endpoints require valid JWT tokens
- **Basic Auth to Maxio**: API key is sent with every Maxio API request (HTTP Basic Auth)
- **HTTPS only**: Both PublicApi and Maxio communication use HTTPS

## Next Steps

### Production Considerations

1. **PCI Compliance**: Currently, payment method is not required for subscriptions. For production with payment capture:
   - Store payment methods via Maxio's payment method API
   - Use Maxio.js for secure tokenization in the browser
   - Never transmit raw card details to your server

2. **Webhooks**: Subscribe to Maxio webhook events (subscription state changes, payment failures, etc.) to keep local database in sync

3. **Subscription Management**: Implement additional endpoints for:
   - Updating subscription plan (upgrade/downgrade)
   - Cancelling subscriptions
   - Viewing invoices and payment history

4. **Metered Components**: The integration includes support for metered components (per-unit charges). Implement usage tracking if needed

5. **Multi-tenancy**: If supporting multiple Maxio sites, extend configuration to allow per-tenant API keys and product families

## Support & Resources

- **Maxio Documentation**: https://developers.maxio.com/
- **eShopOnWeb Reference**: https://github.com/dotnet-architecture/eShopOnWeb
- **Maxio Sandbox**: https://docs.maxio.com/hc/en-us/articles/24294719944845-Getting-started-with-Maxio-sandbox
