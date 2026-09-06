# Maxio Subscription Billing Integration Setup & Verification Guide

## Overview

This guide walks through setting up and verifying the Maxio subscription billing integration added to the eShopOnWeb reference application. This integration enables subscription management while maintaining the existing one-time purchase functionality.

## Prerequisites

- .NET 8.0 SDK (or run with `DOTNET_ROLL_FORWARD=Major`)
- eShopOnWeb repository
- Maxio sandbox credentials:
  - `MAXIO_API_KEY`: Your Maxio API key
  - `MAXIO_SITE_SUBDOMAIN`: Your Maxio sandbox subdomain
  - `MAXIO_ENVIRONMENT`: Sandbox environment identifier
  - `MAXIO_DEFAULT_PRODUCT_FAMILY`: Product family handle (default: `eshop-subscribe`)

## Setup Steps

### 1. Configure Environment Variables

Set the following environment variables on your system. These values are loaded into the configuration at runtime:

**On Windows (PowerShell):**
```powershell
$env:MAXIO_API_KEY = "your_api_key_here"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

**On Windows (Command Prompt):**
```cmd
set MAXIO_API_KEY=your_api_key_here
set MAXIO_SITE_SUBDOMAIN=your_subdomain
set MAXIO_ENVIRONMENT=sandbox
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
set UseOnlyInMemoryDatabase=true
set DOTNET_ROLL_FORWARD=Major
```

**On macOS/Linux (Bash):**
```bash
export MAXIO_API_KEY="your_api_key_here"
export MAXIO_SITE_SUBDOMAIN="your_subdomain"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
export DOTNET_ROLL_FORWARD="Major"
```

### 2. Verify Configuration Files

The configuration has been added to `src/PublicApi/appsettings.json`:

```json
"Maxio": {
  "ApiKey": "",
  "Subdomain": "",
  "ProductFamilyHandle": "eshop-subscribe",
  "BaseUrl": ""
}
```

- `ApiKey`: Loaded from `MAXIO_API_KEY` env var
- `Subdomain`: Loaded from `MAXIO_SITE_SUBDOMAIN` env var
- `ProductFamilyHandle`: Loaded from `MAXIO_DEFAULT_PRODUCT_FAMILY` env var (default: `eshop-subscribe`)
- `BaseUrl`: Optional override; if set, uses this URL instead of constructing from subdomain

### 3. Run the Application

**For PublicApi (subscription endpoints):**
```bash
cd src/PublicApi
dotnet run
```

The API will start on `https://localhost:27303`. The JWT authentication requires a valid token to access subscription endpoints.

**For Web (login to get tokens):**
```bash
cd src/Web
dotnet run
```

The web app will start on `https://localhost:5001`.

## Subscription Architecture

### Maxio Sandbox Catalog

The Maxio sandbox site `cp-exp-2` includes pre-configured subscription plans:

| Entity | Handle | ID (approx) | Price | Interval |
|--------|--------|-------------|-------|----------|
| Product Family | `eshop-subscribe` | 3023074 | N/A | N/A |
| Pro Plan | `eshop-pro` | 7126957 | $299.00/mo | Monthly |
| Basic Plan | `basic-plan` | 7126958 | $29.00/mo | Monthly |
| Metered Component | `api-call` | 3057195 | $0.01/unit | Metered |

**Important:** Numeric IDs are reassigned on re-seed; always use handles for reference.

### Implementation Architecture

The integration is organized into three layers:

**1. ApplicationCore (`MaxioConfiguration.cs`)**
- Configuration model binding from `Maxio:*` settings
- Constructs Maxio API base URL from subdomain or explicit override

**2. Infrastructure Services**
- **`MaxioApiClient.cs`**: Low-level HTTP client with automatic auth headers
  - Handles Basic auth encoding
  - Converts snake_case JSON to camelCase for .NET
  - Logs all API calls
  
- **`MaxioService.cs`**: Business logic layer
  - Manages customer lookup/creation (idempotent via reference field)
  - Manages subscription creation and retrieval
  - Maps Maxio API responses to DTOs

**3. PublicApi Endpoints** (JWT-authenticated minimal APIs)
- `GET /api/subscription-plans`: List available subscription plans
- `POST /api/subscriptions`: Create a subscription for authenticated user
- `GET /api/my-subscriptions`: Retrieve user's active subscriptions

### Customer Mapping

Customers are identified in Maxio by a `reference` field, which is set to the eShopOnWeb user's ID (GUID). This enables:
- **Idempotency**: Calling create multiple times never duplicates customers
- **Persistence**: User↔Customer mapping survives the entire session

When a subscription endpoint is called:
1. Extract user from JWT token
2. Look up or create Maxio customer with reference = user ID
3. Create/list subscriptions for that customer

## Verification Steps

### Step 1: Build Verification

Verify the project builds without errors:

```bash
cd repo
dotnet build src/PublicApi/PublicApi.csproj
```

**Expected:** Build succeeds (warnings about vulnerabilities and obsolete code are pre-existing).

### Step 2: Get Authentication Token

Start the PublicApi service and authenticate:

**Create a user (or use existing):**
```bash
# Email: testuser@example.com
# Password: Pass@word1
```

**Get a JWT token via POST to authenticate endpoint:**
```bash
curl -X POST "https://localhost:27303/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "testuser@example.com",
    "password": "Pass@word1"
  }' \
  -k  # Ignore certificate validation for localhost dev
```

**Response (example):**
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "testuser@example.com"
}
```

Save the `token` value for the next steps.

### Step 3: List Available Subscription Plans

**Endpoint:** `GET /api/subscription-plans`

```bash
TOKEN="your_token_here"
curl -X GET "https://localhost:27303/api/subscription-plans" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -k
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic subscription",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 4: Create a Subscription

**Endpoint:** `POST /api/subscriptions`

```bash
TOKEN="your_token_here"
curl -X POST "https://localhost:27303/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  -k
```

**Expected Response:**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "price": 299.00,
  "nextBillingAt": "2026-10-07T12:34:56Z",
  "createdAt": "2026-09-07T12:34:56Z"
}
```

**Idempotency Check:** Run the same curl command again. Since the customer reference (user ID) already exists in Maxio:
- If the user has no subscriptions: Creates a new subscription
- If the user already has a subscription to this plan: Returns the existing subscription (may error or update, depending on Maxio's behavior)

### Step 5: List User's Subscriptions

**Endpoint:** `GET /api/my-subscriptions`

```bash
TOKEN="your_token_here"
curl -X GET "https://localhost:27303/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "price": 299.00,
      "nextBillingAt": "2026-10-07T12:34:56Z",
      "createdAt": "2026-09-07T12:34:56Z"
    }
  ]
}
```

### Step 6: Verify Maxio Portal

Log into your Maxio sandbox portal (`cp-exp-2`) and verify:
1. A new customer record exists with reference = your eShopOnWeb user ID
2. The customer has a subscription to the Pro Plan (or whichever plan you selected)
3. The subscription state is "active" with the next billing date set

## Troubleshooting

### Issue: "Unauthorized" responses on subscription endpoints

**Cause:** JWT token is missing or invalid

**Solution:**
1. Ensure you've called the `/api/authenticate` endpoint first
2. Verify the token is included in the `Authorization: Bearer <token>` header
3. Check that the token hasn't expired (tokens expire after 7 days)

### Issue: "Failed to create subscription" errors

**Cause:** Configuration not loaded or Maxio API unreachable

**Solution:**
1. Verify environment variables are set correctly
2. Check the console logs for specific error messages
3. Ensure the API key and subdomain are correct
4. Verify you can reach Maxio API: `curl https://<SUBDOMAIN>.chargify.com/customers.json -u <API_KEY>:X`

### Issue: Customer lookup fails with 404

**Cause:** Maxio API credentials are incorrect

**Solution:**
1. Re-verify `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN`
2. Test credentials directly: `curl https://<SUBDOMAIN>.chargify.com/subscriptions.json -u <API_KEY>:X`

### Issue: Plans endpoint returns empty list

**Cause:** Product family handle is incorrect or doesn't exist

**Solution:**
1. Verify `MAXIO_DEFAULT_PRODUCT_FAMILY` is set to `eshop-subscribe`
2. In Maxio portal, confirm the product family exists and contains plans
3. Check product family details to verify the handle is correct

## Secrets Management

**Important:** Never commit API keys or other secrets to the repository.

- Environment variables are the secure source of truth
- The `appsettings.json` file contains only placeholders (empty strings)
- Actual secrets are provided at runtime via environment variables
- In production, use appropriate secret management (Azure Key Vault, AWS Secrets Manager, etc.)

## Performance Considerations

1. **Customer Creation**: First subscription request creates a customer (slightly slower). Subsequent requests reuse the existing customer (fast lookup via reference).
2. **API Calls**: Each endpoint makes 1–2 API calls to Maxio (customer lookup + subscription operation).
3. **Caching**: Consider implementing caching for subscription plans since they change infrequently.

## Known Limitations

1. **Trial Periods**: The seeded plans have no trial period. Trial support can be added via the `trial_interval` parameter in subscription creation.
2. **Components**: Metered component billing is configured in Maxio but not yet exposed via the API.
3. **Dunning/Retries**: Payment retry logic is configured in Maxio but not customizable via this API layer.
4. **Payment Profiles**: Payment information is not required for the seeded plans (payment_method_not_required = true). If changing to paid-only plans, payment profile creation would be needed.

## Integration with Existing Flows

The subscription capability is **completely additive** to the existing one-time purchase (cart/checkout) flow:
- No changes to existing catalog, basket, or order endpoints
- Subscription endpoints are in a separate namespace (`/api/subscription-*`)
- Subscription and order purchases are independent—a user can have both

## Next Steps

1. **Customize Plans**: Create additional subscription plans in your Maxio sandbox
2. **Handle Webhooks**: Integrate Maxio webhooks for subscription state changes (cancellation, expiration, etc.)
3. **UI Integration**: Add a subscription browsing and checkout UX in the web frontend
4. **Billing Portal**: Implement a customer self-service subscription management page
5. **Analytics**: Track subscription acquisition, churn, and MRR (Monthly Recurring Revenue)

## References

- [Maxio Billing API Docs](https://developers.chargify.com/docs)
- eShopOnWeb: https://github.com/dotnet-architecture/eShopOnWeb
- Subscription Signup Flow: [Maxio Docs - Subscription Signup](https://developers.chargify.com/core-concepts/subscription-signup)
