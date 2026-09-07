# Maxio Subscription Billing Integration Guide

## Overview
This guide documents the subscription billing integration added to eShopOnWeb using Maxio Advanced Billing. The integration allows users to browse and subscribe to recurring billing plans.

## Architecture

### Files Added

**Configuration & Services:**
- `src/PublicApi/MaxioConfiguration.cs` - Configuration model for Maxio credentials
- `src/PublicApi/Services/IMaxioService.cs` - Service interface and DTOs
- `src/PublicApi/Services/MaxioService.cs` - Maxio API client implementation

**Endpoints:**
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` - GET /api/subscription-plans
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` - POST /api/subscriptions
- `src/PublicApi/SubscriptionEndpoints/ListMySubscriptionsEndpoint.cs` - GET /api/my-subscriptions

**Configuration:**
- Updated `src/PublicApi/appsettings.json` - Added Maxio config section
- Updated `src/PublicApi/Program.cs` - Registered MaxioService

### API Endpoints

All endpoints require JWT authentication.

#### 1. List Available Subscription Plans
```
GET /api/subscription-plans
Authorization: Bearer {jwt-token}
```

**Response:**
```json
{
  "correlationId": "guid",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan",
      "priceInCents": 29900,
      "priceFormatted": "$299.00",
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

#### 2. Create a Subscription
```
POST /api/subscriptions
Authorization: Bearer {jwt-token}
Content-Type: application/json

{
  "productHandle": "eshop-pro",
  "correlationId": "guid"
}
```

**Response:**
```json
{
  "correlationId": "guid",
  "subscription": {
    "id": 12345,
    "customerId": 67890,
    "productHandle": "eshop-pro",
    "productName": "Pro Plan",
    "state": "active",
    "activatedAt": "2026-09-07T12:00:00Z",
    "nextBillingAt": "2026-10-07T12:00:00Z",
    "currentPeriodAmountInCents": 29900,
    "currentPeriodAmountFormatted": "$299.00"
  }
}
```

#### 3. List My Subscriptions
```
GET /api/my-subscriptions
Authorization: Bearer {jwt-token}
```

**Response:**
```json
{
  "correlationId": "guid",
  "subscriptions": [
    {
      "id": 12345,
      "customerId": 67890,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "state": "active",
      "activatedAt": "2026-09-07T12:00:00Z",
      "nextBillingAt": "2026-10-07T12:00:00Z",
      "currentPeriodAmountInCents": 29900,
      "currentPeriodAmountFormatted": "$299.00"
    }
  ]
}
```

## Setup Instructions

### 1. Configure Maxio Credentials

Set environment variables:
```powershell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-3"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:MAXIO_ENVIRONMENT = "sandbox"
```

### 2. Initialize User Secrets

Run the setup script from the repository root:
```powershell
.\setup-maxio-secrets.ps1
```

Or manually configure:
```powershell
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-3"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### 3. Environment Configuration

For development/testing:
```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
```

### 4. Run the Application

```powershell
cd src/PublicApi
dotnet run
```

The PublicApi will be available at `https://localhost:28303`.

## Testing the Integration

### 1. Authenticate

First, get a JWT token:
```bash
curl -X POST https://localhost:28303/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

Copy the `token` from the response.

### 2. List Subscription Plans

```bash
curl -X GET https://localhost:28303/api/subscription-plans \
  -H "Authorization: Bearer {token}"
```

Expected: Returns list of available plans (Pro Plan, Basic Plan)

### 3. Create a Subscription

```bash
curl -X POST https://localhost:28303/api/subscriptions \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

Expected: 201 Created with subscription details

### 4. View Your Subscriptions

```bash
curl -X GET https://localhost:28303/api/my-subscriptions \
  -H "Authorization: Bearer {token}"
```

Expected: Returns list of user's subscriptions

## Key Implementation Details

### Customer Idempotency
- Customers are created with a reference to the eShopOnWeb userId
- The service automatically returns existing customers if they already exist
- This prevents duplicate customer creation on multiple subscription attempts

### Authentication
- All endpoints require JWT bearer authentication
- User identity is extracted from JWT claims:
  - `NameIdentifier` (sub) → userId for Maxio customer reference
  - `Email` → customer email
  - `FirstName`, `LastName` → customer names (defaults to "Customer" if not provided)

### Error Handling
- Graceful error responses for API failures
- Invalid product handles return 400 Bad Request
- Missing authentication returns 401 Unauthorized

### Configuration
- All Maxio credentials come from user-secrets in development
- Supports both subdomain-based URLs and custom BaseUrl
- Configuration from `appsettings.json` can be overridden by environment variables

## Maxio API Integration

The service uses the Maxio Billing API with basic authentication:
- `GET /products.json` - List available products/plans
- `GET /customers/lookup.json` - Find customer by reference
- `POST /customers.json` - Create new customer
- `POST /subscriptions.json` - Create subscription
- `GET /customers/{id}/subscriptions.json` - List customer subscriptions

All API calls use JSON serialization with direct HTTP requests to maintain compatibility with .NET Standard patterns.

## Sandbox Testing

The integration is configured for the Maxio sandbox environment (`cp-exp-3`). Available plans:

| Plan | Handle | Price | ID |
|------|--------|-------|-----|
| Pro Plan | eshop-pro | $299.00/mo | 7126957 |
| Basic Plan | basic-plan | $29.00/mo | 7126958 |

**Note:** Numeric IDs are reassigned on sandbox re-seed, but handles remain stable.

## Security Considerations

- API credentials are stored in user-secrets, never in repository files
- JWT tokens are required for all subscription endpoints
- HTTPS is enforced for development and production
- No credit card information is transmitted through this API (payment not required for these plans)

## Troubleshooting

### "Failed to create customer" error
- Verify Maxio API key and subdomain are correctly set in user-secrets
- Check that the API key has permissions to create customers and subscriptions

### "Product handle not found" error
- Verify the product handle exists in your Maxio site
- Sandbox plans: `eshop-pro`, `basic-plan`

### SSL Certificate Issues
- Trust the development certificate: `dotnet dev-certs https --trust`

### Port Conflicts
- Ensure port 28303 is available for PublicApi
- Check `launchSettings.json` for custom port configuration

## Future Enhancements

- Add subscription management endpoints (upgrade, downgrade, cancel)
- Implement webhook support for subscription events
- Add metered component usage tracking
- Support multiple billing cycles and trial periods
- Add discount/coupon application
