# Maxio Subscription Billing Integration - Setup & Verification Guide

## Overview

This guide explains how to set up and verify the Maxio subscription billing integration in eShopOnWeb.

## Configuration

### Environment Variables

The subscription feature requires the following environment variables:

- `MAXIO_API_KEY` - Your Maxio API key (from the Maxio sandbox/production site)
- `MAXIO_SITE_SUBDOMAIN` - Your Maxio site subdomain (e.g., `cp-exp-3` for sandbox)
- `MAXIO_DEFAULT_PRODUCT_FAMILY` - The product family handle (e.g., `eshop-subscribe`)
- `MAXIO_BASE_URL` (optional) - Override the Maxio API base URL; if not set, uses `https://{subdomain}.chargify.com`

### Application Settings

The application is configured via the `Maxio` section in `appsettings.*.json`:

```json
{
  "Maxio": {
    "Subdomain": "cp-exp-3",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": ""
  }
}
```

## Running the Application

### Prerequisites

- .NET 8.0+ SDK (application will roll forward as needed)
- For in-memory database (default in Development): No additional setup
- For SQL Server: Either LocalDB or connection string configured

### Local Development

Set the required environment variables and run:

```bash
cd src/PublicApi

# Set environment variables
export ASPNETCORE_ENVIRONMENT=Development
export MAXIO_API_KEY=your_api_key_here
export MAXIO_SITE_SUBDOMAIN=cp-exp-3
export MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe

# Run the application
dotnet run
```

The API will be available at `https://localhost:25103` (adjust based on `launchSettings.json`).

## API Endpoints

### 1. Get Available Subscription Plans

```http
GET /api/subscription-plans
Authorization: Bearer <jwt_token>
```

**Response:**
```json
{
  "correlationId": "uuid",
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription plan",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 2. Create a Subscription

```http
POST /api/subscriptions
Authorization: Bearer <jwt_token>
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "correlationId": "uuid",
  "subscriptionId": 123456,
  "customerId": 987654,
  "state": "active",
  "currentPeriodEndsAt": "2026-10-06T12:00:00Z",
  "nextAssessmentAt": "2026-10-06T12:00:00Z",
  "activatedAt": "2026-09-06T10:30:00Z"
}
```

### 3. Get My Subscriptions

```http
GET /api/my-subscriptions
Authorization: Bearer <jwt_token>
```

**Response:**
```json
{
  "correlationId": "uuid",
  "subscriptions": [
    {
      "id": 123456,
      "state": "active",
      "currentPeriodEndsAt": "2026-10-06T12:00:00Z",
      "nextAssessmentAt": "2026-10-06T12:00:00Z",
      "activatedAt": "2026-09-06T10:30:00Z",
      "createdAt": "2026-09-06T10:30:00Z",
      "product": {
        "id": 7126957,
        "name": "Pro Plan",
        "handle": "eshop-pro",
        "description": "Professional subscription plan",
        "price": 299.00,
        "interval": 1,
        "intervalUnit": "month"
      }
    }
  ]
}
```

## Authentication

The subscription endpoints require JWT authentication (except `/api/subscription-plans` which is public):

1. Get a token from `/api/authenticate`:
```bash
curl -X POST https://localhost:25103/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

2. Use the returned token in the `Authorization: Bearer <token>` header

## Key Features

### Idempotent Customer Creation
When a user subscribes, the system:
1. Checks if a Maxio customer already exists for that user (using the user ID as the reference)
2. If not, creates a new customer with the user's email and name
3. Proceeds to create the subscription

This ensures that repeated subscription requests don't create duplicate customers.

### Maxio Integration
- Uses Maxio's OpenAPI specification as the authoritative contract
- Authenticates with Basic Auth (API key as username, 'x' as password)
- Handles JSON request/response payloads
- Gracefully handles API errors and missing configuration

## Verification Steps

### Step 1: Build the Application
```bash
cd src/PublicApi
dotnet build
```

### Step 2: Get a JWT Token
```bash
TOKEN=$(curl -s -X POST https://localhost:25103/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | jq -r '.token')
```

### Step 3: Verify Endpoints in Swagger
Navigate to: `https://localhost:25103/swagger/index.html`

You should see three new endpoints under "Subscriptions":
- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

### Step 4: Test Subscription Plans Endpoint
```bash
curl -X GET https://localhost:25103/api/subscription-plans -k \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json"
```

### Step 5: Test Create Subscription Endpoint
```bash
curl -X POST https://localhost:25103/api/subscriptions -k \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

### Step 6: Verify Subscription in Maxio
1. Log in to your Maxio site (https://cp-exp-3.chargify.com)
2. Navigate to Customers
3. Find the customer with reference matching the user ID (demouser@microsoft.com → correlation ID)
4. Verify the subscription is listed under their subscriptions

## Troubleshooting

### "API key is not configured"
- Ensure `MAXIO_API_KEY` environment variable is set
- Verify credentials are valid for the specified sandbox site

### "401 Unauthorized" from Maxio
- Check that the API key is correct
- Verify the site subdomain is correct (should be `cp-exp-3` for sandbox)
- Ensure the Basic Auth header is properly formed (key:x in base64)

### "Product not found"
- Verify the product family handle matches (`eshop-subscribe`)
- Confirm products exist in the product family
- Check that `productHandle` in the request matches an actual product (e.g., `eshop-pro`)

### "Customer reference already exists"
- This is normal and expected behavior
- The system will use the existing customer if one already exists for the user

## Production Deployment

For production deployment:

1. **Secrets Management**
   - Use Azure Key Vault, AWS Secrets Manager, or your platform's secrets service
   - Load `MAXIO_API_KEY` and other credentials from secrets
   - Never commit credentials to the repository

2. **Configuration**
   - Update `MAXIO_SITE_SUBDOMAIN` to your production site subdomain
   - Adjust `MAXIO_DEFAULT_PRODUCT_FAMILY` if different from `eshop-subscribe`
   - Set appropriate `MAXIO_BASE_URL` if using EU hosting

3. **Database**
   - Use SQL Server or your configured database for production
   - Ensure proper connection string configuration
   - Run migrations before deployment

4. **Error Handling**
   - Implement proper error handling and logging
   - Log failed Maxio API calls for debugging
   - Provide user-friendly error messages

## Architecture Notes

### Service Layer
- `IMaxioService` interface in `ApplicationCore/Interfaces/IMaxioService.cs`
- Implementation in `ApplicationCore/Services/MaxioService.cs`
- HTTP client configured with Basic Auth and JSON content type

### Endpoints
- Located in `PublicApi/SubscriptionEndpoints/`
- Follow MinimalApi.Endpoint pattern used throughout eShopOnWeb
- Registered automatically via `app.MapEndpoints()` in `Program.cs`

### Configuration
- `MaxioSettings` class in `ApplicationCore/MaxioSettings.cs`
- Bound to `Maxio:*` configuration section
- Environment variables with `MAXIO_` prefix override configuration files

## Future Enhancements

- Subscription management (pause, resume, cancel)
- Webhook handling for Maxio events (subscription state changes, failed payments)
- Subscription history and invoice tracking
- Plan migration and proration
- Integration with eShopOnWeb's basket/order system for one-time purchases alongside recurring subscriptions
