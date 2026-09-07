# Maxio Subscription Billing Integration — Verification Guide

This guide walks through testing the recurring subscription functionality added to eShopOnWeb.

## Prerequisites

1. **.NET SDK 10.0+** installed
2. **ASP.NET Core 8.0 runtime** installed (or allow rollForward to latestMajor)
3. **Environment variables** set:
   - `MAXIO_API_KEY` — API key for Maxio sandbox
   - `MAXIO_SITE_SUBDOMAIN` — Maxio site subdomain (e.g., `cp-exp-3`)
   - `MAXIO_ENVIRONMENT` — Maxio environment (e.g., `US`)
   - `MAXIO_DEFAULT_PRODUCT_FAMILY` — Product family handle (e.g., `eshop-subscribe`)
   - `UseOnlyInMemoryDatabase=true` — Use in-memory database (no SQL Server required)
   - `APP_PORT_BLOCK_BASE` — Base port for the app (e.g., 28700)

4. **JWT Token** for testing protected endpoints (obtained from the authenticate endpoint)

## Step-by-Step Verification

### 1. Build the Solution

```bash
cd repo
dotnet build
```

Expected result: **Build succeeds** with no errors (some NuGet vulnerability warnings are expected).

### 2. Start the PublicApi Application

```bash
dotnet run --project src/PublicApi
```

Expected output:
```
PublicApi App created...
Seeding Database...
LAUNCHING PublicApi
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:28703
```

The app listens on `https://localhost:28703` by default (check `appsettings.json` for the exact port).

### 3. Authenticate and Get a JWT Token

**Request:**
```bash
curl -k -X POST https://localhost:28703/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

**Expected response:**
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com",
  "correlationId": "..."
}
```

Copy the `token` value — you'll need it for the next calls.

### 4. Test GET /api/subscription-plans

**Request:**
```bash
BEARER_TOKEN="<paste-token-here>"

curl -k -X GET https://localhost:28703/api/subscription-plans \
  -H "Authorization: Bearer $BEARER_TOKEN"
```

**Expected response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "correlationId": "..."
}
```

### 5. Test POST /api/subscriptions (Create Subscription)

**Request:**
```bash
BEARER_TOKEN="<paste-token-here>"

curl -k -X POST https://localhost:28703/api/subscriptions \
  -H "Authorization: Bearer $BEARER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }'
```

**Expected response (201 Created):**
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "priceInCents": 29900,
    "nextBillingDate": "2026-10-07T00:00:00Z",
    "createdAt": "2026-09-07T12:34:56Z",
    "activatedAt": "2026-09-07T12:34:56Z"
  },
  "correlationId": "..."
}
```

**Key assertions:**
- HTTP status is `201 Created`
- `subscription.state` is `"active"`
- `subscription.nextBillingDate` is set (next billing cycle)
- `subscription.id` is a non-zero integer

### 6. Test GET /api/my-subscriptions (List User Subscriptions)

**Request:**
```bash
BEARER_TOKEN="<paste-token-here>"

curl -k -X GET https://localhost:28703/api/my-subscriptions \
  -H "Authorization: Bearer $BEARER_TOKEN"
```

**Expected response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "priceInCents": 29900,
      "nextBillingDate": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:34:56Z",
      "activatedAt": "2026-09-07T12:34:56Z"
    }
  ],
  "correlationId": "..."
}
```

**Key assertions:**
- HTTP status is `200 OK`
- `subscriptions` array contains the subscription(s) created in step 5
- Each subscription has the correct plan details

### 7. Test Idempotency (Optional)

Re-run the POST /api/subscriptions request from step 5 **without changing the user or plan**.

**Expected behavior:**
- The system detects an existing subscription
- Either returns the existing subscription or creates a new one (depending on implementation)
- No duplicate subscriptions are created for the same user/plan combination

### 8. Test Error Handling (Optional)

**Request with invalid plan handle:**
```bash
BEARER_TOKEN="<paste-token-here>"

curl -k -X POST https://localhost:28703/api/subscriptions \
  -H "Authorization: Bearer $BEARER_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "nonexistent-plan"
  }'
```

**Expected response (4xx or 5xx):**
- HTTP status indicates an error (not 2xx)
- Response contains a descriptive error message

## Troubleshooting

### "No plans returned" from GET /api/subscription-plans
- Verify `MAXIO_SITE_SUBDOMAIN` environment variable is set to a valid Maxio sandbox site
- Confirm the product family handle matches a real family in your Maxio sandbox
- Check app logs for API error details

### "Unauthorized" (401) on protected endpoints
- Ensure you copied the JWT token correctly
- The token may have expired; re-authenticate to get a fresh one
- Check that the Authorization header is `Bearer <token>` (not `Bearer: <token>`)

### "Connection refused" or "Cannot reach the server"
- Verify the PublicApi app is running (check the console for startup messages)
- Confirm the port matches your `appsettings.json` (default 28703 for https)
- Ensure HTTPS is enabled and the dev certificate is trusted

### Database errors
- If using real SQL Server and LocalDB is not installed, set `UseOnlyInMemoryDatabase=true`
- In-memory database loses data on app restart — this is expected

## Architecture Notes

### File Layout

- **`src/PublicApi/SubscriptionEndpoints/`** — HTTP endpoints
  - `GetSubscriptionPlansEndpoint.cs` — `GET /api/subscription-plans`
  - `CreateSubscriptionEndpoint.cs` — `POST /api/subscriptions`
  - `GetMySubscriptionsEndpoint.cs` — `GET /api/my-subscriptions`
  
- **`src/PublicApi/SubscriptionService.cs`** — Orchestration layer for Maxio operations
  
- **`src/PublicApi/MaxioConfiguration.cs`** — Configuration model for Maxio settings
  
- **`src/PublicApi/Program.cs`** — DI setup for Maxio client and SubscriptionService

### Design Decisions

1. **Idempotent Customer Lookup** — Uses `ReadCustomerByReference()` with the eShopOnWeb user ID as the reference key. On first subscription, the customer is created with the same reference.

2. **Plan Enumeration** — Fetches plans from the `eshop-subscribe` product family. Plans are returned as-is from Maxio (name, handle, price).

3. **Error Handling** — Uses typed `SdkException<TError>` catches with `TryGet…` accessors for typed errors and `TryGetRawError()` for raw HTTP responses.

4. **No Card Required** — Subscriptions are created without payment profiles, as Maxio sandbox plans are configured with `payment_method_not_required: true`.

5. **JWT Authentication** — Endpoints require JWT bearer token from the authenticate endpoint. User ID is extracted from JWT claims.

## Next Steps (For Production)

1. **Database Persistence** — Switch from in-memory to a real database (SQL Server, PostgreSQL, etc.)
2. **Secrets Management** — Store Maxio API key in a secrets manager (Azure Key Vault, AWS Secrets Manager, etc.) instead of environment variables
3. **Error Logging** — Wire up centralized logging (Application Insights, Sentry, DataDog, etc.)
4. **Webhook Handling** — Implement Maxio webhooks for subscription lifecycle events (payment failure, upgrade/downgrade, etc.)
5. **Subscription Management** — Add endpoints for cancel, pause, resume, upgrade/downgrade subscriptions
6. **Testing** — Add integration tests against the Maxio sandbox
7. **Rate Limiting** — Implement rate limiting to prevent abuse

## Success Criteria

✅ All three endpoints return 200/201 responses with correct data
✅ Plans are fetched from Maxio sandbox
✅ Customer is created idempotently (no duplicates on retry)
✅ Subscription is created and state is "active"
✅ User subscriptions are listed correctly
✅ Error responses are descriptive and use correct HTTP status codes
✅ Unauthenticated requests are rejected with 401

---

**Generated:** 2026-09-07
**Integration Status:** Complete and Ready for Verification
