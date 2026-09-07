# Maxio Subscription Billing Integration - Verification Guide

## Overview
The eShopOnWeb application now includes a parallel recurring-subscription billing capability using **Maxio Advanced Billing** as the system of record. This is an additive feature that coexists with the existing one-time commerce flow.

## Implementation Summary

### New Components Created

1. **Entities**
   - `src/ApplicationCore/Entities/SubscriptionAggregate/Subscription.cs` - Stores subscription records tied to users

2. **Services**
   - `src/Infrastructure/Services/MaxioClientService.cs` - HTTP client for Maxio API interaction with basic auth
   - `src/Infrastructure/Services/SubscriptionService.cs` - Business logic for subscription management

3. **API Endpoints** (JWT-authenticated)
   - `GET /api/subscription-plans` - Lists available subscription plans from Maxio
   - `POST /api/subscriptions` - Creates a subscription for the authenticated user
   - `GET /api/my-subscriptions` - Returns user's active subscriptions

4. **Database**
   - Added `Subscriptions` DbSet to `CatalogContext` for in-memory and SQL Server persistence

### Configuration

**Environment Variables** (read automatically at startup):
- `MAXIO_API_KEY` - Maxio sandbox API key
- `MAXIO_SITE_SUBDOMAIN` - Maxio site subdomain (e.g., `cp-exp-2`)
- `MAXIO_ENVIRONMENT` - Maxio region: `US` (chargify.com) or `EU` (ebilling.maxio.com)
- `MAXIO_DEFAULT_PRODUCT_FAMILY` - Default product family handle (optional, for future use)

**Optional Configuration**:
- `MAXIO_BASE_URL` - Override the computed base URL (env var or appsettings.json)

Configuration is loaded from:
1. Environment variables (highest priority)
2. `appsettings.json` under `Maxio:` section
3. Default fallback for environment (`US` if not specified)

## Verification Steps

### Prerequisites
1. .NET 8 SDK installed
2. Environment variables set:
   - `MAXIO_API_KEY=<your-key>`
   - `MAXIO_SITE_SUBDOMAIN=cp-exp-2`
   - `MAXIO_ENVIRONMENT=US`
3. `UseOnlyInMemoryDatabase=true` (for local testing without LocalDB)
4. `DOTNET_ROLL_FORWARD=Major` (to allow .NET 8 to run with 10.0 SDK if needed)

### Build the Solution
```bash
cd C:\path\to\repo
dotnet build eShopOnWeb.sln
```
Expected: Build succeeds with 0 errors, 8 warnings (pre-existing package vulnerabilities)

### Start the PublicApi Server
```bash
cd src/PublicApi
set UseOnlyInMemoryDatabase=true
set DOTNET_ROLL_FORWARD=Major
dotnet run --no-build
```
Expected: API starts on `https://localhost:28443` and `http://localhost:28444`

### Run the Integration Tests

#### 1. Authenticate
```bash
curl -X POST https://localhost:28443/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  -k
```
Response: JWT token in `token` field
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

#### 2. Get Subscription Plans
```bash
curl -X GET https://localhost:28443/api/subscription-plans \
  -H "Authorization: Bearer <TOKEN>" \
  -k
```
Response: Array of available plans
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "...",
      "priceInDollars": 299.00,
      "billingInterval": 1,
      "billingIntervalUnit": "month"
    },
    ...
  ]
}
```

#### 3. Create a Subscription
```bash
curl -X POST https://localhost:28443/api/subscriptions \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k
```
Response: Subscription details
```json
{
  "id": 1,
  "maxioSubscriptionId": 12345678,
  "planHandle": "eshop-pro",
  "planName": "Pro Plan",
  "priceInDollars": 299.00,
  "state": "active",
  "currentPeriodEndsAt": "2026-10-07T...",
  "nextAssessmentAt": "2026-10-07T...",
  "createdAt": "2026-09-07T..."
}
```

#### 4. Get My Subscriptions
```bash
curl -X GET https://localhost:28443/api/my-subscriptions \
  -H "Authorization: Bearer <TOKEN>" \
  -k
```
Response: Array of user's subscriptions
```json
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 12345678,
      "planHandle": "eshop-pro",
      "planName": "Pro Plan",
      "priceInDollars": 299.00,
      "state": "active",
      "currentPeriodEndsAt": "2026-10-07T...",
      "nextAssessmentAt": "2026-10-07T...",
      "createdAt": "2026-09-07T..."
    }
  ]
}
```

## Key Design Decisions

1. **Manual JWT Claims Extraction**: Endpoints manually extract user ID from JWT claims because the minimal API middleware handles token validation differently than traditional controllers.

2. **Idempotent Customer Creation**: Each subscription creation attempts to create a Maxio customer with the user ID as the reference. Maxio enforces uniqueness, so duplicate subscription attempts for the same user fail gracefully at the API level.

3. **In-Memory Database**: During development/testing, subscriptions are stored in an in-memory EF Core database that persists only within a single run. For production, configure SQL Server in `appsettings.json`.

4. **Maxio as System of Record**: The `Subscription` entity acts as a local cache/mirror of Maxio subscriptions. The authoritative state lives in Maxio; the local database is for quick access and audit trails.

5. **No Trial/Setup Fees**: Sandbox plans are configured for immediate billing with no trial periods or setup fees, simplifying the subscription flow.

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| `InvalidOperationException: MAXIO_SITE_SUBDOMAIN...required` | Environment variable not set | Verify env vars are set before starting dotnet |
| `System.Text.Json.JsonException` on `/subscription-plans` | Maxio response format mismatch | Check Maxio API response; may need to deserialize differently |
| `401 Unauthorized` on endpoints | JWT token not valid | Regenerate token; check expiration (currently 7 days) |
| API redirects HTTP to HTTPS | Expected behavior | Use `https://localhost:28443` in URLs |

## Production Readiness Checklist

- [ ] Configure SQL Server connection strings in `appsettings.json` (remove in-memory database)
- [ ] Use environment-specific secrets management (Azure Key Vault, AWS Secrets Manager, etc.) instead of env vars
- [ ] Extend `MaxioClientService` with retry logic and rate-limiting
- [ ] Implement webhook handlers for Maxio events (subscription status changes, failed payments, etc.)
- [ ] Add comprehensive logging/observability
- [ ] Implement subscription cancellation endpoint
- [ ] Add subscription plan management (admin endpoints to sync/update plans from Maxio)
- [ ] Audit logs for subscription lifecycle events
- [ ] Compliance review for PCI DSS (Maxio handles card data; we don't accept it directly)

## Testing Checklist

- [x] Endpoints return correct HTTP status codes
- [x] JWT authentication works on all subscription endpoints
- [x] Subscription creation stores data locally
- [x] My subscriptions returns only user's subscriptions
- [x] Error handling for malformed requests
- [ ] Integration tests with Maxio sandbox
- [ ] Load testing for concurrent subscriptions
- [ ] Maxio webhook validation and processing

## Architecture Notes

**Layering**:
- API Endpoints (PublicApi/SubscriptionEndpoints) → Business Logic (SubscriptionService) → Data Access (CatalogContext + MaxioClientService)

**Separation of Concerns**:
- MaxioClientService: HTTP communication only
- SubscriptionService: Maxio business logic and local persistence
- Endpoints: Request/response handling and user context

**Future Extensibility**:
- Add `ISubscriptionProvider` interface to allow swapping billing providers
- Implement subscription components/metering for usage-based billing
- Add subscription plan versioning for price/terms changes
