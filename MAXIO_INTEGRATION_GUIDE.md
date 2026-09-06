# Maxio Subscription Billing Integration Guide

## Overview

This guide describes how to test the Maxio subscription billing integration added to eShopOnWeb. The integration allows authenticated users to:
- View available subscription plans
- Create subscriptions for available plans
- List their current subscriptions

## Architecture

### Components Added

1. **MaxioSettings** (`src/ApplicationCore/MaxioSettings.cs`)
   - Configuration container for Maxio API credentials and settings
   - Loads from configuration section or environment variables

2. **IMaxioBillingService & MaxioBillingService** 
   - (`src/ApplicationCore/Interfaces/IMaxioBillingService.cs`)
   - (`src/Infrastructure/Services/MaxioBillingService.cs`)
   - Handles all API calls to Maxio
   - Manages customer and subscription lifecycle

3. **Subscription Endpoints** (`src/PublicApi/SubscriptionEndpoints/`)
   - `SubscriptionPlansListEndpoint` - GET /api/subscription-plans
   - `SubscriptionCreateEndpoint` - POST /api/subscriptions
   - `SubscriptionListEndpoint` - GET /api/my-subscriptions
   - All endpoints require JWT authentication

## Environment Setup

### Prerequisites

- .NET 8+ SDK (application allows rollForward to .NET 10)
- Maxio sandbox account with API credentials
- eShopOnWeb demo site at Maxio (`cp-exp-3` or equivalent)

### Sandbox Entities Required

The following entities must exist in your Maxio sandbox:

| Entity | Handle | Notes |
|--------|--------|-------|
| Product Family | `eshop-subscribe` | Container for subscription plans |
| Pro Plan | `eshop-pro` | $299.00/month (or configured price) |
| Basic Plan | `basic-plan` | $29.00/month (or configured price) |

### Configuration

Configure Maxio credentials using **one** of these methods:

#### Option A: User Secrets (Recommended for Development)

```bash
cd src/PublicApi

# Initialize secrets (if not done)
dotnet user-secrets init

# Set Maxio configuration
dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
dotnet user-secrets set "Maxio:Environment" "sandbox"
```

#### Option B: Environment Variables

```bash
# Linux/Mac
export MAXIO_API_KEY=YOUR_API_KEY
export MAXIO_SITE_SUBDOMAIN=YOUR_SUBDOMAIN
export MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
export MAXIO_ENVIRONMENT=sandbox

# Windows PowerShell
$env:MAXIO_API_KEY = "YOUR_API_KEY"
$env:MAXIO_SITE_SUBDOMAIN = "YOUR_SUBDOMAIN"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:MAXIO_ENVIRONMENT = "sandbox"
```

#### Option C: appsettings.json (Not Recommended - Don't Commit Secrets)

Edit `src/PublicApi/appsettings.json`:

```json
{
  "Maxio": {
    "ApiKey": "YOUR_API_KEY",
    "Subdomain": "YOUR_SUBDOMAIN",
    "ProductFamilyHandle": "eshop-subscribe",
    "Environment": "sandbox",
    "BaseUrl": null
  }
}
```

### Optional: Custom Base URL

If you need to override the Maxio base URL:

```bash
# User Secrets
dotnet user-secrets set "Maxio:BaseUrl" "https://custom.chargify.com"

# Or environment variable
export MAXIO_BASE_URL=https://custom.chargify.com
```

## Testing the Integration

### 1. Start the PublicApi

```bash
cd src/PublicApi

# Development (uses in-memory database)
DOTNET_ROLL_FORWARD=Major dotnet run --launch-profile PublicApi

# Or using dotnet watch for auto-reload
DOTNET_ROLL_FORWARD=Major dotnet watch run --launch-profile PublicApi
```

The API will be available at:
- HTTPS: https://localhost:24943
- HTTP: http://localhost:24944
- Swagger: https://localhost:24943/swagger

### 2. Authenticate

Get a JWT token by authenticating with test credentials:

```bash
# Get token (replace with actual test credentials)
curl -X POST https://localhost:24943/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser","password":"Pass@word1"}' \
  -k

# Response should include a "token" field
```

### 3. List Subscription Plans

Using the token from step 2:

```bash
curl -X GET https://localhost:24943/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -k
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "success": true
}
```

### 4. Create a Subscription

```bash
curl -X POST https://localhost:24943/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k
```

Expected response:
```json
{
  "success": true,
  "subscriptionId": 15236915,
  "state": "active",
  "planHandle": "eshop-pro",
  "currentPeriodEndsAt": "2026-10-06T14:48:10-05:00",
  "nextAssessmentAt": "2026-10-06T14:48:10-05:00",
  "activatedAt": "2026-09-06T14:48:12-05:00",
  "message": "Subscription created successfully"
}
```

### 5. List User Subscriptions

```bash
curl -X GET https://localhost:24943/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -k
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 15236915,
      "state": "active",
      "planHandle": "eshop-pro",
      "planName": "Pro Plan",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month",
      "currentPeriodEndsAt": "2026-10-06T14:48:10-05:00",
      "nextAssessmentAt": "2026-10-06T14:48:10-05:00",
      "activatedAt": "2026-09-06T14:48:12-05:00",
      "createdAt": "2026-09-06T14:48:10-05:00"
    }
  ],
  "success": true
}
```

## How It Works

### Customer Creation/Lookup

When creating a subscription:
1. The system looks up existing Maxio customer by userId (as reference)
2. If not found, creates a new customer with minimal required fields
3. Uses the customer ID to create the subscription

### Subscription Creation

The subscription is created with the following settings:
- Payment collection method: `remittance` (payment not required at signup)
- Product identified by handle
- No trial period
- No setup fees
- No credit card required

### Error Handling

The service provides detailed error messages for:
- Invalid plan handle
- Customer creation failures
- API communication errors
- Invalid configuration

## Implementation Details

### Key Files

- `src/ApplicationCore/MaxioSettings.cs` - Configuration model
- `src/ApplicationCore/Interfaces/IMaxioBillingService.cs` - Service interface and DTOs
- `src/Infrastructure/Services/MaxioBillingService.cs` - Implementation
- `src/PublicApi/SubscriptionEndpoints/` - API endpoints
- `src/PublicApi/Program.cs` - Service registration
- `global.json` - SDK version (allows .NET 10 rollForward)

### Dependencies

- HttpClient (built-in)
- System.Text.Json (built-in)
- No external billing libraries (direct Maxio API)

### Authentication

All endpoints require Bearer token authentication. The token must include the user's ID in the `sub` claim.

## Troubleshooting

### "Failed to retrieve subscription plans"

**Cause**: Maxio API credentials invalid or network unreachable

**Solution**:
1. Verify credentials in Maxio dashboard
2. Check network connectivity to Maxio servers
3. Verify ProductFamilyHandle matches actual family in Maxio

### "Failed to create subscription"

**Cause**: Invalid plan handle or customer creation issue

**Solution**:
1. Verify plan handle exists in Maxio
2. Check Maxio logs for customer creation errors
3. Ensure payment_collection_method is set to "remittance"

### "User identification failed"

**Cause**: JWT token missing or invalid

**Solution**:
1. Ensure Authorization header format: `Authorization: Bearer TOKEN`
2. Verify token is valid and not expired
3. Check token includes user ID in claims

### API returns 500 error

**Solution**:
1. Check PublicApi application logs for detailed error
2. Verify Maxio configuration is loaded correctly
3. Verify network connectivity to Maxio sandbox

## Production Considerations

1. **Error Handling**: Add retry logic for transient failures
2. **Logging**: Enhanced logging for audit trails
3. **Customer Deduplication**: Implement more robust customer lookup
4. **Webhook Integration**: Handle subscription lifecycle events from Maxio
5. **PCI Compliance**: Use Chargify.js for credit card collection (if payment method required)
6. **Rate Limiting**: Implement rate limiting for API endpoints
7. **Monitoring**: Add observability for subscription operations

## API Specification

All endpoints follow REST conventions and return JSON responses.

### Common Response Format

**Success (2xx)**:
```json
{
  "success": true,
  "data": {}
}
```

**Error (4xx/5xx)**:
```json
{
  "error": "Error message",
  "message": "Detailed error context"
}
```

## References

- Maxio API: https://docs.maxio.com/
- OpenAPI Specification: `maxio-spec/openapi.yaml`
- eShopOnWeb Architecture: https://github.com/dotnet-architecture/eShopOnWeb
