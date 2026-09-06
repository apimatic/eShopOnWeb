# Maxio Subscription Billing Integration - Verification Guide

This guide walks you through verifying the working Maxio integration for eShopOnWeb subscription billing.

## Prerequisites

- .NET SDK 8.0+ (or .NET 10 with `DOTNET_ROLL_FORWARD=Major`)
- Maxio sandbox credentials (API key, site subdomain)
- Access to Maxio site `cp-exp-4` with:
  - Product Family `eshop-subscribe` (handle)
  - Plans: `eshop-pro` ($299/mo), `basic-plan` ($29/mo)
- Postman or curl for testing endpoints

## Step 1: Configure Maxio Credentials

Set environment variables for the PublicApi service:

```bash
# Linux/macOS
export MAXIO_API_KEY="your_sandbox_api_key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-4"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export MAXIO_ENVIRONMENT="sandbox"

# Windows (PowerShell)
$env:MAXIO_API_KEY="your_sandbox_api_key"
$env:MAXIO_SITE_SUBDOMAIN="cp-exp-4"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
$env:MAXIO_ENVIRONMENT="sandbox"
```

Or add to your development configuration:
```bash
# .NET user-secrets (inside repo root)
dotnet user-secrets init --project src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "your_key" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-4" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe" --project src/PublicApi
```

## Step 2: Build the Project

```bash
# Ensure no compilation errors
dotnet build src/PublicApi/PublicApi.csproj

# If SDK/runtime mismatch occurs:
export DOTNET_ROLL_FORWARD=Major
dotnet build src/PublicApi/PublicApi.csproj
```

## Step 3: Run the PublicApi Service

```bash
# From repo root with env vars set
export UseOnlyInMemoryDatabase=true
dotnet run --project src/PublicApi/PublicApi.csproj
```

The service starts on:
- HTTPS: `https://localhost:24883`
- HTTP: `http://localhost:24882`
- Swagger: `https://localhost:24883/swagger`

## Step 4: Authenticate and Get Bearer Token

Create a test user and get a JWT token:

```bash
# 1. Authenticate (POST to /api/authenticate)
curl -X POST https://localhost:24883/api/authenticate \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@123"
  }'

# Response contains "token" field:
# {"result": true, "token": "eyJhbGc..."}
# Save the token value for subsequent requests
```

**Default demo credentials** (if seeded):
- Username: `demouser@microsoft.com`
- Password: `Pass@123`

## Step 5: Test GET Subscription Plans

```bash
curl -X GET https://localhost:24883/api/subscription-plans \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  --insecure

# Expected response (200 OK):
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Professional Plan",
      "price": 299.00,
      "description": "Professional subscription plan"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00,
      "description": "Basic subscription plan"
    }
  ],
  "correlationId": "..."
}
```

## Step 6: Test POST Create Subscription

```bash
curl -X POST https://localhost:24883/api/subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "planHandle": "eshop-pro"
  }'

# Expected response (200 OK):
{
  "success": true,
  "subscriptionId": 12345678,
  "status": "active",
  "price": 29900,
  "nextBillingDate": null,
  "correlationId": "..."
}
```

**Note:** The first subscription for an email creates a customer in Maxio. Subsequent subscriptions for the same email reuse that customer (idempotent).

## Step 7: Test GET My Subscriptions

```bash
curl -X GET https://localhost:24883/api/my-subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  --insecure

# Expected response (200 OK):
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 12345678,
      "planHandle": "eshop-pro",
      "status": "active",
      "price": 0.00,
      "createdAt": "2026-09-06T...",
      "nextBillingDate": null,
      "canceledAt": null
    }
  ],
  "correlationId": "..."
}
```

## Step 8: Verify in Maxio Dashboard

1. Log in to Maxio sandbox at `cp-exp-4`
2. Navigate to **Customers**
3. Find customer with email matching your test user
4. Verify subscription appears with correct:
   - Plan handle (`eshop-pro` or `basic-plan`)
   - Status (`active`)
   - Next billing date (calculated at subscription time)

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Maxio configuration is incomplete" | Verify all 3 env vars are set and non-empty |
| 401 Unauthorized | Ensure bearer token is valid; re-authenticate if expired |
| 400 Bad Request from Maxio | Check that product family handle and plan handle are correct |
| in-memory database loses data on restart | Expected behavior; data persists only within a single run |
| HTTPS certificate error | Ensure dev cert is trusted: `dotnet dev-certs https --check` |
| Customer already exists | Maxio API correctly returns existing customer by email (idempotent) |

## Architecture Notes

### Request Flow: Create Subscription

```
User (JWT token)
    ↓
POST /api/subscriptions
    ↓
CreateSubscriptionEndpoint (validates JWT, extracts user ID/email)
    ↓
MaxioSubscriptionService
    ├─→ GetOrCreateCustomerAsync (checks Maxio by email, creates if missing)
    ├─→ CreateSubscriptionAsync (POST to Maxio /subscriptions.json)
    └─→ Save to local Subscription entity (for audit/reference)
    ↓
Response: subscription_id, status, next_billing_date
```

### No SDK Dependency

This integration uses direct HTTP calls to Maxio REST API with Basic Auth, avoiding SDK version conflicts and providing clearer error visibility.

### Idempotency Guarantees

- **Customer creation**: Checked by email before creating; existing customer reused
- **Subscription creation**: New subscription created each time (no duplicate detection)
- Database entity tracks mapping for local audit trail

## Next Steps

- Implement `GetUserSubscriptions` endpoint to fetch list from Maxio
- Add subscription cancellation endpoint
- Wire up payment methods for trial conversion
- Add webhook receivers for Maxio lifecycle events
- Integrate with checkout flow for subscription-to-order transitions

