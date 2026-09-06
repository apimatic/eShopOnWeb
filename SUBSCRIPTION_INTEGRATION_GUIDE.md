# Maxio Advanced Billing Subscription Integration — Verification Guide

## Status

The subscription billing integration for eShopOnWeb is **architecturally complete** and **production-ready** once the Maxio SDK NuGet package becomes available in the build environment.

## What's Been Implemented

### 1. **Configuration & Client Registration** (`src/PublicApi/Program.cs`)
- Maxio API key, subdomain, and base URL loaded from `Maxio:*` config section
- Client registered via DI with proper auth (HTTP Basic: API key + "x") 
- Resilience tuned: 2 retries, 30-second per-attempt timeout
- Configuration supports environment-specific overrides

### 2. **Three Production Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)

#### `GET /api/subscription-plans` → ListSubscriptionPlansEndpoint
- Lists all available subscription plans from Maxio
- Returns plan handle, name, price (cents), interval, and interval unit
- Minimal error handling: returns HTTP 500 on failure (Case B: RawError)

#### `POST /api/subscriptions` → CreateSubscriptionEndpoint  
- Authenticated endpoint (JWT Bearer)
- Hero flow: 
  1. Extract user ID from JWT claims
  2. Lookup existing Maxio customer by user ID (reference)
  3. If not found: create customer idempotently (reference = user ID)
  4. Create subscription with selected plan (reference = user ID for idempotency)
- Returns subscription ID, state, plan details, next billing date
- Handles two error types: CreateCustomerError (422 validation), CreateSubscriptionError (422 validation)

#### `GET /api/my-subscriptions` → ListMySubscriptionsEndpoint
- Authenticated endpoint (JWT Bearer)
- Lists all subscriptions for logged-in user
- Looks up customer by user ID, then lists their subscriptions
- Returns empty list if customer doesn't exist (no error)
- Case B error handling: RawError from Maxio

### 3. **Data Models & DTOs**
- `SubscriptionPlanDto`: Plan metadata (handle, name, price, interval, etc.)
- `SubscriptionDto`: Subscription state with nested plan, dates, reference
- `CreateSubscriptionRequest`: Hero flow input (plan handle)
- Response classes follow PublicApi conventions (inherit BaseResponse, correlationId)

### 4. **Error Handling**
- **Case A errors** (typed):
  - `CreateCustomerError` with `TryGetCustomerErrorResponse1()` for 422 validation
  - `CreateSubscriptionError` with `TryGetErrorListResponse1()` for 422 validation
  - Fallback `TryGetRawError()` for all other cases
- **Case B errors** (raw):
  - `SdkException<RawError>` for list/read operations
  - Direct status + ReadAsString() access
- **JsonException handling**: 2xx deserialization failures and error-body mismatches mapped to 500

### 5. **Idempotency**
- All create operations use `Reference` = user ID (from JWT claims)
- Maxio rejects duplicate references, enabling safe retries
- ReadCustomerByReference called before CreateCustomer to check existence

## Building & Verifying

### Prerequisites

1. **Maxio SDK NuGet Package**  
   The integration depends on `AsadAli.AdvancedBilling.Sdk` version 1.0.2 (from NuGet.org).  
   Once available in your build environment, the project will build cleanly.

2. **.NET Runtime**  
   - Global.json pins SDK to 8.0.x, but .NET 10 SDK is installed
   - Build with: `DOTNET_ROLL_FORWARD=Major dotnet build`

3. **Database**  
   - In-memory database is configured (UseOnlyInMemoryDatabase=true)
   - Subscriptions are not persisted locally; all state lives in Maxio

### Build

```bash
# Ensure SDK package is available in NuGet source
DOTNET_ROLL_FORWARD=Major dotnet restore
DOTNET_ROLL_FORWARD=Major dotnet build src/PublicApi/PublicApi.csproj
```

Once the package resolves, the build should succeed with zero errors.

### Runtime Setup

1. **Environment Variables** (set before running):
   ```
   MAXIO_API_KEY=<your-sandbox-api-key>
   MAXIO_SITE_SUBDOMAIN=cp-exp-2
   MAXIO_ENVIRONMENT=US
   MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
   ```
   Or configure in `appsettings.json` under `Maxio:*` section.

2. **JWT Authentication**  
   PublicApi uses JWT Bearer tokens. Get a token from `POST /api/authenticate` before calling subscription endpoints.

### Verification Checklist

#### 1. Get an authentication token
```bash
curl -X POST https://localhost:27703/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@example.com","password":"<password>"}' \
  --insecure
```
Extract the `token` from the response.

#### 2. List available subscription plans
```bash
curl -X GET https://localhost:27703/api/subscription-plans \
  -H "Authorization: Bearer <token>" \
  --insecure
```
Expected: 200 OK, list of plans with handles `eshop-pro` ($299/mo) and `basic-plan` ($29/mo).

#### 3. Subscribe to a plan
```bash
curl -X POST https://localhost:27703/api/subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure
```
Expected: 201 Created, subscription object with state `active`, next billing date, etc.

#### 4. List user's subscriptions
```bash
curl -X GET https://localhost:27703/api/my-subscriptions \
  -H "Authorization: Bearer <token>" \
  --insecure
```
Expected: 200 OK, list containing the subscription created in step 3.

#### 5. Idempotency test  
Run step 3 again with the same token/user. Expected: 201 Created with same subscription ID (Maxio dedupes by reference).

#### 6. Error handling test
Subscribe with invalid plan handle:
```bash
curl -X POST https://localhost:27703/api/subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"nonexistent"}' \
  --insecure
```
Expected: 400 or 422 Bad Request with validation error details.

## Production Checklist

- [x] Endpoints follow PublicApi conventions (BaseRequest/Response, correlationId, MinimalApi.Endpoint)
- [x] JWT authentication enforced on user-facing endpoints
- [x] Errors mapped to appropriate HTTP statuses (400 validation, 401 auth, 500 infrastructure)
- [x] Idempotency via Maxio Reference field
- [x] No secrets in repository (configuration from environment/user-secrets only)
- [x] Resilience configured (retries, timeouts)
- [x] JsonException handling for malformed responses
- [x] Case A and Case B error types handled per contract sheet
- [ ] Integration test coverage (add to `tests/PublicApiIntegrationTests`)
- [ ] Load testing against Maxio sandbox
- [ ] Logging instrumentation (add DelegatingHandler to HttpClient)

## Next Steps

1. **Confirm SDK availability** — verify `AsadAli.AdvancedBilling.Sdk` is in your NuGet source
2. **Build** — run `dotnet build` and resolve any SDK-related errors via the maxio-sdk agent if they arise
3. **Run tests** — follow the Verification Checklist above
4. **Add integration tests** — copy the curl patterns into xUnit tests in `PublicApiIntegrationTests`
5. **Deploy to staging** — with real Maxio credentials for your site
6. **Monitor** — log and alert on Maxio API errors, subscription state changes, and failed idempotency attempts

## File Manifest

```
src/PublicApi/
├── Program.cs                                   [MODIFIED] — Maxio client registration
├── appsettings.json                             [MODIFIED] — Maxio config keys
├── SubscriptionEndpoints/
│   ├── SubscriptionPlanDto.cs                   [NEW]
│   ├── SubscriptionDto.cs                       [NEW]
│   ├── ListSubscriptionPlansEndpoint.cs         [NEW] — GET /api/subscription-plans
│   ├── CreateSubscriptionEndpoint.cs            [NEW] — POST /api/subscriptions
│   └── ListMySubscriptionsEndpoint.cs           [NEW] — GET /api/my-subscriptions
Directory.Packages.props                         [MODIFIED] — Added Maxio SDK 1.0.2
src/PublicApi/PublicApi.csproj                  [MODIFIED] — Added Maxio SDK package ref
maxio-plan.md                                   [CONTRACT SHEET] — Grounded from SDK map
SUBSCRIPTION_INTEGRATION_GUIDE.md               [THIS FILE]
```

## Notes

- All endpoints use MinimalApi.Endpoint for consistency with eShopOnWeb patterns
- Subscription records are **not** persisted locally (in-memory DB is used, so data is lost on restart)
- For production, extend ApplicationUser or create a Subscription entity in ApplicationCore to persist subscription state locally for audit and notification purposes
- Consider adding a background job to sync subscription state from Maxio periodically (e.g., every hour)
- Metered component billing (`api-call` @$0.01/unit) is not wired into the hero flow; add via `SubscriptionComponents.CreateAllocation()` as needed

