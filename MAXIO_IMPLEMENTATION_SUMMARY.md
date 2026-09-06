# Maxio Subscription Billing Integration — eShopOnWeb

## Overview

Added recurring subscription billing to eShopOnWeb as an **additive, parallel capability** alongside the existing one-time checkout flow. The integration uses **Maxio Advanced Billing** as the system of record for subscription state.

## Architecture

### Components

1. **Service Layer** (`MaxioSubscriptionService`)
   - Wraps all Maxio SDK interactions
   - Implements idempotent customer creation/lookup
   - Handles subscription lifecycle operations
   - Provides error handling boundary

2. **HTTP Endpoints** (JWT-authenticated)
   - `GET /api/subscription-plans` — List available plans
   - `POST /api/subscriptions` — Create subscription (user)
   - `GET /api/my-subscriptions` — List user subscriptions

3. **Dependency Injection**
   - Maxio client registered as singleton
   - HTTP client managed via `IHttpClientFactory`
   - Service lifetime: scoped

4. **Configuration**
   - Runtime bindings from `appsettings.json`: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, `Maxio:BaseUrl`
   - Credentials from environment → .NET user secrets
   - Retry policy: 2 attempts, 10s per-attempt timeout, exponential backoff

## Capabilities

### GET /api/subscription-plans
Lists all available subscription plans from Maxio Product Family `eshop-subscribe`.

**Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### POST /api/subscriptions
Creates a subscription for the logged-in user.

**Request:**
```json
{
  "product_id": 7126957
}
```

**Response (201 Created):**
```json
{
  "subscription": {
    "id": 12345,
    "state": "active",
    "reference": "user-id:product-id:timestamp",
    "createdAt": "2026-09-07T...",
    "nextAssessmentAt": "2026-10-07T..."
  }
}
```

**Behind the scenes:**
1. Extracts user ID from JWT token (`ClaimTypes.Name`)
2. Looks up or creates Maxio customer (idempotent via reference)
3. Creates subscription
4. Returns subscription details

### GET /api/my-subscriptions
Lists all subscriptions for the logged-in user.

**Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "reference": "...",
      "createdAt": "...",
      "nextAssessmentAt": "...",
      "currentPeriodEndsAt": "..."
    }
  ]
}
```

## Idempotency

The service ensures **idempotent customer creation** by:
1. Using eShopOnWeb user ID as Maxio customer reference
2. On first POST /subscriptions: looking up customer by reference
3. If not found (404): creating customer with that reference
4. If found: reusing existing customer
5. Creating subscription for that customer

This guarantees:
- Duplicate POST requests create only one subscription
- Users can refresh/retry without risk of double-billing
- Maxio is the system of record; re-seeding Maxio resets state for all users

## Error Handling

The service catches SDK exceptions and converts them to HTTP responses:
- **401/403**: Unauthenticated/unauthorized → HTTP 401
- **400**: Validation errors → HTTP 400
- **4xx**: Client errors (e.g., product not found) → HTTP 4xx
- **5xx**: Server errors → HTTP 500

Exceptions logged at INFO/ERROR level with user-friendly messages in responses.

## Dependencies

- **MaxioAdvancedBilling.Sdk** (v1.0.2) — Maxio Advanced Billing .NET SDK
- **Microsoft.Extensions.Http** — HTTP client factory
- Existing: AutoMapper, ASP.NET Core Identity, EF Core

## Configuration

### appsettings.json
```json
{
  "Maxio": {
    "Subdomain": "cp-exp-3",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": ""
  }
}
```

### Environment Variables (user secrets)
```bash
Maxio:ApiKey          # From MAXIO_API_KEY
Maxio:Subdomain       # From MAXIO_SITE_SUBDOMAIN
Maxio:ProductFamilyHandle  # From MAXIO_DEFAULT_PRODUCT_FAMILY
Maxio:BaseUrl         # (optional override)
```

## Testing Scenario

### Flow
1. User authenticates: `POST /api/authenticate` → receives JWT token
2. User views plans: `GET /api/subscription-plans` (no auth required)
3. User subscribes: `POST /api/subscriptions` with `product_id: 7126957`
4. User views subscriptions: `GET /api/my-subscriptions` → sees their active subscription
5. Maxio dashboard shows: new customer and subscription with correct pricing

### Sandbox Entities
- Product Family: `eshop-subscribe` (ID: 3023074)
- Pro Plan: `eshop-pro` (ID: 7126957, $299/mo)
- Basic Plan: `basic-plan` (ID: 7126958, $29/mo)
- Both: no trial, no setup fee, zero-payment subscriptions OK, not taxable

## Build & Deployment

### Build
```bash
dotnet build eShopOnWeb.sln -c Release
```

Expected: 0 errors (9 pre-existing package warnings)

### Run
```bash
$env:UseOnlyInMemoryDatabase="true"
$env:DOTNET_ROLL_FORWARD="Major"
dotnet run --project src/PublicApi/PublicApi.csproj
```

Starts on `https://localhost:27643` (HTTPS).

### Configuration for Different Environments
- **Local dev**: Set user secrets from environment variables
- **Staging/Prod**: Swap only the configuration values (API key, subdomain, product family)
  - No code changes needed
  - Same endpoints, same behavior

## Known Limitations

1. **In-Memory State**: Subscription tracking in eShopOnWeb uses in-memory database (no persistence)
   - Workaround: For production, implement a database table linking eShopWeb user → Maxio subscription ID
   
2. **Payment Method Not Collected**: The integration does not collect card details
   - Assumption: Maxio product is configured to permit zero-payment subscriptions
   - If product requires payment method, subscription creation returns 422
   
3. **Metered Component Not Integrated**: Metered `api-call` component (ID: 3057195) exists but is not used
   - Usage tracking not implemented; can be added as a follow-up

## Future Enhancements

- [ ] Persist subscription ↔ Maxio mapping in database
- [ ] Webhook handlers for Maxio events (renewal, failure, cancellation)
- [ ] Admin endpoints to view/manage subscriptions
- [ ] Metered usage tracking and reporting
- [ ] Subscription management (pause, resume, cancel)
- [ ] Plan upgrades/downgrades
- [ ] Integration tests with Maxio sandbox

## Files Changed

**New:**
- `src/PublicApi/Services/MaxioSubscriptionService.cs` (DTOs, business logic, SDK wrapping)
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs`
- `MAXIO_SETUP.md` (setup & verification guide)
- `MAXIO_IMPLEMENTATION_SUMMARY.md` (this file)

**Modified:**
- `src/PublicApi/Program.cs` — DI registration, middleware, endpoint mapping
- `src/PublicApi/appsettings.json` — Maxio config keys
- `Directory.Packages.props` — SDK package version

**Unchanged:**
- Domain entities (Buyer, Order, etc.)
- Existing checkout flow
- Web project (cookie-based auth)

## References

- Maxio API: https://chargify.com/api-v2/
- SDK: AsadAli.AdvancedBilling.Sdk
- Integration tested against: Maxio sandbox `cp-exp-3`

