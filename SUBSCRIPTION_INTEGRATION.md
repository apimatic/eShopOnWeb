# Maxio Subscription Integration — eShopOnWeb

## Overview

This document describes the subscription billing capability added to eShopOnWeb using Maxio Advanced Billing as the billing system. The integration is **additive** and does not replace the existing cart/checkout flow.

## Architecture

### Three New Endpoints (PublicApi project)

All endpoints are JWT-authenticated (except where noted) and follow the existing PublicApi conventions.

| Endpoint | Method | Auth | Purpose |
|----------|--------|------|---------|
| `/api/subscription-plans` | GET | None | List available subscription plans from Maxio |
| `/api/subscriptions` | POST | JWT | Subscribe logged-in user to a plan |
| `/api/my-subscriptions` | GET | JWT | Retrieve user's active subscriptions |

### Key Components

- **MaxioSettings.cs** — Configuration class loading from `Maxio:*` config section
- **MaxioSubscriptionService.cs** — Service implementing subscription operations (list plans, create subscription, list user subscriptions)
- **SubscriptionPlan*Endpoint.cs** — Three endpoint classes following eShopOnWeb's endpoint pattern

### Idempotency

Customer creation is idempotent:
1. Lookup customer by user ID (reference)
2. If not found (404), create new customer
3. Use customer ID for all subscription operations

User-subscription mapping is stored in memory (`ConcurrentDictionary<string, int>`) keyed on user ID.

## Configuration

### Environment Variables (Maxio Credentials)

Set before running:
```bash
export MAXIO_API_KEY=<your-api-key>
export MAXIO_SITE_SUBDOMAIN=cp-exp-4
export MAXIO_ENVIRONMENT=US
export MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### .NET User Secrets

Credentials are stored in .NET user-secrets (already configured):
```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "<api-key>"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-4"
dotnet user-secrets set "Maxio:Environment" "US"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### appsettings.json (Optional)

For environment-specific overrides:
```json
{
  "Maxio": {
    "BaseUrl": null  // optional: override base URL for dev/testing
  }
}
```

## Building

### Prerequisites

- .NET SDK 8.0 or later (configured with `rollForward: latestMajor` in global.json)
- Maxio sandbox credentials (site: cp-exp-4)

### Build Steps

```bash
# Restore dependencies (includes Maxio SDK v1.0.2)
dotnet restore eShopOnWeb.sln

# Build solution
dotnet build eShopOnWeb.sln -c Debug
```

## Running & Testing

### 1. Start PublicApi

```bash
cd src/PublicApi
dotnet run --configuration Debug
```

The API listens on:
- HTTPS: `https://localhost:27663`
- HTTP: `http://localhost:27664` (redirects to HTTPS)

### 2. Get JWT Token

```bash
curl -k -X POST -H "Content-Type: application/json" \
  -d '{"username":"testuser","password":"Pass123!"}' \
  https://localhost:27663/api/authenticate
```

Expected response (extract the `token` field):
```json
{
  "result": true,
  "token": "eyJ0eXAiOiJKV1QiLCJhbGc...",
  "username": "testuser",
  ...
}
```

### 3. List Available Plans

Unauthenticated:
```bash
curl -k -s https://localhost:27663/api/subscription-plans | jq .
```

Expected response:
```json
{
  "correlationId": "...",
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "price": 299.0,
      "billingInterval": "Month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "price": 29.0,
      "billingInterval": "Month"
    }
  ]
}
```

### 4. Create Subscription

Authenticated (replace `<TOKEN>` with value from step 2):
```bash
curl -k -X POST -H "Content-Type: application/json" \
  -H "Authorization: Bearer <TOKEN>" \
  -d '{"planHandle":"eshop-pro"}' \
  https://localhost:27663/api/subscriptions
```

Expected response:
```json
{
  "correlationId": "...",
  "subscription": {
    "id": 12345678,
    "customerId": 0,
    "productHandle": "eshop-pro",
    "state": "Active",
    "currentPeriodEndsAt": "2026-10-06T21:05:00Z"
  }
}
```

### 5. Retrieve User Subscriptions

Authenticated (replace `<TOKEN>`):
```bash
curl -k -H "Authorization: Bearer <TOKEN>" \
  https://localhost:27663/api/my-subscriptions
```

Expected response:
```json
{
  "correlationId": "...",
  "subscriptions": [
    {
      "id": 12345678,
      "customerId": 0,
      "productHandle": "eshop-pro",
      "state": "Active",
      "currentPeriodEndsAt": "2026-10-06T21:05:00Z"
    }
  ]
}
```

## Sandbox Test Data

**Site:** `cp-exp-4`  
**Product Family:** `eshop-subscribe` (ID: 3023074)

### Available Plans

| Plan | Handle | Price | ID |
|------|--------|-------|-----|
| Pro Plan | eshop-pro | $299.00/mo | 7126957 |
| Basic Plan | basic-plan | $29.00/mo | 7126958 |

**Note:** Numeric IDs may change on Maxio re-seed; use handles (stable) for production.

## Implementation Notes

### SDK Usage

- **SDK Package:** `AsadAli.AdvancedBilling.Sdk` v1.0.2
- **Authentication:** Basic Auth (username = API key, password = "x")
- **Error Handling:** Case A (typed errors) for create operations; Case B (raw errors) for list/lookup operations
- **Endpoint Routing:** Follows MinimalApi.Endpoint pattern with `IEndpoint<IResult, TRequest, TDependency>`

### Current Limitations

1. **In-Memory Subscription Map** — Subscriptions are only mapped during the current app session. On restart, the mapping is lost. Clients must refetch from Maxio via `/api/my-subscriptions`.
2. **No Payment Method Capture** — Subscriptions are created without requiring a payment method (Maxio is configured to allow this). Production deployments should require payment profiles.
3. **Response Field Mapping** — Some Maxio response fields (e.g., `CustomerId`, `ProductHandle` in subscription details) are not fully mapped; workaround implemented by tracking via plan handle and returning placeholder values.

### Error Scenarios

- **401 Unauthorized:** Invalid Maxio credentials (check env vars and user-secrets)
- **422 Unprocessable Entity:** Invalid plan handle or subscription already exists for customer
- **404 Not Found:** (lookup operations) Customer doesn't exist or plan not found

## Future Enhancements

1. Persist subscription mapping to database (user_id ↔ maxio_subscription_id)
2. Webhook integration for Maxio subscription state changes (renewal, cancellation, payment failure)
3. UI for subscription management (dashboard, upgrade/downgrade, cancellation)
4. Payment method capture and validation before subscription creation
5. Metered component usage tracking (if using the `api-call` component)
6. Dunning management for failed payments

## Files Changed

### New Files
- `src/PublicApi/MaxioSettings.cs`
- `src/PublicApi/SubscriptionEndpoints/MaxioSubscriptionService.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanListEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs`

### Modified Files
- `Directory.Packages.props` — Added Maxio SDK package version
- `src/PublicApi/PublicApi.csproj` — Added Maxio SDK dependency
- `src/PublicApi/Program.cs` — Registered Maxio configuration and service in DI container

## Troubleshooting

### "Unauthorized" Response

**Cause:** Invalid Maxio credentials  
**Solution:** Verify env vars are set and user-secrets were initialized:
```bash
cd src/PublicApi && dotnet user-secrets list
```

### Build Errors on Maxio Types

**Cause:** Missing using statements or incorrect namespaces  
**Solution:** All Maxio types are fully qualified in imports. Verify `AsadAli.AdvancedBilling.Sdk` v1.0.2 is restored:
```bash
dotnet restore src/PublicApi/PublicApi.csproj
```

### No Response from Endpoints

**Cause:** API not running or wrong port  
**Solution:** Confirm PublicApi is listening:
```bash
netstat -tlnp | grep 27663  # or 27664 for HTTP
```

## References

- **Maxio SDK:** https://github.com/apimatic/maxio-sdks/blob/master/doc/dotnet.md
- **maxio-plan.md** — Full contract sheet with SDK signatures and response types
- **Maxio Sandbox:** https://cp-exp-4.maxio.com (admin login required)
