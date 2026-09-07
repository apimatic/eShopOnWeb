# Maxio Subscription Billing Integration — Verification Guide

This guide walks through setting up and testing the Maxio Advanced Billing integration for eShopOnWeb subscription capabilities.

## Prerequisites

- .NET 8.0 SDK (or .NET 10 with rollForward enabled in global.json)
- Maxio sandbox API key and account (site: `cp-exp-1`)
- HTTPS dev certificate (run `dotnet dev-certs https --trust`)
- curl or Postman for API testing

## Setup Steps

### 1. Configure User Secrets

The integration reads Maxio credentials from .NET user-secrets, not from configuration files (to prevent secrets in the repo).

Run these commands from the `src/PublicApi` directory:

```bash
cd src/PublicApi

# Initialize user secrets (if not already done)
dotnet user-secrets init

# Store Maxio credentials (replace with actual values)
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_SANDBOX_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-1"
dotnet user-secrets set "Maxio:Environment" "sandbox"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

**Note:** The API key is the authentication token from your Maxio sandbox account. You can find it in Maxio's admin interface under API Credentials.

### 2. Start the PublicApi Server

```bash
cd src/PublicApi
dotnet run --configuration Debug
```

The server starts at `https://localhost:28963` with Swagger documentation available at `https://localhost:28963/swagger`.

### 3. Get an Authentication Token

Before calling subscription endpoints, you need a JWT token. The PublicApi provides an authenticate endpoint:

```bash
# Request a token
curl -X POST https://localhost:28963/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"demouser@microsoft.com"}'
```

This returns a response like:
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "username": "demouser@microsoft.com"
}
```

Copy the `token` value for use in the next steps.

## Testing the Subscription Endpoints

### 1. Get Available Subscription Plans

Lists the subscription plans available for purchase (e.g., Pro Plan $299/mo, Basic Plan $29/mo).

```bash
TOKEN="<your-token-from-above>"

curl -X GET https://localhost:28963/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json"
```

**Expected Response (200 OK):**
```json
{
  "result": true,
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "billingInterval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "priceInCents": 2900,
      "billingInterval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 2. Subscribe to a Plan

Creates a subscription for the authenticated user. This endpoint idempotently creates a Maxio customer if one doesn't exist (using the user's email as the reference key).

```bash
TOKEN="<your-token-from-above>"

curl -X POST https://localhost:28963/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

**Expected Response (200 OK):**
```json
{
  "result": true,
  "subscription": {
    "id": 12345678,
    "productName": "Pro Plan",
    "state": "active",
    "priceInCents": 29900,
    "nextBillingAt": "2026-10-07T12:00:00Z",
    "activatedAt": "2026-09-07T12:00:00Z"
  }
}
```

**Error Responses:**
- `422 Unprocessable Entity` — validation error from Maxio (e.g., product not found, invalid handle)
- `500 Internal Server Error` — Maxio API error or SDK exception

### 3. Get User's Active Subscriptions

Lists all active subscriptions for the authenticated user.

```bash
TOKEN="<your-token-from-above>"

curl -X GET https://localhost:28963/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json"
```

**Expected Response (200 OK):**
```json
{
  "result": true,
  "subscriptions": [
    {
      "id": 12345678,
      "productName": "Pro Plan",
      "state": "active",
      "priceInCents": 29900,
      "balanceInCents": 0,
      "nextBillingAt": "2026-10-07T12:00:00Z",
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
      "activatedAt": "2026-09-07T12:00:00Z"
    }
  ]
}
```

## Idempotency & Double-Click Safety

The integration uses **idempotent customer creation** — calling the subscribe endpoint twice with the same user doesn't create duplicate customers or subscriptions:

1. **First call** — Creates a Maxio customer (using email as reference) and subscribes to the plan.
2. **Second call** — Looks up the existing customer by email reference, reuses it, and attempts to create a new subscription.

If the same plan is requested twice, the second attempt returns a Maxio API error (already subscribed), which surfaces as a 422 response with a descriptive message.

## Debugging

### Enable Request/Response Logging

Add this to `appsettings.Development.json` to see detailed HTTP traffic:

```json
{
  "Logging": {
    "LogLevel": {
      "System.Net.Http": "Debug",
      "Microsoft.AspNetCore": "Debug"
    }
  }
}
```

Then check the console output for request/response details.

### Common Issues

1. **"Maxio credentials ... must be configured"** 
   - User-secrets not set. Run the `dotnet user-secrets set` commands above.

2. **"HTTP 404" from Maxio**
   - Product handle (`eshop-pro`, `basic-plan`) not found. Verify seeded entities on site `cp-exp-1`.

3. **"HTTP 422" — Validation error**
   - Product misconfigured (e.g., requires payment method). Confirm seeded products have no payment requirement.

4. **"Unable to read subscription plans" / JsonException**
   - Malformed or unexpected response from Maxio. Check API logs in Maxio console.

5. **Connection timeout (≈30s hang)**
   - Maxio API unreachable. Verify internet access and Maxio sandbox status.

## Architecture Overview

- **MaxioSubscriptionService** (`src/PublicApi/Services/MaxioSubscriptionService.cs`) — Core business logic for Maxio interactions (idempotent customer lookup, subscription creation, error handling).
- **Endpoints** (`src/PublicApi/SubscriptionEndpoints/`) — Three HTTP endpoints following eShopOnWeb's Ardalis.ApiEndpoints pattern.
- **Program.cs** — Configures the Maxio SDK client with DI (HttpClientFactory, per-attempt timeout, retry policy).
- **Data Model** (`src/Infrastructure/Data/UserMaxioCustomer.cs`) — Schema for storing Maxio customer ID ↔ eShopOnWeb user mapping (not yet integrated into DbContext for in-memory dev testing).

## Production Considerations

1. **Persistence** — Currently subscriptions are tracked only in Maxio. For a production deployment, integrate `UserMaxioCustomer` into your EF Core DbContext and migrations to persist customer IDs locally.

2. **Error Handling** — The service catches SDK exceptions and JsonException separately per the dotnet-error-handling skill guidance. Review error responses in `MaxioSubscriptionService.cs` and adjust message clarity as needed.

3. **Webhooks** — Maxio can send webhooks (subscription state changes, payment failures). Implement webhook handlers to keep local state in sync.

4. **Retry Policy** — Default configuration retries on `503`, `502`, `500`, `429`, `408` and transport failures. Non-idempotent writes (subscription creation) may be retried on connection failures — use Maxio's reference field to deduplicate server-side.

5. **Rate Limiting** — Maxio sandbox has rate limits. Monitor 429 responses and adjust retry backoff if needed.

6. **Secrets Management** — User-secrets is a dev-only mechanism. For deployed environments (staging, production), use Azure Key Vault, AWS Secrets Manager, or your infrastructure's secret store.

## Next Steps

1. Integrate `UserMaxioCustomer` entity into your DbContext for production persistence.
2. Add webhook handlers for subscription lifecycle events (renewal, cancellation, dunning).
3. Implement subscription management UI (cancel, change plan, view invoices).
4. Add integration tests using a Maxio sandbox account fixture.
