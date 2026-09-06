# Maxio Subscription Billing Integration — Verification Guide

## Overview

This guide walks through verifying the working Maxio subscription billing integration on eShopOnWeb. The integration adds three JWT-authenticated endpoints to the PublicApi that handle subscription plans, subscription creation, and subscription listing.

---

## Prerequisites

1. **Maxio Sandbox Credentials** (supplied separately):
   - Sandbox API key
   - Sandbox site subdomain (e.g., `cp-exp-4`)
   
2. **Environment Setup**:
   - .NET 8.0+ SDK (or allow rollforward to .NET 10)
   - `UseOnlyInMemoryDatabase=true` (no SQL Server required)
   - `DOTNET_ROLL_FORWARD=Major` if using .NET 10 SDK with .NET 8 project

3. **Dev Certificate** (for HTTPS):
   ```bash
   dotnet dev-certs https --check
   # If not trusted, run:
   dotnet dev-certs https --trust
   ```

---

## Step 1: Configure Environment Variables

Before running the app, set the Maxio sandbox credentials as environment variables:

**On Windows (PowerShell):**
```powershell
$env:MAXIO_API_KEY = "<your-sandbox-api-key>"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-4"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
```

**On Linux/macOS (Bash):**
```bash
export MAXIO_API_KEY="<your-sandbox-api-key>"
export MAXIO_SITE_SUBDOMAIN="cp-exp-4"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
```

---

## Step 2: Build the Project

```bash
cd src/PublicApi
dotnet build -c Debug
```

Expected output: `Build succeeded. ... 0 Error(s)`

---

## Step 3: Run the PublicApi Application

```bash
dotnet run --project src/PublicApi/PublicApi.csproj
```

The app will start and open Swagger at `https://localhost:27583/swagger/index.html`

Expected console output:
```
info: Seeding Database...
info: PublicApi App created...
info: LAUNCHING PublicApi
```

---

## Step 4: Authenticate to Get Bearer Token

### Via Swagger UI:
1. Open `https://localhost:27583/swagger/index.html`
2. Find the **`POST /api/authenticate`** endpoint
3. Click "Try it out"
4. Enter credentials (from seed data):
   ```json
   {
     "username": "demouser@microsoft.com",
     "password": "Pass@word1"
   }
   ```
5. Execute → Copy the `token` from the response

### Via curl:
```bash
curl -X POST "https://localhost:27583/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  --insecure
```

Response will include:
```json
{
  "token": "eyJhbGc...",
  "result": true,
  "username": "demouser@microsoft.com"
}
```

---

## Step 5: List Subscription Plans

### Via Swagger UI:
1. Click the padlock icon in the top-right → Paste your bearer token
2. Find **`GET /api/subscription-plans`**
3. Click "Try it out" → Execute

### Via curl:
```bash
curl -X GET "https://localhost:27583/api/subscription-plans" \
  -H "Authorization: Bearer <your-token>" \
  --insecure
```

**Expected response** (200 OK):
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with advanced features"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription plan"
    }
  ]
}
```

---

## Step 6: Create a Subscription

### Via curl:
```bash
curl -X POST "https://localhost:27583/api/subscriptions" \
  -H "Authorization: Bearer <your-token>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

**Expected response** (201 Created):
```json
{
  "subscriptionId": 123456789,
  "customerId": 987654321,
  "state": "active",
  "createdAt": "2026-09-07T12:34:56Z",
  "nextBillingAt": "2026-10-07T12:34:56Z"
}
```

**Verify in Swagger:**
- Look for `Location` header in response: `/api/my-subscriptions/{subscriptionId}`
- Status should be **201 Created**

---

## Step 7: List User's Subscriptions

### Via curl:
```bash
curl -X GET "https://localhost:27583/api/my-subscriptions" \
  -H "Authorization: Bearer <your-token>" \
  --insecure
```

**Expected response** (200 OK):
```json
{
  "subscriptions": [
    {
      "id": 123456789,
      "state": "active",
      "productId": 7126957,
      "createdAt": "2026-09-07T12:34:56Z",
      "nextBillingAt": "2026-10-07T12:34:56Z"
    }
  ]
}
```

---

## Step 8: Verify Idempotency

Call **`POST /api/subscriptions`** again with the same bearer token (same user):

```bash
curl -X POST "https://localhost:27583/api/subscriptions" \
  -H "Authorization: Bearer <your-token>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"basic-plan"}' \
  --insecure
```

**Expected:**
- New subscription created with different `subscriptionId`
- Same `customerId` (customer was found by userId)
- **`GET /api/my-subscriptions`** now returns 2 subscriptions

---

## Step 9: Verify Missing Auth

Call any subscription endpoint without a token:

```bash
curl -X GET "https://localhost:27583/api/subscription-plans" --insecure
```

**Expected response** (401 Unauthorized)

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| **"Connection refused" to Maxio API** | Verify `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN` are set. Check Maxio sandbox is accessible. |
| **"401 Unauthorized" with subscription endpoints** | Token missing or expired. Get fresh token from `/api/authenticate`. |
| **"400 Bad Request" on subscription create** | Verify `productHandle` matches a seeded plan (`eshop-pro` or `basic-plan`). |
| **Swagger shows "No servers available"** | Ignore warning; API is on `https://localhost:27583`. |
| **"HTTPS connection error"** | Run `dotnet dev-certs https --trust`. |
| **Empty subscription list after create** | Wait a moment; in-memory DB is live. Verify customer exists in Maxio by subscribing with `eshop-pro` first. |

---

## Manual Maxio Verification (Optional)

If you want to verify customers/subscriptions directly in Maxio:

1. Log in to Maxio sandbox site (`cp-exp-4`)
2. Navigate to **Customers**
3. Search for customer with reference = eShopOnWeb userId (format: `<guid>`)
4. Click customer → **Subscriptions** tab
5. Verify subscription shows state, billing dates, product handle

---

## API Endpoint Summary

| Method | Endpoint | Auth | Body | Returns |
|--------|----------|------|------|---------|
| `GET` | `/api/subscription-plans` | JWT | — | Plans list |
| `POST` | `/api/subscriptions` | JWT | `{productHandle}` | Subscription (201) |
| `GET` | `/api/my-subscriptions` | JWT | — | User's subscriptions |

---

## Integration Architecture

```
eShopOnWeb (PublicApi)
  ↓
  JWT Auth Middleware
  ↓
  [Subscription Endpoints]
  ├─ ListSubscriptionPlansEndpoint
  ├─ CreateSubscriptionEndpoint
  └─ ListMySubscriptionsEndpoint
  ↓
  MaxioService (IMaxioService)
  ├─ GetOrCreateCustomerAsync (idempotent by userId)
  ├─ ListProductsAsync
  ├─ CreateSubscriptionAsync
  └─ ListCustomerSubscriptionsAsync
  ↓
  HttpClient (Basic Auth: API key)
  ↓
  Maxio Sandbox API (cp-exp-4)
  ├─ POST /customers.json
  ├─ GET /products.json
  ├─ POST /subscriptions.json
  └─ GET /subscriptions.json
```

---

## Next Steps

1. **For Production**:
   - Replace sandbox credentials with production Maxio credentials
   - Store API key in Azure Key Vault or secure secrets manager (NOT in repo)
   - Add logging/monitoring for subscription events
   - Test with real payment methods (require PCI compliance)
   - Set up webhooks for subscription state changes

2. **For UI Integration**:
   - Frontend can call `/api/subscription-plans` to display available plans
   - On plan selection, POST to `/api/subscriptions`
   - Periodically poll `/api/my-subscriptions` to display user's active subscriptions

3. **For Feature Expansion**:
   - Cancel subscription endpoint
   - Update subscription (change plan)
   - Metered component usage tracking (using `api-call` component)
   - Webhook handlers for subscription events (signup_success, renewal_success, etc.)

---

## Success Criteria ✓

Integration is working when:
- [x] All three endpoints respond with 200/201 status
- [x] JWT authentication required and enforced
- [x] Customers created idempotently (same userId → same customer)
- [x] Subscriptions persist across API calls
- [x] Next billing dates returned correctly
- [x] All Maxio API errors handled gracefully
