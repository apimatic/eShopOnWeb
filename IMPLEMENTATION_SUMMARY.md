# Maxio Subscription Billing Integration - Implementation Summary

## What Was Built

Added complete subscription billing capability to eShopOnWeb using **Maxio Advanced Billing** as the system of record. This is an **additive, parallel capability** alongside the existing one-time cart/checkout flow — no changes to core commerce operations.

## Files Added/Modified

### New Files

1. **`src/PublicApi/MaxioSettings.cs`**
   - Configuration class to hold Maxio API credentials and settings
   - Binds from `Maxio:*` configuration section
   - Loads from environment variables: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT`, `MAXIO_DEFAULT_PRODUCT_FAMILY`

2. **`src/PublicApi/Services/MaxioSubscriptionService.cs`**
   - Core service implementing Maxio operations
   - `ListSubscriptionPlansAsync()` — Fetch and filter products by family
   - `CreateSubscriptionAsync()` — Idempotent subscription creation (customer + subscription)
   - `ListCustomerSubscriptionsAsync()` — Retrieve user's subscriptions
   - Private helper: `LookupOrCreateCustomerAsync()` — Idempotent customer management

3. **`src/PublicApi/SubscriptionEndpoints/SubscriptionPlansListEndpoint.cs`**
   - `GET /api/subscription-plans` — List available plans (requires JWT auth)
   - Returns plan handle, name, description, price, and billing interval

4. **`src/PublicApi/SubscriptionEndpoints/SubscriptionsCreateEndpoint.cs`**
   - `POST /api/subscriptions` — Create subscription (requires JWT auth)
   - Request: `{ productHandle: string }`
   - Response: subscription ID, state, billing dates, price

5. **`src/PublicApi/SubscriptionEndpoints/SubscriptionsListEndpoint.cs`**
   - `GET /api/my-subscriptions` — Retrieve user's subscriptions (requires JWT auth)
   - Returns all subscriptions for authenticated user with state and billing details

6. **`SUBSCRIPTION_INTEGRATION_GUIDE.md`**
   - Step-by-step verification guide with curl examples
   - Tests all three endpoints and error cases
   - Production readiness checklist

### Modified Files

1. **`Directory.Packages.props`**
   - Added `AsadAli.AdvancedBilling.Sdk` version 1.0.0 package

2. **`src/PublicApi/PublicApi.csproj`**
   - Added package reference to Maxio SDK

3. **`src/PublicApi/appsettings.json`**
   - Added `Maxio` configuration section with default values

4. **`src/PublicApi/Program.cs`**
   - Imported Maxio namespaces
   - Configured environment variable loading
   - Set up Maxio client DI registration
   - Configured HTTP client with resilience (retries, timeouts, connection pooling)
   - Registered `MaxioSubscriptionService` as scoped service

## Architecture & Design

### Client Initialization

- **Singleton** `MaxioAdvancedBillingClient` registered via DI
- **Named HTTP client** (`MaxioClient`) with dedicated socket handler
- **Connection pooling** with 5-minute lifetime (handles DNS changes)
- **Per-attempt timeout** of 30 seconds
- **Retry policy**: 3 max retries with exponential backoff (1s, 2s, 4s)
- **Auth**: HTTP Basic with API key (username) and "x" (password)

### Idempotency

**Customer**: Lookup by reference (user ID) before create. On duplicate, use existing customer.

**Subscription**: Lookup by reference (`{userId}:{productHandle}`) before create. On retry, return existing subscription.

This prevents duplicate customers and subscriptions even if API calls are retried.

### Error Handling

- **Typed errors** (Case A): `SdkException<{Operation}Error>` with `TryGet*` accessors
  - Example: `CreateCustomerError.TryGetCustomerErrorResponse1()` for 422 validation
- **Untyped errors** (Case B): `SdkException<RawError>` for simple status-code responses
  - Example: Product lookup returns `SdkException<RawError>` on 404
- **All exceptions** caught and mapped to HTTP error responses with user-safe messages
- **Transport failures** (connection reset, timeout) treated same as API errors

### Configuration

| Setting | Source | Default | Purpose |
|---------|--------|---------|---------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` env var | — | API authentication |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` env var | — | Site subdomain (e.g., `cp-exp-1`) |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` env var | `US` | Server region (US or EU) |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` env var | `eshop-subscribe` | Product family to filter |
| `Maxio:BaseUrl` | `MAXIO_BASE_URL` env var (optional) | Derived from subdomain | Override base URL |

All credentials load from environment variables — no secrets in code.

## API Endpoints

### `GET /api/subscription-plans`

**Authentication:** Required (JWT Bearer token)

**Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan",
      "priceInCents": 29900,
      "intervalUnit": "month"
    }
  ]
}
```

### `POST /api/subscriptions`

**Authentication:** Required (JWT Bearer token)

**Request:**
```json
{
  "productHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productHandle": "eshop-pro",
  "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
  "nextAssessmentAt": "2026-10-06T00:00:00Z",
  "productPriceInCents": 29900
}
```

### `GET /api/my-subscriptions`

**Authentication:** Required (JWT Bearer token)

**Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
      "nextAssessmentAt": "2026-10-06T00:00:00Z",
      "productPriceInCents": 29900
    }
  ]
}
```

## Maxio Sandbox Setup

The integration targets the **Maxio sandbox** (`cp-exp-1`) with seeded data:

| Entity | Handle | ID | Details |
|--------|--------|----|---------| 
| Product Family | `eshop-subscribe` | 3023074 | Container for plans |
| Pro Plan | `eshop-pro` | 7126957 | $299.00/mo, no trial, no payment required |
| Basic Plan | `basic-plan` | 7126958 | $29.00/mo, no trial, no payment required |
| Metered Component | `api-call` | 3057195 | $0.01/unit, for usage-based billing |

Plans never expire, are not taxable, and do not require payment method at signup (all configured on the family).

## Build & Test

### Prerequisites
- .NET 8 SDK (project pins 8.0.x, rolls forward to latest 8)
- HTTPS dev cert (for localhost:5001 and localhost:24343)
- Environment variables set (see Configuration section)

### Build
```bash
dotnet build eShopOnWeb.sln
```
Result: **All projects build successfully with 0 errors.**

### Run
```bash
# Start PublicApi
cd src/PublicApi
dotnet run --launch-profile=https

# In another terminal, run verification tests
# See SUBSCRIPTION_INTEGRATION_GUIDE.md for curl examples
```

## Production Grade Features

1. **Resilience**: Automatic retries with exponential backoff, per-attempt timeout
2. **Idempotency**: Reference-based lookups prevent duplicate customers/subscriptions
3. **Security**: Credentials from env vars only, JWT auth on all endpoints, HTTPS required
4. **Error Handling**: Typed exceptions with detailed error codes, user-safe messages
5. **Observability**: SDK client configured for HTTP logging (via custom handler pattern)
6. **Scalability**: Singleton client with connection pooling, scoped service layer

## Non-Breaking

The subscription capability is **completely isolated**:
- New endpoints under `/api/subscriptions` and `/api/subscription-plans` (no conflicts)
- New service layer (`MaxioSubscriptionService`) separate from existing services
- No changes to existing cart, order, or catalog APIs
- Existing tests unaffected (no modifications to ApplicationCore, Infrastructure, or Web)

## Known Limitations & Next Steps

1. **Payment Method**: Currently not required. If payment capture is added later, enhance `CreateSubscription` request to include card/bank details.
2. **Webhooks**: Maxio sends subscription state-change webhooks that could update UI or trigger actions (not implemented here).
3. **Metered Billing**: The sandbox includes a metered component (`api-call`) for usage-based billing; usage reporting is not yet wired.
4. **Frontend**: UI components to browse and subscribe not included (integration layer only).
5. **Billing History**: View past invoices/charges not exposed (future enhancement).

## Verification Checklist

- [x] Solution builds with no errors
- [x] All three endpoints compile and run
- [x] Maxio SDK client properly initialized and configured
- [x] Environment variables loaded and no secrets hardcoded
- [x] Idempotency implemented for customers and subscriptions
- [x] Error handling covers both typed and untyped SDK exceptions
- [x] JWT authentication enforced on all subscription endpoints
- [x] HTTP client configured with resilience (retries, timeouts, pooling)
- [x] Comprehensive verification guide provided with curl examples
- [x] Production-grade error messages (user-safe, no internal details)

## Testing the Integration

Follow **SUBSCRIPTION_INTEGRATION_GUIDE.md** for:
1. Get authentication token
2. List subscription plans
3. Create subscription (Pro Plan)
4. Retrieve user's subscriptions
5. Create second subscription (Basic Plan)
6. Verify idempotency (retry same subscription)
7. Test error cases (missing auth, invalid product, etc.)

All steps include expected responses and verification checks.
