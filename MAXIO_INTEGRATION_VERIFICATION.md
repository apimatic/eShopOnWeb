# Maxio Subscription Integration - Verification Guide

This guide walks through verifying the Maxio Advanced Billing subscription integration in eShopOnWeb is working correctly.

## Prerequisites

- .NET 8.0+ SDK installed
- Environment variables set:
  - `MAXIO_API_KEY` — Maxio sandbox API key
  - `MAXIO_SITE_SUBDOMAIN` — Maxio site subdomain (e.g., `cp-exp-1`)
  - `MAXIO_ENVIRONMENT` — Maxio environment (defaults to `US`)
  - `MAXIO_DEFAULT_PRODUCT_FAMILY` — Product family handle (e.g., `eshop-subscribe`)
- User secrets configured (see [Configuration](#configuration) below)

## Configuration

User secrets have been configured in the PublicApi project to store Maxio credentials securely:
```bash
cd src/PublicApi
dotnet user-secrets list
```

If secrets are not yet configured, run:
```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

## Build Verification

Verify the solution builds with no errors:
```bash
DOTNET_ROLL_FORWARD=Major dotnet build eShopOnWeb.sln
```

Expected output: `Build succeeded` with 0 errors.

## Running the Application

### Start the PublicApi

The PublicApi includes three new subscription endpoints under the `SubscriptionEndpoints` tag:

1. **Get JWT Token** (required for subsequent calls)
   ```bash
   curl -X POST https://localhost:28823/api/authenticate \
     -H "Content-Type: application/json" \
     -d '{"username":"[email protected]","password":"Pass123$"}' \
     --insecure
   ```
   
   Save the `token` from the response.

2. **List Available Subscription Plans**
   ```bash
   curl https://localhost:28823/api/subscription-plans \
     -H "Authorization: Bearer <TOKEN>" \
     --insecure
   ```
   
   Expected response:
   ```json
   {
     "correlationId": "...",
     "plans": [
       {
         "handle": "eshop-pro",
         "name": "Professional Plan",
         "priceInCents": 29900,
         "interval": 1,
         "intervalUnit": "month",
         "description": "..."
       },
       {
         "handle": "basic-plan",
         "name": "Basic Plan",
         "priceInCents": 2900,
         "interval": 1,
         "intervalUnit": "month",
         "description": "..."
       }
     ]
   }
   ```

3. **Create a Subscription**
   ```bash
   curl -X POST https://localhost:28823/api/subscriptions \
     -H "Authorization: Bearer <TOKEN>" \
     -H "Content-Type: application/json" \
     -d '{"planHandle":"eshop-pro"}' \
     --insecure
   ```
   
   Expected response (201 Created):
   ```json
   {
     "correlationId": "...",
     "subscription": {
       "id": 12345,
       "state": "active",
       "priceInCents": 29900,
       "nextBillingDate": "2026-10-07T...",
       "reference": "user-id-..."
     }
   }
   ```

4. **List User's Subscriptions**
   ```bash
   curl https://localhost:28823/api/my-subscriptions \
     -H "Authorization: Bearer <TOKEN>" \
     --insecure
   ```
   
   Expected response: list of the user's active subscriptions from Maxio.

## Key Design Decisions

### Architecture

- **Subscription Service** (`MaxioSubscriptionService`): Encapsulates all Maxio SDK interactions, handles idempotency, error handling
- **HTTP Endpoints** (`SubscriptionEndpoints`): Three REST endpoints following eShopOnWeb conventions
  - Request/response DTOs extend `BaseRequest`/`BaseResponse` for consistent correlation IDs
  - JWT authentication required on all subscription endpoints
  - User identity extracted from JWT claims (`ClaimTypes.NameIdentifier`)

### Idempotency

- **Customer Creation**: Uses Maxio `Reference` field set to eShopOnWeb user ID
  - Pattern: `ReadCustomerByReference` (404 → `CreateCustomer`)
  - Prevents duplicate customers if called multiple times with same user
- **Subscription Creation**: Searches existing subscriptions before creating
  - Pattern: `ListCustomerSubscriptions` → check for matching plan + active state
  - Prevents duplicate active subscriptions to the same plan

### Error Handling

- SDK errors mapped to HTTP 400 Bad Request with user-friendly messages
- Typed error accessors used per SDK contract (Case A `TryGet*` methods)
- Connection failures and JSON deserialization failures caught and wrapped

### Database Integration

- `Subscription` entity added to ApplicationCore for future persistence
  - Not currently persisted (in-memory DB); can be extended to track subscription state locally
- `SubscriptionConfiguration` provides EF Core mapping

## Testing Checklist

- [ ] Solution builds with `dotnet build`
- [ ] PublicApi runs with `dotnet run`
- [ ] Authentication endpoint returns valid JWT
- [ ] `/api/subscription-plans` lists Pro ($299/mo) and Basic ($29/mo) plans
- [ ] `/api/subscriptions` (POST) creates a new subscription with state `active`
- [ ] Creating the same subscription twice returns an error (idempotency check works)
- [ ] `/api/my-subscriptions` lists the created subscription
- [ ] Subscription has correct `priceInCents` and `nextBillingDate`

## Troubleshooting

### 401 Unauthorized on subscription endpoints
- JWT token expired or invalid
- User ID not found in token claims
- Re-authenticate to get a fresh token

### 422 Unprocessable Entity on create subscription
- Plan handle is invalid
- Customer creation failed validation
- Check Maxio credentials and network access

### 5xx Internal Server Error
- Check application logs for full error details
- Verify environment variables are set correctly
- Ensure Maxio sandbox is accessible

## Production Considerations

When moving to production:
1. Use `ServerEnvironment.Eu` if targeting EU Maxio site
2. Implement per-attempt timeout with `CancellationToken` in controller layer
3. Add structured logging for audit trail
4. Persist subscriptions locally for offline capability
5. Implement retry and circuit-breaker patterns for resilience
6. Add rate limiting on subscription creation endpoint
7. Validate plan handles against local cache to fail-fast
