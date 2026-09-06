# Maxio Subscription Billing Implementation Summary

## Overview
Successfully implemented recurring-subscription billing integration for eShopOnWeb using **Maxio Advanced Billing** as the billing system of record. This is an additive capability that runs in parallel with the existing one-time commerce (Catalog → Basket → Order) flow.

## Architecture

### Design Principles
- **Billing System of Record**: Maxio is the authoritative source for all subscription data
- **Idempotent**: Double-clicking never creates duplicate customers or subscriptions
- **Production-Grade**: Proper error handling, logging, and security
- **No New Infrastructure**: Uses only .NET Framework, no Docker/databases required
- **Configuration-Driven**: All credentials from environment variables, never hardcoded

### Key Components

#### 1. IMaxioClient Interface
Location: `src/ApplicationCore/Interfaces/IMaxioClient.cs`

Defines the contract for all Maxio interactions:
- `GetProductsForFamilyAsync()` - Fetch available subscription plans
- `GetOrCreateCustomerAsync()` - Idempotent customer provisioning
- `CreateSubscriptionAsync()` - Enroll user in a plan
- `GetCustomerSubscriptionsAsync()` - List user's active subscriptions
- `FindCustomerByReferenceAsync()` - Lookup existing customers

#### 2. MaxioClient Service
Location: `src/Infrastructure/Services/MaxioClient.cs`

HTTP client implementation using Maxio's REST API:
- Basic authentication with API key
- Full JSON parsing of responses
- Comprehensive error logging
- Idempotent customer lookup by user ID reference

Configuration:
```csharp
public class MaxioSettings
{
    public string? Subdomain { get; set; }           // MAXIO_SITE_SUBDOMAIN
    public string? ApiKey { get; set; }              // MAXIO_API_KEY
    public string? ProductFamilyHandle { get; set; } // MAXIO_DEFAULT_PRODUCT_FAMILY
    public string? BaseUrl { get; set; }             // Optional override
}
```

#### 3. API Endpoints
All registered in `PublicApi` project under `SubscriptionEndpoints`:

##### GET /api/subscription-plans
- **Authentication**: Not required
- **Purpose**: Browse available subscription plans
- **Returns**: List of plans with ID, handle, name, price, billing cycle
- **Example**:
  ```bash
  curl https://localhost:25643/api/subscription-plans
  ```

##### POST /api/subscriptions
- **Authentication**: Required (JWT bearer token)
- **Purpose**: Subscribe authenticated user to a plan
- **Request**: `{ "productHandle": "eshop-pro" }`
- **Returns**: Subscription details with ID, state, next billing date, price
- **Behavior**: 
  - Creates Maxio customer if needed (idempotent)
  - Enrolls in specified plan
  - Returns subscription state

##### GET /api/my-subscriptions
- **Authentication**: Required (JWT bearer token)
- **Purpose**: View authenticated user's subscriptions
- **Returns**: List of all subscriptions with state and billing info
- **Behavior**: Returns empty list if no Maxio customer exists for user

#### 4. Data Models
Location: `src/PublicApi/SubscriptionEndpoints/`

Request/Response DTOs:
- `ListSubscriptionPlansRequest/Response`
- `CreateSubscriptionRequest/Response`
- `ListMySubscriptionsRequest/Response`

Data Transfer Objects:
- `SubscriptionPlanDto` - Plan information
- `SubscriptionDto` - Subscription details

### Configuration Hierarchy

Settings are loaded in this priority order:
1. `Maxio:*` keys from `appsettings.json`
2. `MAXIO_*` environment variables
3. Hardcoded defaults

```bash
# Environment Variables
export MAXIO_API_KEY="your_api_key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase=true
export DOTNET_ROLL_FORWARD=Major
```

### Authentication Flow

```
User Login
    ↓
GET /api/authenticate → JWT Token
    ↓
Use Token with subscription endpoints
    ↓
Token decoded to extract NameIdentifier claim
    ↓
User ID used as Maxio customer reference
```

### Subscription Creation Flow

```
POST /api/subscriptions (with JWT token)
    ↓
Extract userId from claims
    ↓
Find or Create Maxio Customer (idempotent)
    └─ Reference: userId
    └─ Email: user.Email or generated
    └─ Name: user.UserName
    ↓
Create Maxio Subscription
    └─ customer_id: Maxio customer ID
    └─ product_handle: "eshop-pro" (from request)
    └─ payment_collection_method: "automatic"
    ↓
Return subscription details to user
```

## Files Created/Modified

### New Files
- `src/ApplicationCore/Interfaces/IMaxioClient.cs` - Interface
- `src/Infrastructure/Services/MaxioClient.cs` - Implementation
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/ListMySubscriptionsEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansRequest.cs`
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansResponse.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionRequest.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionResponse.cs`
- `src/PublicApi/SubscriptionEndpoints/ListMySubscriptionsRequest.cs`
- `src/PublicApi/SubscriptionEndpoints/ListMySubscriptionsResponse.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionDto.cs`
- `MAXIO_SETUP.md` - Setup and testing guide
- `test-subscription-endpoints.sh` - Bash test script
- `test-subscription-endpoints.ps1` - PowerShell test script
- `IMPLEMENTATION_SUMMARY.md` - This file

### Modified Files
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/PublicApi/Program.cs` - Registered MaxioClient in DI container

## Building and Running

### Prerequisites
- .NET SDK (version 8.0+ or 10.0+ with rollForward enabled)
- Maxio sandbox account with credentials
- eShopOnWeb test user

### Build
```bash
cd repo
export DOTNET_ROLL_FORWARD=Major
dotnet build src/PublicApi/PublicApi.csproj
```

### Run
```bash
export DOTNET_ROLL_FORWARD=Major
export MAXIO_API_KEY="your_key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export UseOnlyInMemoryDatabase=true

cd src/PublicApi
dotnet run
```

### Test
```bash
# Bash
./test-subscription-endpoints.sh "demouser@microsoft.com" "Pass@word123"

# PowerShell
.\test-subscription-endpoints.ps1 -Username "demouser@microsoft.com" -Password "Pass@word123"

# Manual curl
curl https://localhost:25643/api/subscription-plans
```

## Security

### Secrets Management
- **Never stored in repository** - All credentials from environment variables
- **No hardcoded values** - Configuration uses read-only settings
- **JWT protected** - Authenticated endpoints require bearer token
- **Reference field** - User ID used as reference, never exposed in responses

### API Security
- HTTPS enforced (requires dev cert)
- JWT authentication on write/read operations
- Basic auth to Maxio (industry standard for billing APIs)
- No PCI compliance needed (payment methods not required for demo plans)

## Testing

### Verification Checklist
- [x] Project builds without errors
- [x] Endpoints register correctly
- [x] Configuration loads from environment
- [x] MaxioClient makes proper HTTP calls
- [x] Customer creation is idempotent
- [x] JWT authentication enforced
- [x] Test scripts validate end-to-end flow

### Sandbox Setup
Demo data already seeded in Maxio sandbox (cp-exp-2):

**Product Family**: `eshop-subscribe`
- **Pro Plan** (`eshop-pro`): $299.00/month, no trial, no payment required
- **Basic Plan** (`basic-plan`): $29.00/month, no trial, no payment required

**Component** (optional): `api-call` ($0.01/unit, metered)

## Production Readiness

### What's Production-Grade
✓ Comprehensive error handling and logging
✓ Idempotent operations (safe for retries)
✓ Proper async/await patterns
✓ Secure credential management
✓ Clean architecture with dependency injection
✓ Follows eShopOnWeb conventions
✓ Extensible design for future billing features

### Potential Enhancements (Out of Scope)
- Database persistence of subscription metadata
- Webhook handlers for Maxio events (cancellations, upgrades, etc.)
- Self-service subscription management (pause, upgrade, cancel)
- Usage reporting for metered components
- Invoice retrieval and rendering
- Refund processing
- Dunning/retry strategies

## Troubleshooting

### Build Issues
```bash
# SDK mismatch
export DOTNET_ROLL_FORWARD=Major
dotnet build src/PublicApi/PublicApi.csproj

# Database errors
export UseOnlyInMemoryDatabase=true
```

### Runtime Issues
```bash
# 401 Unauthorized (missing credentials)
export MAXIO_API_KEY="your_key"

# 404 Not Found (wrong subdomain)
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"

# HTTPS certificate errors
dotnet dev-certs https --trust
```

### API Issues
```bash
# No subscription plans returned
- Verify product family handle is correct
- Check Maxio sandbox has products seeded

# "User not found" error
- Ensure user exists in eShopOnWeb
- Check JWT token is from correct user

# Double subscription creation
- Integration is idempotent by design
- Each user gets one customer record
- Repeated subscriptions to same plan may be rejected by Maxio
```

## References

- Maxio API Documentation: See MAXIO_SETUP.md
- eShopOnWeb Reference Architecture: https://github.com/dotnet-architecture/eShopOnWeb
- PublicApi Endpoint Pattern: Ardalis.ApiEndpoints + MinimalApi.Endpoint
- ASP.NET Core Minimal APIs: https://docs.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis

## Summary

The Maxio subscription billing integration is complete, tested, and ready for verification. The implementation follows best practices for security, maintainability, and extensibility while remaining additive to eShopOnWeb's existing catalog/cart/order flow.

All credentials are environment-variable driven, the system is idempotent, and the endpoints follow RESTful conventions. The three public endpoints enable the hero flow: browse plans, subscribe, and view active subscriptions.
