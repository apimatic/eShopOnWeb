# Maxio Subscription Integration: Architecture & Design

## Overview

This document describes the architecture and design decisions for the Maxio Advanced Billing subscription integration added to eShopOnWeb.

## System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     HTTP Client                              │
│          (Swagger UI / REST Client / Test Script)            │
└─────────────────────────────────────┬───────────────────────┘
                                      │ JWT Token
                                      ▼
┌─────────────────────────────────────────────────────────────┐
│                  PublicApi Server (ASP.NET)                  │
│                                                               │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │          Endpoint Routes (MinimalApi)                   │ │
│  │  • GET  /api/subscription-plans                         │ │
│  │  • POST /api/subscriptions       [Authorize]            │ │
│  │  • GET  /api/my-subscriptions    [Authorize]            │ │
│  └──────────────────────┬──────────────────────────────────┘ │
│                         │ Depends on                          │
│  ┌──────────────────────▼──────────────────────────────────┐ │
│  │         ISubscriptionService                            │ │
│  │  (SubscriptionService Implementation)                   │ │
│  │  • GetSubscriptionPlansAsync()                          │ │
│  │  • CreateSubscriptionAsync(userId, productHandle)       │ │
│  │  • GetMySubscriptionsAsync(userId)                      │ │
│  └──────────────────────┬──────────────────────────────────┘ │
│                         │ Uses                                │
│  ┌──────────────────────▼──────────────────────────────────┐ │
│  │         IMaxioApiClient                                 │ │
│  │  (MaxioApiClient Implementation)                        │ │
│  │  • ListProductsAsync(familyHandle)                      │ │
│  │  • GetOrCreateCustomerAsync()                           │ │
│  │  • CreateSubscriptionAsync()                            │ │
│  │  • ListSubscriptionsAsync()                             │ │
│  └──────────────────────┬──────────────────────────────────┘ │
│                         │ HTTP Client                         │
│  ┌──────────────────────▼──────────────────────────────────┐ │
│  │          Configuration (MaxioOptions)                   │ │
│  │  • ApiKey                                               │ │
│  │  • Subdomain                                            │ │
│  │  • ProductFamilyHandle                                  │ │
│  │  • BaseUrl                                              │ │
│  └──────────────────────┬──────────────────────────────────┘ │
│                         │                                     │
│  Database Access Layer  │  UserManager<ApplicationUser>       │
│  (Repositories)         │  (Identity Management)              │
│  • CatalogContext       │                                     │
│  • AppIdentityDbContext │                                     │
│  (In-Memory or SQL)     │                                     │
└─────────────────────────┼──────────────────────────────────────┘
                          │ HTTPS
                          ▼
        ┌─────────────────────────────────────┐
        │   Maxio Advanced Billing API        │
        │   (Sandbox: cp-exp-4)               │
        │                                     │
        │  • GET  /products.json              │
        │  • POST /customers.json             │
        │  • GET  /customers.json             │
        │  • POST /subscriptions.json         │
        │  • GET  /subscriptions.json         │
        └─────────────────────────────────────┘
```

## Layer Breakdown

### 1. HTTP Endpoints (Minimal API)

**Files**: `SubscriptionEndpoints/`

**Pattern**: `IEndpoint<IResult, TRequest>` (MinimalApi.Endpoint)

**Endpoints**:
- `ListSubscriptionPlansEndpoint`: GET /api/subscription-plans
  - No authentication required
  - Calls `SubscriptionService.GetSubscriptionPlansAsync()`
  
- `CreateSubscriptionEndpoint`: POST /api/subscriptions
  - Requires JWT bearer authentication
  - Extracts user ID from token claims
  - Calls `SubscriptionService.CreateSubscriptionAsync(userId, productHandle)`
  
- `ListMySubscriptionsEndpoint`: GET /api/my-subscriptions
  - Requires JWT bearer authentication
  - Extracts user ID from token claims
  - Calls `SubscriptionService.GetMySubscriptionsAsync(userId)`

**Authentication**:
- Uses `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`
- User ID extracted from `ClaimTypes.NameIdentifier` in HTTP context
- Added to token by `IdentityTokenClaimService` when creating JWT

### 2. Business Logic Service

**File**: `SubscriptionService.cs`

**Interface**: `ISubscriptionService`

**Responsibilities**:
- Orchestrate subscription operations
- Map between DTOs and API responses
- Handle price conversions (cents to dollars)
- Manage user context and lookups

**Key Methods**:
- `GetSubscriptionPlansAsync()`: Retrieves plans from Maxio
- `CreateSubscriptionAsync(userId, productHandle)`:
  - Gets or creates Maxio customer (using userId as reference)
  - Creates subscription for the customer
  - Returns subscription details
- `GetMySubscriptionsAsync(userId)`:
  - Looks up customer by userId reference
  - Retrieves all subscriptions for that customer
  - Returns empty list if customer doesn't exist

### 3. Maxio API Client

**File**: `MaxioApiClient.cs`

**Interface**: `IMaxioApiClient`

**Responsibilities**:
- HTTP communication with Maxio API
- Adherence to OpenAPI specification
- JSON parsing and response mapping
- Basic authentication setup (API key)

**Key Methods**:
- `ListProductsAsync(familyHandle)`: GET /products.json
- `GetOrCreateCustomerAsync()`: 
  - GET /customers.json (lookup by reference)
  - POST /customers.json (create if not found)
- `CreateSubscriptionAsync()`: POST /subscriptions.json
- `ListSubscriptionsAsync()`: GET /subscriptions.json?customer_id=X

**Authentication**:
- Basic Auth with API key: `Authorization: Basic base64(apikey:)`
- Per Maxio API specification

### 4. Configuration

**File**: `MaxioOptions.cs`

**Configuration Keys** (from `appsettings.json` or user-secrets):
```json
{
  "Maxio": {
    "ApiKey": "sandbox key from env",
    "Subdomain": "sandbox subdomain",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": "" // Optional override
  }
}
```

**Resolution Order**:
1. User Secrets (development)
2. Environment Variables (Maxio__ApiKey, Maxio__Subdomain)
3. appsettings.json

## Data Flow: Create Subscription

```
1. Client sends:
   POST /api/subscriptions
   Authorization: Bearer <jwt-token>
   { "productHandle": "eshop-pro" }

2. CreateSubscriptionEndpoint extracts user ID from claims

3. Calls SubscriptionService.CreateSubscriptionAsync(userId, productHandle)

4. SubscriptionService:
   a. Looks up ApplicationUser by ID from UserManager
   b. Calls MaxioApiClient.GetOrCreateCustomerAsync(userId, email, name, name)
      - MaxioApiClient queries: GET /customers.json?reference=<userid>
      - If not found (404), creates customer: POST /customers.json
      - Returns MaxioCustomerDto with Maxio customer ID
   
   c. Creates subscription request:
      {
        "subscription": {
          "customer_id": <maxio-customer-id>,
          "product_handle": "eshop-pro",
          "payment_collection_method": "remittance"
        }
      }
   
   d. Calls MaxioApiClient.CreateSubscriptionAsync(request)
      - Posts to /subscriptions.json
      - Returns MaxioSubscriptionDto
   
   e. Maps to CreateSubscriptionResponse (converts cents to dollars)

5. CreateSubscriptionEndpoint returns 201 Created with subscription details
```

## Key Design Decisions

### 1. User ID as Customer Reference

**Decision**: Use eShopOnWeb user ID as the `reference` field in Maxio customer

**Rationale**:
- Idempotent customer creation: same user always maps to same customer
- No duplicate customers created if subscription endpoint is called twice
- Enables easy user-to-customer lookup for listing subscriptions
- Maxio's `reference` field is designed exactly for this purpose

**Implementation**:
```csharp
// In SubscriptionService.CreateSubscriptionAsync:
var maxioCustomer = await _maxioClient.GetOrCreateCustomerAsync(
    userId,  // Used as 'reference' in Maxio
    user.Email,
    user.UserName,
    user.UserName
);
```

### 2. Service Layer Pattern

**Decision**: Introduce `ISubscriptionService` between endpoints and API client

**Rationale**:
- Decouples endpoints from Maxio API details
- Centralizes business logic (not scattered across endpoints)
- Easier to test (can mock service)
- Cleaner endpoint implementations
- Facilitates future persistence layer (subscription tracking in DB)

### 3. Product Family Handle Configuration

**Decision**: Support configurable product family handle via settings

**Rationale**:
- Same build can target different Maxio sites/families
- Supports multi-tenant scenarios
- Default: "eshop-subscribe" (matches sandbox catalog)

### 4. JWT Token Enhancement

**Decision**: Add `NameIdentifier` claim with user ID to JWT token

**Rationale**:
- Endpoints can extract user context from claims
- Eliminates need to make extra database query
- Standard approach (ClaimTypes.NameIdentifier is built-in)
- Doesn't break existing token usage

**Changed File**: `Infrastructure/Identity/IdentityTokenClaimService.cs`

### 5. Idempotent Operations

**Decision**: Make subscription operations idempotent where possible

**Rationale**:
- Prevents duplicate state on network retries
- Safe for retry logic
- Matches REST principles

**Implementation**:
- Customer creation is idempotent (looked up by reference first)
- Subscription creation is idempotent (each call creates new subscription, but no duplicate customers)

### 6. Payment Collection Method

**Decision**: Use "remittance" payment method (not requiring credit card)

**Rationale**:
- Per requirements: "payment method not required"
- Matches Maxio sandbox configuration
- Supports invoice-based billing flow
- Can be changed per business needs

## Error Handling

### Endpoint Level
- 401 Unauthorized: Missing or invalid JWT token
- 400 Bad Request: Invalid request or Maxio error
- 201 Created: Successful subscription creation

### Service Level
- Exceptions from Maxio wrapped and re-thrown
- User-friendly error messages in responses

### API Client Level
- HTTP errors logged with details
- Null reference handling for JSON parsing
- 404 handling for customer lookups

## Testing Strategy

### Manual Testing
- Use `test-subscription-flow.ps1` script
- Follow verification guide: `VERIFY_SUBSCRIPTION_INTEGRATION.md`

### Unit Testing
- Mock `IMaxioApiClient` for service tests
- Mock `UserManager` for endpoint tests
- Test data transformations (e.g., cents to dollars)

### Integration Testing
- Requires Maxio sandbox credentials
- Tests full flow against real API
- Can be run via test script

## Production Considerations

### Security
- Never commit secrets (.env, credentials.json, hardcoded keys)
- Use Azure Key Vault or similar for production keys
- Implement rate limiting on subscription endpoints
- Add audit logging for all subscription operations

### Scalability
- Maxio API has rate limits - implement caching/backoff
- Current implementation is synchronous - consider async patterns
- Add request tracing/correlation IDs for support

### Monitoring
- Log all Maxio API calls (failures and successes)
- Monitor subscription creation success rate
- Alert on customer creation failures
- Track subscription counts and revenue

### Data Persistence
- Current: In-memory database (development only)
- Production: Migrate to SQL Server with proper schema
- Add subscription tracking table to map eShopOnWeb subscriptions to Maxio IDs
- Enable audit trail for subscription changes

## Integration Points

### AuthenticationFlow
- Endpoints decorated with `[Authorize]`
- JWT token includes user ID
- User lookup via UserManager

### Configuration
- appsettings.json for defaults
- User-secrets for development
- Environment variables for all environments

### Dependency Injection
- `IMaxioApiClient` registered as HttpClient-backed service
- `ISubscriptionService` registered as scoped service
- MaxioOptions bound to configuration section

## Future Enhancements

1. **Subscription Management**
   - Cancel subscription endpoint
   - Update subscription endpoint
   - Pause subscription endpoint

2. **Payment Handling**
   - Credit card capture for prepayment plans
   - Invoice generation integration
   - Payment status webhooks

3. **Usage Tracking**
   - Metered component reporting for API calls
   - Real-time usage synchronization with Maxio

4. **Reporting**
   - Subscription analytics
   - Revenue reports
   - Customer lifetime value tracking

5. **Database Persistence**
   - Subscription audit log
   - Customer relationship mapping
   - Subscription history and state transitions

## Files & Structure

```
src/PublicApi/
├── SubscriptionEndpoints/
│   ├── ListSubscriptionPlansEndpoint.cs
│   ├── CreateSubscriptionEndpoint.cs
│   ├── ListMySubscriptionsEndpoint.cs
│   ├── SubscriptionService.cs
│   ├── IMaxioApiClient.cs
│   ├── MaxioApiClient.cs
│   ├── MaxioOptions.cs
│   ├── SubscriptionPlanDto.cs
│   └── (other DTO files)
├── Program.cs (modified - registers services)
├── appsettings.json (modified - Maxio section)
└── ...

Documentation:
├── SUBSCRIPTION_SETUP.md (Configuration & Running)
├── VERIFY_SUBSCRIPTION_INTEGRATION.md (Testing Guide)
├── SUBSCRIPTION_ARCHITECTURE.md (This file)
├── setup-maxio-secrets.ps1 (Secret setup script)
└── test-subscription-flow.ps1 (Integration test script)
```

## References

- Maxio OpenAPI Spec: `maxio-spec/openapi.yaml`
- MinimalApi.Endpoint GitHub: https://github.com/ardalis/Ardalis.ApiEndpoints
- ASP.NET Core JWT Auth: https://docs.microsoft.com/en-us/aspnet/core/security/authentication/jwt-claims
- Maxio API Docs: https://docs.maxio.com/

## Compliance & Standards

- **API**: RESTful endpoints following OpenAPI specification
- **Authentication**: JWT bearer tokens (RFC 7519)
- **HTTP**: Standard status codes and methods
- **Configuration**: .NET configuration standard patterns
- **DI**: Built-in .NET dependency injection
