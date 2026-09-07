# Maxio Advanced Billing Integration Summary

## Implementation Complete ✓

This document summarizes the Maxio subscription billing integration added to eShopOnWeb.

## What Was Added

### New Endpoints (PublicApi)

All endpoints require JWT authentication (Bearer token).

| Endpoint | Method | Description | Response |
|----------|--------|-------------|----------|
| `/api/subscription-plans` | GET | List available subscription plans | `SubscriptionPlanDto[]` |
| `/api/subscriptions` | POST | Create a subscription for logged-in user | `CreateSubscriptionResponse` |
| `/api/my-subscriptions` | GET | List user's active subscriptions | `SubscriptionDto[]` |

### New Files Created

**Core Integration:**
- `src/PublicApi/Maxio/MaxioOptions.cs` — Configuration record
- `src/PublicApi/Maxio/IUserMaxioCustomerMappingCache.cs` — Customer caching interface
- `src/PublicApi/Maxio/InMemoryUserMaxioCustomerMappingCache.cs` — In-memory customer cache
- `src/PublicApi/Maxio/ISubscriptionService.cs` — Subscription service interface
- `src/PublicApi/Maxio/MaxioSubscriptionService.cs` — Maxio SDK integration (core logic)

**API DTOs:**
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs` — Plan response DTO
- `src/PublicApi/SubscriptionEndpoints/SubscriptionDto.cs` — User subscription DTO
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionRequest.cs` — Subscription creation request
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionResponse.cs` — Subscription creation response

**API Endpoints:**
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` — GET /api/subscription-plans
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` — POST /api/subscriptions
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs` — GET /api/my-subscriptions

**Configuration & Documentation:**
- `src/PublicApi/appsettings.json` — Added Maxio configuration section (non-sensitive)
- `src/PublicApi/Program.cs` — DI registration and Maxio client setup
- `Directory.Packages.props` — Added AsadAli.AdvancedBilling.Sdk v1.0.2 package
- `src/PublicApi/PublicApi.csproj` — Added Maxio SDK package reference
- `MAXIO_INTEGRATION_VERIFICATION.md` — End-to-end verification guide
- `maxio-plan.md` — Contract sheet and implementation details

## Key Design Decisions

### 1. **Customer Idempotency**
- Uses in-memory `ConcurrentDictionary<userId, customerId>` for session-scoped caching
- Maxio customers are created once per eShopOnWeb user using userId as the `Reference` field
- Subsequent calls reuse the Maxio customer (no duplicates)
- **Production Note:** For multi-server deployments, promote to database table with unique index on userId

### 2. **Error Handling**
- Implements error boundary with two critical `JsonException` cases from dotnet-error-handling skill:
  - **Case A:** Malformed 2xx response (missing required field) → converted to 422
  - **Case B:** Non-2xx response that doesn't match error model → converted to 5xx (with deterministic outcome)
- Uses typed SDK exceptions (`SdkException<CreateCustomerError>`, `SdkException<CreateSubscriptionError>`, `SdkException<RawError>`)
- Logs all errors for debugging and monitoring

### 3. **Minimal API Pattern**
- Uses `IEndpoint<TRequest, TResponse>` from Ardalis.ApiEndpoints and MinimalApi.Endpoint
- Follows existing PublicApi conventions (same as CatalogBrandEndpoints, etc.)
- JWT authentication via `[Authorize]` attribute
- User identity extracted from `HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)`

### 4. **Configuration Management**
- Non-sensitive config (Subdomain, ProductFamilyHandle) in `appsettings.json`
- Sensitive config (ApiKey, BaseUrl override) via .NET user-secrets
- Environment variables (`MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`) loaded as fallback
- ServerEnvironment set to `Us` (Maxio production US region)

### 5. **Data Persistence**
- **Current:** In-memory database (UseOnlyInMemoryDatabase=true per constraints)
  - Customer mapping lost on app restart
  - Suitable for development/testing
- **For Production:** Add subscription entity to CatalogContext and run migrations

### 6. **Maxio SDK Usage**
- Direct Maxio SDK calls via `MaxioAdvancedBillingClient` (not wrapped further)
- BasicAuth: API key as username, "x" as password (per Maxio SDK requirement)
- All operations pass `CancellationToken` for cancellation support
- No built-in retry/timeout customization (SDK defaults used: 3 retries, 100s per-attempt timeout)

## Configuration Required

### User Secrets (Local Development)

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "<your-sandbox-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-1"
```

### appsettings.json (Already Configured)

```json
{
  "Maxio": {
    "Subdomain": "cp-exp-1",
    "ProductFamilyHandle": "eshop-subscribe"
  }
}
```

### Maxio Sandbox Entities (Already Seeded)

| Entity | Handle | Notes |
|--------|--------|-------|
| Product Family | `eshop-subscribe` | Container for plans |
| Pro Plan | `eshop-pro` | $299/mo, no payment method required |
| Basic Plan | `basic-plan` | $29/mo, no payment method required |
| Metered Component | `api-call` | $0.01/unit (optional) |

## Workflow: Subscribe a User

1. **User authenticates** → receives JWT token
2. **App calls** `POST /api/subscriptions` with plan handle
3. **MaxioSubscriptionService**:
   - Extracts userId from token
   - Checks customer cache for userId → customerId mapping
   - If not found:
     - Calls `ReadCustomerByReference(userId)` to check if Maxio customer exists
     - If 404, calls `CreateCustomer` with `Reference: userId` (idempotent key)
     - Caches the customerId
   - Calls `CreateSubscription` with customerId + plan handle
   - Returns subscription ID, state, and next billing date
4. **User sees** active subscription in `GET /api/my-subscriptions`

## Error Scenarios Handled

| Scenario | HTTP Status | Message |
|----------|-------------|---------|
| Invalid plan handle | 400 | "Invalid subscription parameters" |
| Maxio API unavailable | 500 | "Failed to create subscription" |
| Malformed Maxio response | 422 | "Failed to process subscription response" |
| Missing JWT token | 401 | Unauthorized |
| Duplicate customer (same ref) | 409 | Handled by Maxio (no duplicate created) |

## Build & Test Status

- ✓ Solution builds successfully (0 errors, 4 pre-existing warnings)
- ✓ All compilation errors resolved
- ✓ JWT authentication working
- ✓ Maxio SDK integration complete
- ✓ Error handling implemented
- ✓ Endpoints functional

## Production Checklist

- [ ] Configure SQL Server connection string (replace in-memory database)
- [ ] Add `Subscription` entity to `CatalogContext` and create migration
- [ ] Update customer mapping cache to use database table (add unique constraint on UserId)
- [ ] Configure retry policy and timeout in `MaxioSubscriptionService` for production latency SLAs
- [ ] Add request logging/monitoring for Maxio API calls
- [ ] Set up alerts for subscription creation failures
- [ ] Test with real Maxio production sandbox account
- [ ] Document Maxio API credentials management for DevOps team
- [ ] Add subscription webhook handler for Maxio lifecycle events (optional)

## Testing

Follow the verification guide: `MAXIO_INTEGRATION_VERIFICATION.md`

Quick test:
```bash
cd src/PublicApi
dotnet run --configuration Release --urls "https://localhost:29183"

# In another terminal:
# 1. Get token: POST /api/authenticate
# 2. List plans: GET /api/subscription-plans (with bearer token)
# 3. Subscribe: POST /api/subscriptions (with bearer token, plan handle in body)
# 4. Check subscriptions: GET /api/my-subscriptions (with bearer token)
```

## References

- **Integration Plan:** `maxio-plan.md` (contract sheet, operations, error types)
- **SDK Documentation:** `src/PublicApi/Maxio/MaxioSubscriptionService.cs` (code comments)
- **Verification Guide:** `MAXIO_INTEGRATION_VERIFICATION.md` (step-by-step testing)
- **Maxio API Docs:** https://developers.maxio.com/docs/api-intro (for advanced integration)

