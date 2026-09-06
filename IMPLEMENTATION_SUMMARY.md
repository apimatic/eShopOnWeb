# Maxio Subscription Billing Implementation Summary

## What Was Implemented

A complete, production-grade subscription billing system for eShopOnWeb using Maxio Advanced Billing as the system of record.

### Architecture

```
PublicApi (HTTP Endpoints)
    ↓
IMaxioService (Interface)
    ↓
MaxioService (Implementation)
    ↓
HttpClient (REST calls to Maxio)
```

### Components

#### 1. Configuration (`ApplicationCore/Settings/MaxioSettings.cs`)
- Stores Maxio credentials: ApiKey, Subdomain, ProductFamilyHandle, optional BaseUrl
- Loaded from appsettings and environment variables
- Never hardcoded - uses .NET user-secrets for sensitive values

#### 2. Service Layer (`Infrastructure/Services/MaxioService.cs`)
- Implements `IMaxioService` interface
- **Core Methods**:
  - `GetSubscriptionPlansAsync()` - Lists all products in the family
  - `GetOrCreateCustomerAsync()` - Idempotent customer creation via user reference
  - `CreateSubscriptionAsync()` - Creates subscription with payment not required
  - `GetCustomerSubscriptionsAsync()` - Lists all active subscriptions for customer

- **Authentication**: Uses HTTP Basic Auth (API key + "x")
- **Error Handling**: Graceful fallbacks for network/API failures
- **Logging**: Integrated logging for auditing and debugging

#### 3. API Endpoints (`PublicApi/SubscriptionEndpoints/`)

##### a. ListSubscriptionPlansEndpoint
- **Route**: `GET /api/subscription-plans`
- **Auth**: None required
- **Purpose**: Discover available plans
- **Returns**: List of plans with name, handle, price (formatted + cents)

##### b. CreateSubscriptionEndpoint
- **Route**: `POST /api/subscriptions`
- **Auth**: JWT Bearer token (requires login)
- **Body**: `{ "productHandle": "eshop-pro" }`
- **Purpose**: Subscribe authenticated user to plan
- **Returns**: Subscription ID, state, next billing date
- **Idempotency**: Uses eShopOnWeb user ID as Maxio customer reference

##### c. ListMySubscriptionsEndpoint
- **Route**: `GET /api/my-subscriptions`
- **Auth**: JWT Bearer token (requires login)
- **Purpose**: View user's active subscriptions
- **Returns**: Customer ID, subscriptions with state and billing info

### Key Features

1. **Idempotent Subscription Creation**
   - Double-clicking "Subscribe" never creates duplicate customers
   - Uses eShopOnWeb user ID as Maxio customer reference
   - Subsequent subscriptions reuse existing customer

2. **Production-Grade Integration**
   - Comprehensive error handling
   - Structured logging for operations
   - Proper HTTP status codes (201 Created, 400 Bad Request, 401 Unauthorized)
   - Clean separation of concerns (Service layer, Endpoints, Configuration)

3. **No Payment Required**
   - Maxio plans configured without requiring payment method at subscription
   - Simplifies testing and enables free trials

4. **Flexible Configuration**
   - Same build runs against different Maxio sites
   - Settings via environment variables or user secrets
   - No hardcoded URLs or credentials

## File Structure

```
src/
├── ApplicationCore/
│   ├── Interfaces/
│   │   └── IMaxioService.cs (Interface + DTOs)
│   └── Settings/
│       └── MaxioSettings.cs
├── Infrastructure/
│   ├── Services/
│   │   └── MaxioService.cs (Implementation)
│   └── Dependencies.cs (Service registration - UPDATED)
└── PublicApi/
    ├── SubscriptionEndpoints/
    │   ├── ListSubscriptionPlansEndpoint.cs
    │   ├── CreateSubscriptionEndpoint.cs
    │   └── ListMySubscriptionsEndpoint.cs
    └── appsettings.Development.json (UPDATED with Maxio config)
```

## Dependencies Added

No new NuGet packages required - all functionality built on existing dependencies:
- `System.Net.Http` (for API calls)
- `System.Text.Json` (for parsing responses)
- `Microsoft.Extensions.*` (existing DI infrastructure)
- `MinimalApi.Endpoint` (existing for endpoint definition)

## Configuration Required

### For Development

```bash
# Set environment variables or use .NET user-secrets
export MAXIO_API_KEY="your_api_key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
```

### In Code

`appsettings.Development.json`:
```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "cp-exp-2",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": null
  }
}
```

- `ApiKey`: Leave empty in config, provide via environment or user-secrets
- `Subdomain`: Maxio sandbox subdomain
- `ProductFamilyHandle`: Container for subscription plans (configured in Maxio)
- `BaseUrl`: Optional override for API endpoint

## Verification Checklist

- [x] Code builds without errors
- [x] Application starts successfully
- [x] Service registration in DI container
- [x] Endpoints registered with minimal API
- [x] Configuration loaded from appsettings
- [x] No secrets in repository
- [x] Follows eShopOnWeb naming and architecture patterns
- [x] Production-grade error handling
- [x] Proper HTTP status codes
- [x] Idempotent customer creation
- [x] JWT authentication on protected endpoints

## Testing the Integration

See **MAXIO_SETUP.md** for complete step-by-step testing guide.

Quick verification:
```bash
# 1. Start the API
cd src/PublicApi
export UseOnlyInMemoryDatabase=true
dotnet run --urls="https://localhost:24923"

# 2. In another terminal, get a token
TOKEN=$(curl -s -X POST https://localhost:24923/api/authenticate \
  -H "Content-Type: application/json" \
  -k \
  -d '{"username":"demouser@microsoft.com","password":"Pass@123"}' \
  | jq -r .token)

# 3. List plans (no auth needed)
curl -s https://localhost:24923/api/subscription-plans -k | jq

# 4. Subscribe (auth required)
curl -s -X POST https://localhost:24923/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -k \
  -d '{"productHandle":"eshop-pro"}' | jq

# 5. View my subscriptions
curl -s https://localhost:24923/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -k | jq
```

## Implementation Decisions

1. **Minimal API Pattern**: Matched existing PublicApi conventions for consistency
2. **Service Layer**: Centralized Maxio interaction in Infrastructure for reusability
3. **In-Memory Database**: Development uses in-memory; production-ready for SQL Server
4. **No Breaking Changes**: Completely additive - existing cart/checkout flow untouched
5. **User Reference Strategy**: Uses eShopOnWeb user ID as Maxio customer reference for idempotency
6. **Error Handling**: Catches and returns meaningful errors without exposing internals
7. **Logging**: Integrated for debugging and audit trail

## Known Limitations

1. In-memory database loses all data on application restart
2. No webhook support (not in scope)
3. No customer self-service cancellation (not in scope)
4. No usage metering for components (not in scope)
5. Simple plan selection - could be enhanced with UI

## Future Enhancements

1. Web UI for subscription management (Blazor component)
2. Webhook handling for Maxio events (payment failures, billing updates)
3. Subscription cancellation endpoint
4. Plan switching/upgrade/downgrade logic
5. Per-subscription metadata and custom pricing
6. Analytics and MRR tracking

---

**Status**: ✅ Ready for testing with Maxio sandbox credentials
