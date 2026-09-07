# Maxio Subscription Billing Integration for eShopOnWeb

This document describes the recurring subscription billing integration with Maxio Advanced Billing (formerly Chargify) for the eShopOnWeb reference architecture.

## Overview

The subscription billing capability is an **additive feature** that runs parallel to the existing one-time commerce flow (Catalog → Basket → Order). It enables shoppers to subscribe to recurring billing plans managed by Maxio.

### Architecture

- **Billing System of Record**: Maxio Advanced Billing sandbox
- **Authentication**: JWT-based for PublicApi endpoints
- **Integration Point**: PublicApi project (`src/PublicApi`)
- **Data Persistence**: In-memory database (can be upgraded to SQL Server)

## Components

### 1. Core Service: MaxioSubscriptionService

**Location**: `src/PublicApi/Services/MaxioSubscriptionService.cs`

Handles all interactions with Maxio API:
- **GetSubscriptionPlansAsync()** - Fetch available plans (public)
- **CreateSubscriptionAsync()** - Create subscription with idempotent customer management
- **GetUserSubscriptionsAsync()** - Retrieve user's subscriptions
- **GetSubscriptionAsync()** - Get single subscription details

**Key Features**:
- Idempotent customer creation using reference-based lookup
- Automatic retry on customer lookup failures
- Comprehensive error logging
- JSON-based API integration over HTTPS

### 2. API Endpoints

All endpoints are registered via Ardalis.ApiEndpoints pattern in the PublicApi project.

#### ListSubscriptionPlansEndpoint
```
GET /api/subscription-plans
Authentication: None (public)
Response: { plans: SubscriptionPlanDto[] }
```
Lists all available subscription plans. No authentication required.

#### CreateSubscriptionEndpoint
```
POST /api/subscriptions
Authentication: Bearer JWT token (required)
Body: { planHandle: string }
Response: { subscription: SubscriptionDto }
HTTP 201 Created on success
```
Creates a subscription for the authenticated user. Automatically:
1. Creates a Maxio customer if one doesn't exist (idempotent via reference)
2. Creates the subscription to the specified plan
3. Returns full subscription details

#### GetUserSubscriptionsEndpoint
```
GET /api/my-subscriptions
Authentication: Bearer JWT token (required)
Response: { subscriptions: SubscriptionDto[] }
```
Returns all subscriptions for the authenticated user.

### 3. Configuration

Configuration is loaded from multiple sources in priority order:

1. **Environment Variables** (highest priority)
   - `MAXIO_API_KEY` → `Maxio:ApiKey`
   - `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
   - `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`
   - `MAXIO_BASE_URL` → `Maxio:BaseUrl`

2. **appsettings.json** (fallback)
   ```json
   {
     "Maxio": {
       "ApiKey": "",
       "Subdomain": "",
       "ProductFamilyHandle": "",
       "BaseUrl": ""
     }
   }
   ```

3. **.NET User Secrets** (for development)
   ```bash
   dotnet user-secrets set "Maxio:ApiKey" "value"
   dotnet user-secrets set "Maxio:Subdomain" "value"
   ```

**No secrets are stored in the repository** - values come from environment or user-secrets only.

## Maxio API Integration

### Authentication
- **Method**: HTTP Basic Authentication
- **Username**: API Key (from `MAXIO_API_KEY`)
- **Password**: `x` (literal character)
- **Format**: `Authorization: Basic base64(apikey:x)`

### Base URL
- **Default**: `https://{subdomain}.chargify.com`
- **Override**: Set `MAXIO_BASE_URL` to use custom endpoint (useful for testing proxies)

### Key Endpoints Used

| Operation | Method | Endpoint |
|-----------|--------|----------|
| List Products | GET | `/products.json` |
| Create Customer | POST | `/customers.json` |
| Lookup Customer | GET | `/customers/lookup.json?reference={ref}` |
| Create Subscription | POST | `/subscriptions.json` |
| List Customer Subscriptions | GET | `/customers/{id}/subscriptions.json` |
| Get Subscription | GET | `/subscriptions/{id}.json` |

## Data Models

### SubscriptionPlanDto
```csharp
public class SubscriptionPlanDto
{
    public long Id { get; set; }                      // Maxio product ID
    public string Handle { get; set; }                // Plan handle (e.g., "eshop-pro")
    public string Name { get; set; }                  // Display name
    public string Description { get; set; }           // Plan description
    public decimal Price { get; set; }                // Price in dollars
    public long PriceInCents { get; set; }            // Price in cents
    public int Interval { get; set; }                 // Billing interval (e.g., 1)
    public string IntervalUnit { get; set; }          // "month", "year", etc.
}
```

### SubscriptionDto
```csharp
public class SubscriptionDto
{
    public long Id { get; set; }                      // Maxio subscription ID
    public long CustomerId { get; set; }              // Maxio customer ID
    public string ProductHandle { get; set; }         // Plan handle
    public string ProductName { get; set; }           // Plan name
    public string State { get; set; }                 // "active", "canceled", etc.
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }   // Next billing date
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

## Sandbox Entities

Pre-configured on Maxio sandbox site `cp-exp-4`:

| Entity | Handle | ID | Notes |
|--------|--------|----|----|
| Product Family | `eshop-subscribe` | 3023074 | Contains plans and metered component |
| Pro Plan | `eshop-pro` | 7126957 | $299.00/mo, no trial, no setup fee |
| Basic Plan | `basic-plan` | 7126958 | $29.00/mo, no trial, no setup fee |
| Metered Component | `api-call` | 3057195 | $0.01/unit for usage-based billing |

**Important**: Both plans have "payment method not required" enabled, so subscriptions work without card capture or 3-D Secure.

## Flow Diagrams

### List Plans
```
┌─────────────┐
│   Browser   │
└──────┬──────┘
       │ GET /api/subscription-plans
       ▼
┌──────────────────┐      ┌─────────────────┐
│ PublicApi        │─────▶│ Maxio Sandbox   │
│ SubscriptionEp   │ GET  │ /products.json  │
└──────────────────┘      └─────────────────┘
       │
       │ JSON plans list
       ▼
┌─────────────┐
│   Browser   │ Display plans
└─────────────┘
```

### Create Subscription
```
┌─────────────┐
│   Browser   │
└──────┬──────┘
       │ POST /api/subscriptions + JWT token
       ▼
┌──────────────────────────────────────────┐
│ CreateSubscriptionEndpoint               │
│ 1. Extract user from JWT                 │
│ 2. Call GetOrCreateCustomerAsync         │
│    ├─ Lookup by reference                │
│    └─ Create if not found                │
│ 3. Call CreateSubscriptionAsync          │
│    ├─ POST /subscriptions.json           │
│    └─ Return subscription details        │
└──────────────────┬───────────────────────┘
       ▲           │
       │ ◀─────────┘
       │ Subscription details
┌──────┴──────┐
│   Browser   │
└─────────────┘
```

### Get User Subscriptions
```
┌─────────────┐
│   Browser   │
└──────┬──────┘
       │ GET /api/my-subscriptions + JWT token
       ▼
┌─────────────────────────────┐
│ GetUserSubscriptionsEndpoint │
│ 1. Extract user from JWT    │
│ 2. Lookup customer by ref   │
│ 3. List subscriptions       │
└──────────────┬──────────────┘
       ▲       │ GET /customers/{id}/subscriptions.json
       │ ◀─────┘
       │ Subscription list
┌──────┴──────────┐
│   Browser       │
│ (with auth)     │
└─────────────────┘
```

## Security Considerations

### Authentication & Authorization
- **Endpoints require JWT authentication** (except plan listing)
- User identity extracted from `ClaimTypes.Name` claim in JWT
- Authorization tied to the authenticated user's ID

### Credential Management
- **No secrets in code**: All credentials come from environment or user-secrets
- **Basic Auth with Maxio**: API key transmitted securely over HTTPS only
- **HTTPS required**: `UseHttpsRedirection()` enforced in Program.cs

### Data Isolation
- Subscriptions are per-user via customer reference (`eshop-{userId}`)
- No cross-tenant data leakage possible
- In-memory database limits to current session (production should use SQL Server)

## Error Handling

### Common Error Scenarios

| Scenario | HTTP Status | Response |
|----------|------------|----------|
| Missing plan handle | 400 Bad Request | `"Plan handle is required"` |
| User not authenticated | 401 Unauthorized | Empty response |
| Maxio API error | 500 Internal Server Error | Logged, error propagated |
| Invalid JWT token | 401 Unauthorized | Rejected by middleware |
| Plan not found | Varies | Maxio API response |

### Logging
- All errors logged via `ILogger<MaxioSubscriptionService>`
- Log level: ERROR for exceptions, INFORMATION for successful operations
- Sensitive data (API keys) never logged

## Future Enhancements

### Short-term
- [ ] Plan caching with TTL to reduce API calls
- [ ] Subscription cancellation endpoint
- [ ] Subscription pause/resume endpoints
- [ ] Payment method management

### Medium-term
- [ ] Webhook handlers for subscription lifecycle events
- [ ] Plan upgrade/downgrade
- [ ] Dunning management for failed payments
- [ ] Custom attributes on customers

### Long-term
- [ ] Multi-tenant support with Maxio site per tenant
- [ ] Metered usage tracking integration
- [ ] Billing portal embedding
- [ ] Revenue recognition integration

## Testing

### Manual Testing
1. Follow setup instructions in `MAXIO_SETUP_AND_VERIFICATION.md`
2. Run test script: `./test-maxio-integration.ps1` (Windows) or `./test-maxio-integration.sh` (Linux/Mac)
3. Verify all tests pass

### Automated Testing
- Create xUnit tests in `tests/PublicApiIntegrationTests/`
- Mock `HttpClient` for unit tests
- Use sandbox for integration tests

### Test Scenarios
- [ ] Get plans returns expected list
- [ ] Authentication required for subscription endpoints
- [ ] Invalid JWT rejected
- [ ] Create subscription succeeds with valid plan
- [ ] Create subscription fails with invalid plan
- [ ] Duplicate customer creation is idempotent
- [ ] User subscriptions only show their own

## Monitoring & Troubleshooting

### Key Metrics
- Subscription creation rate and latency
- Maxio API response times
- Failed subscription attempts
- Authentication failures

### Logs to Watch
```
Error creating subscription for user {UserId}
Error getting subscription plans from Maxio
Error getting subscriptions for user {UserId}
```

### Common Issues & Solutions

**Issue**: `401 Unauthorized` from Maxio
- **Cause**: Invalid API key or format
- **Solution**: Verify `MAXIO_API_KEY` and base64 encoding in service

**Issue**: Plans list is empty
- **Cause**: No products with "eshop-" handle in Maxio
- **Solution**: Create plans in Maxio with correct handle prefix

**Issue**: "Could not find customer"
- **Cause**: Customer reference lookup failed
- **Solution**: Check customer reference format (`eshop-{userId}`)

## References

- [Maxio Advanced Billing API](https://developers.maxio.com/)
- [Maxio Help Center](https://docs.maxio.com/hc/en-us/)
- [eShopOnWeb Repository](https://github.com/dotnet-architecture/eShopOnWeb)
- Setup & Verification: `MAXIO_SETUP_AND_VERIFICATION.md`
