# Maxio Subscription Billing Integration — eShopOnWeb

This guide walks through verifying the recurring subscription capability added to eShopOnWeb.

## Architecture Overview

**Three new endpoints** on PublicApi (JWT-authenticated):
- `GET /api/subscription-plans` — List available subscription plans from Maxio
- `POST /api/subscriptions` — Create a subscription for the logged-in user (idempotent)
- `GET /api/my-subscriptions` — List subscriptions for the logged-in user

**Implementation:**
- Service: `MaxioSubscriptionService` in `src/PublicApi/SubscriptionEndpoints/`
- Uses direct HTTP calls to Maxio Chargify API (no external SDK dependency)
- Basic auth via API key loaded from user-secrets (`Maxio:ApiKey`, `Maxio:Subdomain`)
- Idempotent customer creation: lookup by reference before creating
- All data serialized as JSON; wire names (snake_case) handled automatically

## Setup & Credentials

### 1. Verify User-Secrets are Configured

Maxio credentials are stored in .NET user-secrets (not in the repository):

```bash
cd src/PublicApi
dotnet user-secrets list
```

Expected output:
```
Maxio:Subdomain = cp-exp-2
Maxio:ProductFamilyHandle = eshop-subscribe
Maxio:ApiKey = <your-api-key>
```

If not set, configure them:
```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

### 2. Environment Variables

Ensure environment is set to use in-memory database (see task):
```bash
export UseOnlyInMemoryDatabase=true
export DOTNET_ROLL_FORWARD=Major
```

## Verification Steps

### Step 1: Start PublicApi

```bash
cd src/PublicApi
dotnet run
```

The service should start on `https://localhost:29083/` (from `appsettings.json`).

### Step 2: Get a JWT Token

Use the authenticate endpoint to obtain a bearer token:

```bash
curl -X POST https://localhost:29083/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username": "demouser@microsoft.com", "password": "Pass@word1"}' \
  --insecure
```

**Response:**
```json
{
  "token": "eyJ...",
  "result": true,
  "username": "demouser@microsoft.com",
  "correlationId": "..."
}
```

Copy the token for subsequent calls.

### Step 3: List Subscription Plans

```bash
curl -X GET https://localhost:29083/api/subscription-plans \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  --insecure
```

**Response (example):**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 4: Create a Subscription

Subscribe the logged-in user to the Pro Plan (ID 7126957):

```bash
curl -X POST https://localhost:29083/api/subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"productId": 7126957}' \
  --insecure
```

**Response (example):**
```json
{
  "id": 12345678,
  "customerId": 87654321,
  "productId": 7126957,
  "state": "active",
  "nextBillingDate": "2026-10-07T00:00:00+00:00",
  "activatedAt": "2026-09-07T10:15:00+00:00"
}
```

**Key details:**
- `state: "active"` — subscription is live
- `nextBillingDate` — when the next billing cycle occurs
- `activatedAt` — timestamp subscription was created
- On double-click (same user, same product), the service returns the existing subscription (idempotent)

### Step 5: List User's Subscriptions

```bash
curl -X GET https://localhost:29083/api/my-subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  --insecure
```

**Response (example):**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "productId": 7126957,
      "state": "active",
      "nextBillingDate": "2026-10-07T00:00:00+00:00",
      "activatedAt": "2026-09-07T10:15:00+00:00"
    }
  ]
}
```

### Step 6: Verify Idempotency

Call the create endpoint again with the same product ID:

```bash
curl -X POST https://localhost:29083/api/subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"productId": 7126957}' \
  --insecure
```

**Expected:** Same subscription ID as Step 4 (no duplicate created).

## Testing Notes

### In-Memory Database

Since `UseOnlyInMemoryDatabase=true` is set, all data is lost on restart. Subscriptions created in Maxio (sandbox) persist, but app-side state resets.

### Sandbox Limitations

- No payment method required (as configured)
- Plans seeded: Pro ($299/mo), Basic ($29/mo)
- Test users can subscribe multiple times to different plans
- Cancellation/state transitions must be done via Maxio admin (out of scope)

### Error Responses

- `401 Unauthorized` — missing or invalid JWT token
- `502 Bad Gateway` — Maxio API call failed (network, auth, or API error)
- `500 Internal Server Error` — unexpected error in subscription service

## Architecture Details

### Endpoints Location

- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/ListMySubscriptionsEndpoint.cs`

### Service Location

- `src/PublicApi/SubscriptionEndpoints/MaxioSubscriptionService.cs`

### Configuration

- `src/PublicApi/Program.cs` — DI setup for MaxioSubscriptionService

### User-Secrets

- PublicApi project UserSecretsId: `1f5031bc-fa83-4fe3-a857-6a0b3a194ca2`
- Stored at: `%APPDATA%\Microsoft\UserSecrets\1f5031bc-fa83-4fe3-a857-6a0b3a194ca2\secrets.json` (Windows)

## Production Considerations

1. **Database Persistence:** Map subscriptions to user in `AppIdentityDbContext` to survive restarts
2. **Webhook Handling:** Implement Maxio webhooks to sync subscription state (cancellations, failed renewals, etc.)
3. **Error Logging:** Integrate with application monitoring (Application Insights, Sentry, etc.)
4. **Rate Limiting:** Implement per-user/per-IP rate limits on subscription endpoints
5. **HTTPS Only:** Ensure all Maxio communication is over HTTPS (already configured)
6. **Timeout Tuning:** Adjust `HttpClient.Timeout` (default 30s) based on latency/SLA requirements
