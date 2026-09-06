# Maxio Subscription Billing Integration

This document describes how to verify the Maxio subscription billing integration for eShopOnWeb.

## Architecture Overview

The integration adds recurring subscription capabilities alongside the existing one-time commerce flow. Key components:

- **ApplicationCore**: Domain logic, Maxio client interface, subscription service
- **Infrastructure**: HttpClient implementation for Maxio API, entity configurations, database access
- **PublicApi**: Three JWT-authenticated REST endpoints for subscription management

## Setup

### Prerequisites

- .NET SDK 8.0+ (or .NET 10 SDK with `DOTNET_ROLL_FORWARD=Major`)
- Maxio Advanced Billing sandbox account with credentials
- The sandbox must have these seeded entities:
  - Product Family: `eshop-subscribe`
  - Plans: `eshop-pro` ($299/mo), `basic-plan` ($29/mo)

### 1. Obtain Credentials

Contact Maxio support or access your sandbox admin dashboard to get:
- `MAXIO_API_KEY`: Your API key for sandbox
- `MAXIO_SITE_SUBDOMAIN`: Your sandbox subdomain (e.g., "cp-exp-3")
- `MAXIO_DEFAULT_PRODUCT_FAMILY`: Product family handle (typically "eshop-subscribe")

### 2. Configure Environment

#### Option A: User Secrets (Development)

```bash
cd src/PublicApi

dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"
dotnet user-secrets set "Maxio:SiteSubdomain" "<your-subdomain>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

#### Option B: Environment Variables (Any Environment)

```bash
export MAXIO_API_KEY="<your-api-key>"
export MAXIO_SITE_SUBDOMAIN="<your-subdomain>"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

### 3. Run the Application

From the repository root:

```bash
# For development with in-memory database
$env:UseOnlyInMemoryDatabase="true"
$env:DOTNET_ROLL_FORWARD="Major"

dotnet run --project src/PublicApi/PublicApi.csproj
```

The PublicApi service will start on the port configured in `launchSettings.json` (default: `https://localhost:24863`).

## API Endpoints

All endpoints require JWT authentication. Get a token first:

```bash
# Authenticate (get JWT token)
curl -X POST https://localhost:24863/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

The response includes a `token` field. Use this token for subsequent requests.

### Endpoint 1: List Available Plans

```bash
curl -X GET https://localhost:24863/api/subscription-plans \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json"
```

**Response** (example):
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "$299 Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "priceInDollars": 299.00,
      "interval": 1,
      "intervalUnit": "month",
      "requireCreditCard": false
    },
    {
      "id": 7126958,
      "name": "$29 Basic Plan",
      "handle": "basic-plan",
      "priceInCents": 2900,
      "priceInDollars": 29.00,
      "interval": 1,
      "intervalUnit": "month",
      "requireCreditCard": false
    }
  ],
  "correlationId": "..."
}
```

### Endpoint 2: Create Subscription

```bash
curl -X POST https://localhost:24863/api/subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}'
```

**Response** (example):
```json
{
  "subscription": {
    "id": 15236915,
    "state": "active",
    "customerId": 14714298,
    "productName": "$299 Pro Plan",
    "productPriceInCents": 29900,
    "productPriceInDollars": 299.00,
    "currentPeriodEndsAt": "2026-10-06T14:48:10-05:00",
    "nextAssessmentAt": "2026-10-06T14:48:10-05:00",
    "activatedAt": "2026-09-06T14:48:12-05:00",
    "createdAt": "2026-09-06T14:48:10-05:00",
    "updatedAt": "2026-09-06T15:24:41-05:00"
  },
  "correlationId": "..."
}
```

### Endpoint 3: List User Subscriptions

```bash
curl -X GET https://localhost:24863/api/my-subscriptions \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json"
```

**Response** (example):
```json
{
  "subscriptions": [
    {
      "id": 15236915,
      "state": "active",
      "customerId": 14714298,
      "productName": "$299 Pro Plan",
      "productPriceInCents": 29900,
      "productPriceInDollars": 299.00,
      "currentPeriodEndsAt": "2026-10-06T14:48:10-05:00",
      "nextAssessmentAt": "2026-10-06T14:48:10-05:00",
      "activatedAt": "2026-09-06T14:48:12-05:00",
      "createdAt": "2026-09-06T14:48:10-05:00",
      "updatedAt": "2026-09-06T15:24:41-05:00"
    }
  ],
  "correlationId": "..."
}
```

## Verification Checklist

- [ ] Application builds without errors
- [ ] Credentials are properly loaded from secrets/environment
- [ ] Authentication endpoint returns a valid JWT token
- [ ] `GET /api/subscription-plans` returns the two seeded plans
- [ ] `POST /api/subscriptions` creates a new Maxio customer for the user (idempotent)
- [ ] `POST /api/subscriptions` creates a subscription to the specified plan
- [ ] Subsequent `POST /api/subscriptions` with same plan doesn't create duplicate
- [ ] `GET /api/my-subscriptions` returns all subscriptions for the logged-in user
- [ ] Second user can create their own subscription without conflicts
- [ ] Error handling works: invalid plan handle returns 400
- [ ] Error handling works: missing JWT token returns 401

## Key Features

### Idempotency
- Creating a subscription twice for the same plan doesn't create duplicates
- User ↔ Maxio customer mapping is stored locally
- Double-clicks on subscribe don't cause issues

### Security
- All endpoints require JWT authentication
- Secrets are read from environment variables or user-secrets (never hardcoded)
- Secrets never appear in repository, code, or logs

### Data Handling
- Maxio is the source of truth for subscription state
- Local database only stores user ↔ Maxio customer ID mapping
- In-memory database (development) doesn't persist across restarts

### Error Handling
- Maxio API errors are caught and converted to domain exceptions
- ExceptionMiddleware handles Maxio exceptions gracefully
- Client receives descriptive error messages with appropriate HTTP status codes

## Troubleshooting

### Build Issues

**Error: SDK mismatch**
```
global.json pins 8.0.x but .NET 10 is installed
```
Solution:
```bash
$env:DOTNET_ROLL_FORWARD="Major"
dotnet build
```

**Error: AddHttpClient not found**
Make sure you're building the PublicApi project, not just Infrastructure.

### Runtime Issues

**Error: No database**
```
(localdb)\mssqllocaldb doesn't exist
```
Solution:
```bash
$env:UseOnlyInMemoryDatabase="true"
dotnet run --project src/PublicApi/PublicApi.csproj
```

**Error: HTTPS certificate**
```
NET::ERR_CERT_AUTHORITY_INVALID
```
Solution:
```bash
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

**Error: Invalid credentials**
```
"message": "Billing service error: HTTP/401"
```
Solution: Verify credentials are correct and have access to the sandbox.

**Error: Plan not found**
```
"message": "Failed to get product by handle: invalid-plan"
```
Solution: Use a valid plan handle: `eshop-pro` or `basic-plan`

## Implementation Details

### Files Added

#### Entities
- `src/ApplicationCore/Entities/MaxioCustomerMapping.cs` - User ↔ Maxio customer mapping

#### Services & Interfaces
- `src/ApplicationCore/Interfaces/IMaxioClient.cs` - Maxio API contract
- `src/ApplicationCore/Interfaces/IMaxioSubscriptionService.cs` - Subscription business logic
- `src/ApplicationCore/Services/MaxioSubscriptionService.cs` - Service implementation
- `src/Infrastructure/Services/MaxioHttpClient.cs` - HTTP client implementation

#### Configuration
- `src/ApplicationCore/MaxioSettings.cs` - Configuration settings
- `src/Infrastructure/Data/Config/MaxioCustomerMappingConfiguration.cs` - EF configuration

#### Exceptions
- `src/ApplicationCore/Exceptions/MaxioApiException.cs`
- `src/ApplicationCore/Exceptions/MaxioCustomerCreationException.cs`
- `src/ApplicationCore/Exceptions/MaxioSubscriptionCreationException.cs`

#### Specifications
- `src/ApplicationCore/Specifications/MaxioCustomerMappingByUserIdSpecification.cs`
- `src/ApplicationCore/Specifications/MaxioCustomerMappingByCustomerIdSpecification.cs`

#### Endpoints
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
- `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs`

#### DTOs & Responses
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs`
- `src/PublicApi/SubscriptionEndpoints/SubscriptionDto.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionRequest.cs`
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionResponse.cs`
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansResponse.cs`
- `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsResponse.cs`

### Changes to Existing Files
- `src/Infrastructure/Data/CatalogContext.cs` - Added MaxioCustomerMappings DbSet
- `src/Infrastructure/Dependencies.cs` - Added Maxio configuration
- `src/PublicApi/Program.cs` - Added HttpClient registration and user-secrets loading
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/PublicApi/Middleware/ExceptionMiddleware.cs` - Added Maxio exception handling

## Production Considerations

For production deployment:

1. **Secrets Management**: Use cloud secrets (Azure Key Vault, AWS Secrets Manager, etc.)
2. **Database**: Use persistent SQL Server or other production database
3. **Logging**: Add structured logging to track Maxio interactions
4. **Monitoring**: Monitor subscription creation and renewal events
5. **Webhooks**: Consider implementing Maxio webhooks for real-time subscription events
6. **Rate Limiting**: Implement rate limiting on subscription endpoints
7. **Audit Trail**: Log all subscription-related operations for compliance

## API Contract Compliance

All interactions with Maxio follow the OpenAPI specification in `maxio-spec/openapi.yaml`:

- Authentication: Basic auth with API key
- Base URL: Derived from subdomain or explicit override
- Request/Response formats: JSON
- Error models: Maxio error schema
- Status codes: Follow HTTP conventions

