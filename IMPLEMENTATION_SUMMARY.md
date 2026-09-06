# Maxio Subscription Integration - Implementation Summary

## Overview

A complete, production-grade Maxio subscription billing integration has been added to the eShopOnWeb PublicApi project. This provides recurring-subscription capabilities as an additive feature to the existing commerce system.

## Architecture

### Core Components

1. **MaxioSettings** (`src/ApplicationCore/Services/MaxioSettings.cs`)
   - Configuration class for Maxio credentials
   - Loads from `Maxio:` section in configuration
   - Supports optional `BaseUrl` override for custom Maxio deployments
   - Default Base URL: `https://{Subdomain}.chargify.com`

2. **MaxioClient** (`src/ApplicationCore/Services/MaxioClient.cs`)
   - HTTP client wrapper for Maxio API
   - Implements Basic authentication (API key:x)
   - Automatic JSON serialization/deserialization
   - Comprehensive error logging
   - Returns null on failure (graceful degradation)

3. **SubscriptionService** (`src/ApplicationCore/Services/SubscriptionService.cs`)
   - Business logic for subscription operations
   - Idempotent customer creation (safe to retry)
   - Customer reference: `eshop_{userId}`
   - Methods:
     - `GetSubscriptionPlans()` - Fetch available plans
     - `EnsureCustomerExists()` - Create customer if needed
     - `CreateSubscription()` - Create new subscription
     - `GetUserSubscriptions()` - List user's subscriptions

4. **Endpoints** (3 new REST endpoints)
   - `GET /api/subscription-plans` - Public endpoint, no auth required
   - `POST /api/subscriptions` - Creates subscription, requires JWT auth
   - `GET /api/my-subscriptions` - Lists user subscriptions, requires JWT auth

### API Endpoints Details

#### GET /api/subscription-plans
Lists available subscription plans from Maxio.

- **Auth**: None required (AllowAnonymous)
- **Response**: Array of plans with handle, name, description, price, billing interval
- **Status Codes**: 200 OK

#### POST /api/subscriptions
Creates a new subscription for the authenticated user.

- **Auth**: Required (Bearer JWT)
- **Request Body**: `{ "productHandle": "eshop-pro" }`
- **Response**: Subscription details with ID, state, plan info, next billing date
- **Status Codes**: 
  - 201 Created (success)
  - 400 Bad Request (validation error)
  - 500 Internal Server Error (API error)

#### GET /api/my-subscriptions
Returns the authenticated user's subscriptions.

- **Auth**: Required (Bearer JWT)
- **Response**: Array of user subscriptions with full details
- **Status Codes**: 200 OK

## Configuration

### Environment Variables

Set these before running the application:

```powershell
$env:MAXIO_API_KEY = "your_api_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_sandbox_subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:MAXIO_BASE_URL = "optional_override"  # Only if using non-standard URL
```

### appsettings.json

The configuration is read from:

```json
{
  "Maxio": {
    "ApiKey": "loaded from env var",
    "Subdomain": "loaded from env var",
    "ProductFamilyHandle": "loaded from env var",
    "BaseUrl": "optional override"
  }
}
```

**Note**: Never commit actual credentials to version control. Use environment variables only.

### DI Registration

In `Program.cs`:

```csharp
builder.Services.AddSingleton(maxioSettings);
builder.Services.AddHttpClient<IMaxioClient, MaxioClient>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
```

## Maxio API Integration

### Authentication
- **Scheme**: HTTP Basic Auth
- **Format**: `api_key:x` (username is API key, password is literal "x")
- **Implementation**: MaxioClient sets header: `Authorization: Basic {base64(api_key:x)}`

### Key Endpoints Used

From `maxio-spec/openapi.yaml`:

| Operation | Endpoint | Purpose |
|-----------|----------|---------|
| List plans | `GET /product_families/handle:{handle}/products.json` | Fetch plans |
| Lookup customer | `GET /customers/lookup.json?reference={ref}` | Check if exists |
| Create customer | `POST /customers.json` | Create if needed |
| Create subscription | `POST /subscriptions.json` | Enroll in plan |
| List subscriptions | `GET /customers/{id}/subscriptions.json` | Get user subs |

### Request/Response Models

All models use snake_case JSON property names (standard for Maxio API):
- `ProductsResponse`, `SubscriptionsResponse` - Arrays
- `CustomerResponse`, `SubscriptionResponse` - Single entities
- `CustomerLookupResponse` - Lookup results
- `ErrorListResponse` - Error details

## Idempotency

The integration ensures idempotent operations:

1. **Customer Creation**:
   - First, lookup customer by reference `eshop_{userId}`
   - If found, return success (already exists)
   - If not found, create new customer
   - Safe to retry; never creates duplicates

2. **Subscription Creation**:
   - Always creates new subscription
   - References existing customer by reference
   - Maxio ensures billing doesn't overlap

## Error Handling

### Graceful Degradation

- **MaxioClient**: Returns null on HTTP error, logs details
- **SubscriptionService**: Returns null/false on error, logs with context
- **Endpoints**: Return appropriate HTTP status with error message

### Error Scenarios

| Scenario | Response | HTTP Status |
|----------|----------|------------|
| Auth token missing | "User not authenticated" | 400 |
| Invalid product handle | "Product handle is required" | 400 |
| Maxio API error | "Failed to create subscription" | 500 |
| Success | Full response | 201/200 |

### Logging

- All errors logged via `ILogger<T>`
- Error messages include status codes, response content
- Operation-level logging: customer create, subscription create
- No sensitive data logged (API key not logged)

## Production Considerations

### Security
- ✓ Credentials from environment variables only
- ✓ JWT authentication on protected endpoints
- ✓ No secrets in code or configuration files
- ✓ HTTPS enforced (UseHttpsRedirection)

### Resilience
- ✓ Idempotent customer creation
- ✓ Comprehensive error handling
- ✓ Graceful null returns on API failure
- ✓ Proper HTTP status codes

### Scalability
- ✓ Scoped service injection (thread-safe)
- ✓ Async/await throughout
- ✓ HttpClientFactory for connection pooling
- ✓ No in-memory caching issues

### Observability
- ✓ Structured logging with user/customer IDs
- ✓ CorrelationId in all responses
- ✓ Error details logged but not exposed to client

## Files Modified/Created

### New Files (7)
- `src/ApplicationCore/Services/MaxioSettings.cs`
- `src/ApplicationCore/Services/MaxioClient.cs`
- `src/ApplicationCore/Services/MaxioDtos.cs`
- `src/ApplicationCore/Services/SubscriptionService.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/MySubscriptionsEndpoint.cs`

### Modified Files (3)
- `src/PublicApi/Program.cs` - Added Maxio service registration
- `src/PublicApi/appsettings.json` - Added Maxio config section
- `global.json` - Set `rollForward: latestMajor`

### Documentation (3)
- `SUBSCRIPTION_INTEGRATION.md` - Feature overview
- `VERIFY_SUBSCRIPTIONS.md` - Testing instructions
- `IMPLEMENTATION_SUMMARY.md` - This file

### Testing (1)
- `test-subscriptions.ps1` - PowerShell test script

## Testing & Verification

### Quick Start

1. **Set credentials**:
   ```powershell
   $env:DOTNET_ROLL_FORWARD = "Major"
   $env:UseOnlyInMemoryDatabase = "true"
   $env:MAXIO_API_KEY = "your_key"
   $env:MAXIO_SITE_SUBDOMAIN = "your_subdomain"
   $env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
   ```

2. **Run PublicApi**:
   ```powershell
   dotnet run --project src/PublicApi/PublicApi.csproj
   ```

3. **Test endpoints**:
   ```powershell
   .\test-subscriptions.ps1
   ```

### Test Workflow

1. List plans (no auth)
2. Authenticate (get JWT token)
3. Check subscriptions (empty)
4. Create subscription
5. List subscriptions (has subscription)

See `VERIFY_SUBSCRIPTIONS.md` for detailed instructions.

## Build & Deployment

### Build
```powershell
dotnet build Everything.sln -c Debug
```

### Run
```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
$env:MAXIO_API_KEY = "key"
$env:MAXIO_SITE_SUBDOMAIN = "subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "family"

dotnet run --project src/PublicApi/PublicApi.csproj
```

### Environment Compatibility
- .NET SDK: 8.0.x with rollForward to latestMajor
- .NET Runtime: .NET 10 (from latest roll-forward)
- Database: In-memory for development (UseOnlyInMemoryDatabase=true)
- HTTPS: Required (dev cert configured)

## Future Enhancements

1. **Subscription Management**
   - Cancel subscription endpoint
   - Upgrade/downgrade plan
   - Pause subscription

2. **Webhook Integration**
   - Handle subscription lifecycle events
   - Sync billing data to local database
   - Handle payment failures

3. **Customer Portal**
   - Generate Maxio portal links
   - Self-service subscription management
   - Invoice history

4. **Database Persistence**
   - Cache subscription data locally
   - User-subscription mapping table
   - Billing history tracking

5. **Payment Integration**
   - Payment method management
   - Invoice delivery
   - Retry logic for failed payments

## Compliance

- **PCI DSS**: Not required (payment method captured by Maxio, not stored locally)
- **Data Protection**: No sensitive data logged or exposed
- **Audit Trail**: Operations logged with user/customer context

## Support & Troubleshooting

See `VERIFY_SUBSCRIPTIONS.md` for:
- Troubleshooting common issues
- Manual testing with cURL
- Verification checklist
- Error message reference

## Code Quality

- ✓ Follows eShopOnWeb patterns and conventions
- ✓ Minimal dependencies (uses standard HttpClient, DI)
- ✓ Comprehensive error handling
- ✓ Async/await throughout
- ✓ Testable design (interfaces for all services)
- ✓ No external nuget dependencies added
- ✓ Follows C# naming conventions
- ✓ Proper use of IDisposable/using statements

## Notes

- The integration is completely additive and does not modify existing commerce flow
- No database migrations required (uses in-memory DB in dev)
- All credentials loaded from environment variables (never in code)
- Maxio OpenAPI spec in `maxio-spec/openapi.yaml` is the authoritative contract
- Customer references use format `eshop_{userId}` for consistent identification
