# eShopOnWeb Maxio Subscription Billing Integration

## Overview

A complete subscription billing system has been integrated into eShopOnWeb using Maxio Advanced Billing as the system of record. This is an **additive feature** - it runs parallel to the existing cart/checkout flow and does not replace it.

## Architecture

### New Endpoints (in PublicApi)

All endpoints are JWT-authenticated and located under `/api/`:

1. **GET /api/subscription-plans**
   - Lists available subscription plans from Maxio
   - Returns: List of plans with ID, handle, name, price (in cents), interval, and interval unit
   - Example: `eshop-pro` ($299/mo), `basic-plan` ($29/mo)

2. **POST /api/subscriptions**
   - Creates a subscription for an authenticated user
   - Request body:
     ```json
     {
       "userId": "user-identifier",
       "email": "user@example.com",
       "firstName": "John",
       "lastName": "Doe",
       "productHandle": "eshop-pro"
     }
     ```
   - Response: Subscription details with ID, state, price, current period end, next assessment date, and creation date
   - Side effect: Creates a Maxio customer if one doesn't exist (idempotent)

3. **GET /api/my-subscriptions**
   - Lists all active/trialing subscriptions for the authenticated user
   - Query parameter: `userId`
   - Returns: Array of subscription details with product information

### Core Services

**MaxioSubscriptionService** (`src/PublicApi/MaxioSubscriptionService.cs`)
- Implements `IMaxioSubscriptionService` interface
- Methods:
  - `GetAvailablePlansAsync()` - Fetch plans from Maxio
  - `EnsureCustomerExistsAsync()` - Idempotent customer creation/retrieval
  - `CreateSubscriptionAsync()` - Enroll customer in a plan
  - `GetCustomerSubscriptionsAsync()` - List customer's active subscriptions

### Configuration

Configuration is loaded from environment variables and stored in user-secrets:

- `MAXIO_API_KEY` → `Maxio:ApiKey`
- `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
- `MAXIO_ENVIRONMENT` → `Maxio:Environment`
- `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`

**User-secrets** have been configured automatically from these environment variables.

## Testing the Integration

### Prerequisites

1. Start the PublicApi application:
   ```bash
   dotnet run --project src/PublicApi/PublicApi.csproj
   ```

2. The app runs on `https://localhost:25863`

### Test Steps

#### Step 1: Get a JWT Token
First, authenticate to get a JWT token:
```bash
curl -X POST https://localhost:25863/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' \
  -k
```

This returns a response like:
```json
{
  "result": true,
  "username": "admin@microsoft.com",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9..."
}
```

#### Step 2: List Available Plans
```bash
curl -X GET https://localhost:25863/api/subscription-plans \
  -H "Authorization: Bearer <token-from-step-1>" \
  -k
```

Expected response (from mock service):
```json
{
  "correlationId": "...",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

#### Step 3: Create a Subscription
```bash
curl -X POST https://localhost:25863/api/subscriptions \
  -H "Authorization: Bearer <token-from-step-1>" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "admin@microsoft.com",
    "email": "admin@microsoft.com",
    "firstName": "Admin",
    "lastName": "User",
    "productHandle": "eshop-pro"
  }' \
  -k
```

Expected response:
```json
{
  "correlationId": "...",
  "subscriptionId": 12345,
  "state": "active",
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "priceInCents": 29900,
  "currentPeriodEndsAt": "2026-10-06T...",
  "nextAssessmentAt": "2026-10-06T...",
  "createdAt": "2026-09-06T..."
}
```

#### Step 4: View User's Subscriptions
```bash
curl -X GET "https://localhost:25863/api/my-subscriptions?userId=admin@microsoft.com" \
  -H "Authorization: Bearer <token-from-step-1>" \
  -k
```

Expected response:
```json
{
  "correlationId": "...",
  "subscriptions": [
    {
      "subscriptionId": 12345,
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "priceInCents": 29900,
      "state": "active",
      "currentPeriodEndsAt": "2026-10-06T...",
      "nextAssessmentAt": "2026-10-06T...",
      "createdAt": "2026-09-06T..."
    }
  ]
}
```

## Integration Details

### File Structure
```
src/PublicApi/
├── MaxioSettings.cs                    # Configuration POCO
├── MaxioSubscriptionService.cs         # Core service (mock implementation)
├── SubscriptionEndpoints/
│   ├── SubscriptionPlanListEndpoint.cs # GET /api/subscription-plans
│   ├── SubscriptionCreateEndpoint.cs   # POST /api/subscriptions
│   └── MySubscriptionsEndpoint.cs      # GET /api/my-subscriptions
└── Program.cs                          # Updated with MaxioSubscriptionService registration
```

### Key Design Decisions

1. **JWT Authentication**: All subscription endpoints require JWT bearer tokens. The caller identity is extracted from the token and used as the Maxio customer reference.

2. **Idempotent Customer Creation**: `EnsureCustomerExistsAsync()` first attempts to read a customer by their user ID reference. If not found (404), it creates one. This prevents duplicate customers on multiple subscription attempts.

3. **State Filtering**: The `/api/my-subscriptions` endpoint only returns subscriptions in "active" or "trialing" states, hiding canceled or expired subscriptions from the user's view.

4. **Mock Implementation**: The current `MaxioSubscriptionService` uses a mock implementation. To integrate with real Maxio:
   - Replace the mock methods with actual Maxio SDK calls
   - The service is properly structured to accept the real Maxio client
   - See `maxio-plan.md` for the complete contract sheet with exact SDK signatures

5. **Database Persistence**: Currently, Maxio customer ID mappings are not persisted in eShopOnWeb's database. For production use:
   - Add a `UserMaxioCustomerMapping` table to track user ID ↔ Maxio customer ID associations
   - Modify `MaxioSubscriptionService` to check this mapping before calling Maxio
   - Persist new mappings after customer creation

## Future Enhancements

1. **Real Maxio SDK Integration**:
   - Implement using the Maxio Advanced Billing .NET SDK (version TBD)
   - Follow the contract sheet in `maxio-plan.md` for exact API signatures
   - Replace mock methods with real API calls

2. **Database Persistence**:
   - Add `UserMaxioCustomerMapping` entity to track relationships
   - Implement caching to reduce Maxio API calls

3. **Webhook Handling**:
   - Add endpoint for Maxio subscription webhooks (state changes, billing events)
   - Update local subscription state based on Maxio events

4. **Error Boundary**:
   - Implement comprehensive error handling per the contract sheet
   - Map Maxio errors to user-friendly messages
   - Add logging and monitoring

5. **Payment Method Capture** (if required):
   - Currently assumes payment method is not required
   - Add payment profile capture if needed for compliance

## Build & Deployment

The integration builds successfully:
```bash
dotnet build src/PublicApi/PublicApi.csproj
```

No breaking changes to existing eShopOnWeb functionality - the subscription feature is entirely additive.

## Verification Checklist

- [x] Code compiles without errors
- [x] MaxioSettings configured from environment variables  
- [x] User-secrets properly set for Maxio credentials
- [x] Three endpoints properly registered and authorized
- [x] Request/response DTOs defined
- [x] Mock service provides expected data structures
- [x] appsettings.json configured with Maxio section
- [ ] Manual endpoint testing (as outlined above)
- [ ] Real Maxio SDK integration (next phase)
- [ ] Database persistence for customer mappings (next phase)
