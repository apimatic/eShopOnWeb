# Maxio Subscription Billing Integration

## Overview

This integration adds recurring-subscription billing to the eShopOnWeb reference app using Maxio Advanced Billing as the system of record. The capability is **additive and parallel** to the existing cart/checkout flow.

## Architecture

### Components

- **MaxioConfiguration** (`src/Infrastructure/MaxioConfiguration.cs`) - Configuration model for Maxio credentials
- **MaxioHttpClient** (`src/Infrastructure/Services/MaxioHttpClient.cs`) - HTTP client for Maxio API communication
- **IMaxioSubscriptionService** / **MaxioSubscriptionService** - Business logic for subscription operations
- **SubscriptionEndpoints** - Three JWT-authenticated REST endpoints

### Data Flow

1. User authenticates to PublicApi using JWT token
2. Requests are made to subscription endpoints with JWT bearer token
3. Endpoints extract user identity from JWT claims (NameIdentifier as userId)
4. Service creates/retrieves Maxio customer using user ID as reference (for idempotency)
5. Service manages subscriptions in Maxio, returns state to caller

## Setup

### Environment Variables

Set these environment variables before running the application:

```bash
MAXIO_API_KEY=<your-sandbox-api-key>
MAXIO_SITE_SUBDOMAIN=<your-site-subdomain>
MAXIO_ENVIRONMENT=sandbox
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

**Note**: Values are loaded from environment variables into `.NET User Secrets` during configuration. Never commit secrets to the repository.

### Configuration Files

The `appsettings.json` file has placeholders (empty strings) for Maxio configuration. At runtime:
1. `Program.cs` calls `AddEnvironmentVariables()` early in configuration
2. `Dependencies.ConfigureServices()` binds the `Maxio` section from configuration
3. Environment variables override `appsettings.json` values

### Database

The integration uses the existing in-memory database for user authentication and mapping. In production, you would want to persist the Maxio customer ID to avoid creating duplicate customers on each request (though the `customer_reference` prevents actual duplicates in Maxio).

## API Endpoints

All endpoints are under the `/api/` path and require JWT authentication (except GetSubscriptionPlans which is public).

### 1. GET /api/subscription-plans

**Public endpoint** (no authentication required)

Lists available subscription plans from the configured product family.

**Response:**
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "pricePerMonth": 299.00,
      "description": "Full features"
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "pricePerMonth": 29.00,
      "description": "Essential features"
    }
  ]
}
```

### 2. POST /api/subscriptions

**Requires JWT authentication**

Creates a new subscription for the authenticated user.

**Request:**
```json
{
  "planHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "subscriptionId": 12345,
  "state": "active",
  "productName": "Pro Plan",
  "productHandle": "eshop-pro",
  "pricePerMonth": 299.00,
  "nextBillingDate": "2026-10-06T04:03:00Z"
}
```

**Behavior:**
- Idempotent: calling multiple times with same user doesn't create duplicate subscriptions
- Creates Maxio customer using `user_id` as reference (unique within site)
- Creates subscription linked to that customer
- Plans without payment methods don't require payment profile

### 3. GET /api/my-subscriptions

**Requires JWT authentication**

Lists all active subscriptions for the authenticated user.

**Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "pricePerMonth": 299.00,
      "currentPeriodStartsAt": "2026-09-06T04:03:00Z",
      "currentPeriodEndsAt": "2026-10-06T04:03:00Z",
      "nextBillingDate": "2026-10-06T04:03:00Z"
    }
  ]
}
```

## Running and Testing

### Prerequisites

- .NET SDK 10+ (or .NET 8+ with `DOTNET_ROLL_FORWARD=Major`)
- ASP.NET Core 8.0 runtime (if using .NET 10 SDK)
- Maxio sandbox credentials (site `cp-exp-2` already has plans seeded)

### Running the Application

From the repo root:

```bash
# Set environment variables
export MAXIO_API_KEY=<your-key>
export MAXIO_SITE_SUBDOMAIN=<your-subdomain>
export MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
export UseOnlyInMemoryDatabase=true

# Run PublicApi
DOTNET_ROLL_FORWARD=Major dotnet run --project src/PublicApi
```

This starts PublicApi on `https://localhost:XXXX` (check console for port).

### Testing Endpoints

#### 1. Get Plans (No Auth)

```bash
curl -X GET https://localhost:5001/api/subscription-plans \
  -H "Content-Type: application/json" \
  --insecure
```

#### 2. Authenticate

First, get a JWT token:

```bash
curl -X POST https://localhost:5001/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word123"}' \
  --insecure
```

Copy the `token` value from response.

#### 3. Create Subscription (Authenticated)

```bash
curl -X POST https://localhost:5001/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure
```

#### 4. Get My Subscriptions (Authenticated)

```bash
curl -X GET https://localhost:5001/api/my-subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  --insecure
```

## Verification Checklist

- [ ] Application builds without errors
- [ ] PublicApi starts with environment variables set
- [ ] Can retrieve subscription plans without authentication
- [ ] Can authenticate with valid credentials
- [ ] Can create subscription with valid JWT token
- [ ] Creating subscription returns correct plan details
- [ ] Can retrieve user's subscriptions with JWT token
- [ ] Subscription state shows as "active"
- [ ] Calling create subscription twice doesn't create duplicates in Maxio
- [ ] All responses include correlation IDs for tracing

## Production Considerations

### Security
- Never commit API keys or credentials to version control
- Use .NET User Secrets in development
- In production, use secure environment variable injection or key vaults (Azure Key Vault, AWS Secrets Manager)
- Consider rate limiting on subscription endpoints
- Add request validation for plan handles

### Database Persistence
- Current implementation uses in-memory database which loses data on restart
- For production, persist user ↔ Maxio customer mapping in SQL database
- Add caching layer for subscription plans (they change infrequently)

### Error Handling
- Current errors return HTTP 500 with Maxio error messages
- Consider mapping specific Maxio errors to appropriate HTTP status codes
- Add structured logging for troubleshooting

### Monitoring
- Log all Maxio API calls (timestamps, endpoints, status codes)
- Track subscription creation success/failure rates
- Monitor API latency to Maxio
- Alert on failed payment renewals

## Troubleshooting

### "Maxio customer creation failed"
- Verify MAXIO_API_KEY is set correctly
- Check MAXIO_SITE_SUBDOMAIN matches your sandbox site
- Ensure product family handle matches configuration

### "Plan not found"
- Verify plan handle exactly matches Maxio product handle
- Check product family contains the plan
- Ensure product family is configured in MAXIO_DEFAULT_PRODUCT_FAMILY

### JWT Token Errors
- Token may be expired (check issue/expires claims)
- Verify Authorization header uses format: `Bearer <token>`
- Check token includes required claims (NameIdentifier, Email)

## Files Modified

- `src/Infrastructure/Dependencies.cs` - Added Maxio service registration
- `src/Infrastructure/MaxioConfiguration.cs` - Configuration model
- `src/Infrastructure/MaxioDto.cs` - Request/response DTOs
- `src/Infrastructure/Services/MaxioHttpClient.cs` - HTTP client
- `src/Infrastructure/Services/MaxioSubscriptionService.cs` - Business logic
- `src/ApplicationCore/Interfaces/IMaxioSubscriptionService.cs` - Service interface
- `src/PublicApi/Program.cs` - Configuration loading order
- `src/PublicApi/appsettings.json` - Config placeholders
- `src/PublicApi/SubscriptionEndpoints/*` - Three new endpoint classes
- `Directory.Packages.props` - Added Microsoft.Extensions.Http package
