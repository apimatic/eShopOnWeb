# Maxio Subscription Billing - Architecture & Design

## Overview

The Maxio subscription billing integration is an **additive capability** that coexists alongside the existing one-time commerce flow (Catalog → Basket → Order). This document describes the architecture, design decisions, and implementation patterns.

## System Architecture

```
┌─────────────────────────────────────────────────────────┐
│                     eShopOnWeb                          │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │           PublicApi (JWT-authenticated)          │  │
│  │                                                   │  │
│  │  /api/subscriptions (POST)                        │  │
│  │  /api/subscription-plans (GET)                    │  │
│  │  /api/my-subscriptions (GET)                      │  │
│  └──────────────────────────────────────────────────┘  │
│           ↓                                              │
│  ┌──────────────────────────────────────────────────┐  │
│  │         MaxioClient Service                      │  │
│  │  - HTTP Basic Auth                                │  │
│  │  - Customer management                           │  │
│  │  - Subscription CRUD                             │  │
│  │  - Product catalog queries                       │  │
│  └──────────────────────────────────────────────────┘  │
│           ↓                                              │
└─────────────────────────────────────────────────────────┘
            ↓
┌─────────────────────────────────────────────────────────┐
│              Maxio Billing API (Sandbox)                │
│                  https://cp-exp-2.chargify.com          │
│                                                         │
│  - Customer management                                  │
│  - Subscription lifecycle                              │
│  - Product catalog                                     │
│  - Invoice generation                                  │
│  - Payment processing (optional)                       │
└─────────────────────────────────────────────────────────┘
```

## Component Breakdown

### 1. Endpoints Layer (`/SubscriptionEndpoints`)

**Purpose:** REST API entry points for subscription operations

#### SubscriptionPlansEndpoint
- **HTTP Method:** GET
- **Route:** `/api/subscription-plans`
- **Auth:** JWT Bearer
- **Responsibility:**
  - List all subscription plans from the configured product family
  - Query Maxio for products filtered by family handle
  - Return plan details: name, price, billing interval, etc.
- **Response:** SubscriptionPlansResponse with plans array

#### CreateSubscriptionEndpoint
- **HTTP Method:** POST
- **Route:** `/api/subscriptions`
- **Auth:** JWT Bearer
- **Input:** CreateSubscriptionRequest { productHandle }
- **Responsibility:**
  - Extract user identity from JWT claims
  - Ensure customer exists in Maxio (create if needed)
  - Create subscription in Maxio
  - Return subscription details and next billing date
- **Response:** CreateSubscriptionResponse with subscription metadata
- **Idempotency:** Customer creation is idempotent (lookup before create)

#### ListSubscriptionsEndpoint
- **HTTP Method:** GET
- **Route:** `/api/my-subscriptions`
- **Auth:** JWT Bearer
- **Responsibility:**
  - Extract user identity from JWT claims
  - Look up Maxio customer by reference
  - List all subscriptions for that customer
  - Return subscription details including billing dates
- **Response:** ListSubscriptionsResponse with subscriptions array

### 2. Service Layer (`/Services`)

#### MaxioClient
- **Type:** Injectable service (IMaxioClient)
- **Responsibility:**
  - Encapsulate all Maxio API communication
  - Handle HTTP Basic authentication
  - Serialize/deserialize JSON with snake_case naming
  - Provide high-level methods for common operations

**Key Methods:**
- `GetOrCreateCustomerAsync(userId, firstName, lastName, email)` - Idempotent customer lookup/create
- `GetProductsByFamilyHandleAsync(familyHandle)` - Query products in a family
- `CreateSubscriptionAsync(customerReference, productHandle)` - Create new subscription
- `GetSubscriptionAsync(subscriptionId)` - Retrieve subscription details
- `ListCustomerSubscriptionsAsync(customerId)` - List customer's subscriptions

**Error Handling:**
- All API calls wrapped in try-catch
- Errors logged with context
- Graceful exceptions thrown for higher-level handling

### 3. Configuration Layer

#### MaxioConfiguration
- **Binding:** Configured section `Maxio` in appsettings
- **Properties:**
  - `ApiKey`: Maxio API credentials (from env var or user-secrets)
  - `Subdomain`: Maxio tenant subdomain (default: cp-exp-2)
  - `ProductFamilyHandle`: Product family containing subscription plans (default: eshop-subscribe)
  - `BaseUrl`: Optional override for custom API endpoints

**Resolution Priority:**
1. User-secrets (development)
2. Environment variables
3. appsettings.json defaults

### 4. Data Models

#### Request DTOs
```csharp
public class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; }
}
```

#### Response DTOs
```csharp
public class SubscriptionPlansResponse : BaseResponse
{
    public bool Success { get; set; }
    public List<SubscriptionPlanDto> Plans { get; set; }
}

public class CreateSubscriptionResponse : BaseResponse
{
    public int SubscriptionId { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; }
    public DateTime? NextBillingAt { get; set; }
    // ... more fields
}
```

#### Maxio API Models
- `Customer`: Customer entity from Maxio
- `Product`: Product/plan entity
- `ProductFamily`: Product family grouping
- `Subscription`: Subscription entity with state and dates
- Response wrappers for JSON deserialization

## Flow Diagrams

### Hero Flow: Subscribe

```
User (JWT Token)
    ↓
GET /api/subscription-plans
    ↓
SubscriptionPlansEndpoint
    ↓
MaxioClient.GetProductsByFamilyHandleAsync()
    ↓
HTTP GET /products.json (filtered by family)
    ↓
Maxio API
    ↓
Return list of plans
    ↓
User selects plan
    ↓
POST /api/subscriptions { productHandle: "eshop-pro" }
    ↓
CreateSubscriptionEndpoint
    ↓
Extract userId from JWT
    ↓
MaxioClient.GetOrCreateCustomerAsync(userId, ...)
    ↓
  ├─→ GET /customers/lookup.json?reference={userId}
    ↓
    (Customer exists?) 
    ├─ Yes: Return existing
    └─ No: POST /customers.json (create new)
    ↓
MaxioClient.CreateSubscriptionAsync(userId, "eshop-pro")
    ↓
POST /subscriptions.json
    ├─ customer_reference: userId
    └─ product_handle: "eshop-pro"
    ↓
Maxio API (creates subscription, generates first invoice)
    ↓
Return subscription with state, dates, ID
    ↓
User sees confirmation with next billing date
```

### View Subscriptions Flow

```
User (JWT Token)
    ↓
GET /api/my-subscriptions
    ↓
ListSubscriptionsEndpoint
    ↓
Extract userId from JWT
    ↓
MaxioClient.GetOrCreateCustomerAsync(userId, ...)
    ↓
MaxioClient.ListCustomerSubscriptionsAsync(customerId)
    ↓
GET /customers/{id}/subscriptions.json
    ↓
Maxio API
    ↓
Return list of subscriptions with state, pricing, dates
    ↓
Format and return to user
```

## Authentication & Authorization

### JWT Token Flow
1. User authenticates via `/api/authenticate` (existing endpoint)
2. Token contains claims: NameIdentifier (userId), Email, GivenName, Surname
3. Token sent in `Authorization: Bearer <token>` header
4. Endpoints validate token via JWT middleware

### Claim Extraction
```csharp
var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
var email = context.User.FindFirst(ClaimTypes.Email)?.Value;
var firstName = context.User.FindFirst(ClaimTypes.GivenName)?.Value;
var lastName = context.User.FindFirst(ClaimTypes.Surname)?.Value;
```

### Maxio Authentication
- HTTP Basic Auth: Base64(ApiKey:X)
- Applied to all Maxio API calls via HttpClient default headers
- Configured in `MaxioClient.ConfigureHttpClient()`

## Data Mapping

### User Identity Mapping
- eShopOnWeb User ID → Maxio Customer Reference
- Enables idempotent operations
- Allows querying subscriptions by user identity

### Plan Mapping
- Maxio Product Handle (e.g., "eshop-pro") → UI friendly name
- Plan pricing in cents → Dollar amounts in UI

### Subscription State
- Maxio states: "active", "paused", "canceled", "expired", "trial"
- Passed through to client without translation

## Dependency Injection

### Registration (in Program.cs)
```csharp
builder.Services.Configure<MaxioConfiguration>(
    builder.Configuration.GetSection(MaxioConfiguration.CONFIG_NAME));
builder.Services.AddHttpClient<IMaxioClient, MaxioClient>();
builder.Services.AddHttpContextAccessor();
```

### Injection Points
- Endpoints: Receive `IMaxioClient` and `IHttpContextAccessor`
- MaxioClient: Receives `HttpClient`, `IOptions<MaxioConfiguration>`, `ILogger`

## Configuration Management

### appsettings.json
```json
{
  "Maxio": {
    "ApiKey": "",
    "Subdomain": "cp-exp-2",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": ""
  }
}
```

### Environment Variables (override appsettings)
- MAXIO_API_KEY
- MAXIO_SITE_SUBDOMAIN
- MAXIO_ENVIRONMENT
- MAXIO_DEFAULT_PRODUCT_FAMILY

### User Secrets (development, highest priority)
```bash
dotnet user-secrets set "Maxio:ApiKey" "..."
```

## Error Handling Strategy

### API Level
- Try-catch wrapping all Maxio calls
- Log errors with context
- Return HTTP 400/500 with user-friendly message

### Client Level
- Validate JWT token presence before processing
- Extract user context with null checks
- Return 401 Unauthorized if authentication fails
- Return 400 Bad Request for invalid inputs

### Maxio Level
- Catch HTTP errors from Maxio API
- Log full error details for debugging
- Return generic error message to client (security)

## Performance Considerations

### Caching Opportunities
- Product list rarely changes (could cache per app instance)
- Customer lookups could be cached per session
- NOT implemented in MVP (added complexity not needed initially)

### API Call Minimization
- GetOrCreateCustomerAsync makes 1-2 calls maximum
- CreateSubscriptionAsync makes 1 call
- ListSubscriptionsAsync makes 1 lookup + 1 list call

### Database Impact
- No database writes in current implementation
- Customer-subscription mapping exists only in Maxio
- eShopOnWeb stores no subscription data

## Security Considerations

### Secrets Management
- API key NEVER hardcoded or committed
- Always read from environment or user-secrets
- Build succeeds without credentials (defaults to empty string)

### Authorization
- All endpoints require JWT authentication
- User can only see their own subscriptions
- No admin/elevated privileges needed

### HTTPS/TLS
- All Maxio API calls over HTTPS
- PublicApi configured with UseHttpsRedirection

### Input Validation
- ProductHandle validated (checked against Maxio product list)
- User context required for all operations
- No SQL injection risk (no direct DB access)

## Extensibility Points

### Easy Additions
- New product families (change ProductFamilyHandle config)
- New subscription endpoints (e.g., cancel, upgrade)
- Metered component tracking
- Custom pricing support

### Moderate Additions
- Database persistence of subscriptions
- Webhook support for Maxio events
- Invoice/billing history display
- Tax calculation integration

### Complex Additions
- Multi-tenant support
- White-label billing portal
- Revenue recognition rules
- Subscription analytics

## Design Decisions

### Why Idempotent Customer Creation?
- Double-click protection (user experience)
- Handles edge cases gracefully
- Prevents orphaned subscriptions
- Required for distributed/retry scenarios

### Why No Database Persistence?
- MVP scope (see task requirements)
- In-memory database already specified
- Customer/subscription data in Maxio is source of truth
- Can be added later if needed

### Why Snake_case JSON?
- Maxio API uses snake_case exclusively
- Automatic serialization via JsonSerializerOptions
- Maintains compatibility without manual mapping

### Why MaxioClient Service?
- Centralize API communication logic
- Enable testability via IMaxioClient interface
- Consistent error handling
- Single responsibility principle

## Testing Strategy

### Unit Tests (Future)
- Mock IMaxioClient interface
- Test endpoint logic independently
- Test configuration binding

### Integration Tests (Future)
- Use Maxio sandbox credentials
- Test against actual Maxio API
- Verify end-to-end flows

### Manual Testing (Current)
- See VERIFICATION_GUIDE.md for step-by-step procedures
- Test with provided sandbox credentials
- Verify Swagger endpoint discovery

## Future Roadmap

1. **Phase 2:** Add database persistence
2. **Phase 3:** Webhook support for subscription events
3. **Phase 4:** Customer portal for managing subscriptions
4. **Phase 5:** Analytics and reporting dashboards
5. **Phase 6:** Advanced billing features (prorations, etc.)

---

**Design Principles Applied:**
- ✓ Single Responsibility Principle
- ✓ Dependency Injection
- ✓ Configuration over Secrets
- ✓ Idempotent Operations
- ✓ Graceful Error Handling
- ✓ Extensibility via Interfaces
- ✓ Security by Default
