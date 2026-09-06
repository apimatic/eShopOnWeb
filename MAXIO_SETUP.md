# Maxio Subscription Billing Integration - Setup Guide

## Overview

This guide explains how to set up and verify the Maxio subscription billing integration in eShopOnWeb.

## Prerequisites

- .NET 10 SDK with `DOTNET_ROLL_FORWARD=Major` environment variable
- Maxio sandbox credentials (API Key, Subdomain)
- JWT token for testing (from the `/api/authenticate` endpoint)

## Configuration Setup

### 1. Set Maxio Credentials via User Secrets

The PublicApi project uses .NET User Secrets to store sensitive configuration. Never commit credentials to the repository.

Set the Maxio configuration:

```powershell
# Navigate to the PublicApi project
cd src/PublicApi

# Set the Maxio API Key
dotnet user-secrets set "Maxio:ApiKey" "your_api_key_here"

# Set the Maxio Subdomain (e.g., "cp-exp-2")
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"

# Set the Product Family Handle (e.g., "eshop-subscribe")
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# (Optional) Override the base URL if not using standard Maxio subdomain
# dotnet user-secrets set "Maxio:BaseUrl" "https://custom-url.com/api"
```

### 2. Environment Variables

The application reads from environment variables as fallback:
- `MAXIO_API_KEY` → `Maxio:ApiKey`
- `MAXIO_SITE_SUBDOMAIN` → `Maxio:Subdomain`
- `MAXIO_DEFAULT_PRODUCT_FAMILY` → `Maxio:ProductFamilyHandle`

Alternatively, set these environment variables before running:

```powershell
$env:MAXIO_API_KEY = "your_api_key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

## Running the PublicApi

```powershell
# Set required environment variables
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"

# Run the PublicApi project
cd src/PublicApi
dotnet run

# The API will start on https://localhost:25563
```

The Swagger documentation will be available at:
- https://localhost:25563/swagger/index.html

## API Endpoints

All subscription endpoints require JWT authentication.

### 1. Get Subscription Plans

**Endpoint:** `GET /api/subscription-plans`

**Headers:**
```
Authorization: Bearer {jwt_token}
```

**Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription plan",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 2. Create Subscription

**Endpoint:** `POST /api/subscriptions`

**Headers:**
```
Authorization: Bearer {jwt_token}
Content-Type: application/json
```

**Request Body:**
```json
{
  "planHandle": "eshop-pro",
  "firstName": "John",
  "lastName": "Doe",
  "email": "john.doe@example.com"
}
```

**Response:**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productName": "Pro Plan",
  "price": 299.00,
  "nextBillingAt": "2026-10-06T12:00:00Z",
  "message": "Subscription created successfully"
}
```

### 3. Get User Subscriptions

**Endpoint:** `GET /api/my-subscriptions`

**Headers:**
```
Authorization: Bearer {jwt_token}
```

**Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "price": 299.00,
      "balance": 0.00,
      "currentPeriodEndsAt": "2026-10-06T12:00:00Z",
      "nextBillingAt": "2026-10-06T12:00:00Z",
      "createdAt": "2026-09-06T12:00:00Z"
    }
  ],
  "message": null
}
```

## Testing the Integration

### 1. Get a JWT Token

```bash
curl -X POST https://localhost:25563/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

This returns:
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com"
}
```

### 2. List Available Plans

```bash
curl https://localhost:25563/api/subscription-plans \
  -H "Authorization: Bearer {your_jwt_token}" \
  -k
```

### 3. Create a Subscription

```bash
curl -X POST https://localhost:25563/api/subscriptions \
  -H "Authorization: Bearer {your_jwt_token}" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro",
    "firstName": "Test",
    "lastName": "User",
    "email": "test@example.com"
  }' \
  -k
```

### 4. Get User Subscriptions

```bash
curl https://localhost:25563/api/my-subscriptions \
  -H "Authorization: Bearer {your_jwt_token}" \
  -k
```

## Important Notes

### Idempotency

- Subscriptions are created using the authenticated user's ID as the `customer_reference` in Maxio
- If a subscription already exists for the user with the same plan, attempting to create another will create a duplicate (Maxio allows multiple subscriptions per customer)
- To check existing subscriptions, use `GET /api/my-subscriptions`

### Database

- This integration uses an in-memory database for the in-memory configuration
- Subscription data is persisted in Maxio (the billing system of record)
- Local userId ↔ Maxio customerId mappings are stored in memory and will be lost on restart

### Payment

- The demo plans (`eshop-pro`, `basic-plan`) do not require payment method entry
- Production use requires proper payment method handling

### Error Handling

API errors include descriptive messages:
```json
{
  "subscriptionId": 0,
  "state": "",
  "productName": "",
  "price": 0,
  "nextBillingAt": null,
  "message": "Error: {error_details}"
}
```

## Production Considerations

1. **Secrets Management:** Use Azure Key Vault or similar in production
2. **Persistent Database:** Replace in-memory database with SQL Server
3. **Error Logging:** Implement comprehensive error logging and monitoring
4. **Webhook Handling:** Implement Maxio webhooks for subscription lifecycle events
5. **Billing Portal:** Consider exposing Maxio's Self-Service Billing Portal
6. **Payment Methods:** Implement proper payment method collection (e.g., via Billing.js)
7. **Audit Trail:** Log all subscription operations for compliance

## Troubleshooting

### "Maxio API Key not configured"
- Verify user-secrets are set: `dotnet user-secrets list`
- Check environment variables are set

### "Unauthorized" errors
- Verify JWT token is valid and not expired
- Check Authorization header format: `Bearer {token}`

### "Customer not found" on GET subscriptions
- This is expected if the user hasn't created any subscriptions yet
- Endpoint returns empty list instead of error

### Subscription creation fails
- Check that the plan handle is correct and exists in Maxio
- Verify Maxio credentials and network connectivity
- Check Swagger documentation for detailed error responses

## Architecture

The integration consists of:

- **MaxioConfiguration:** Settings for API credentials
- **MaxioClient:** HTTP client for Maxio API calls with basic auth
- **MaxioService:** Business logic for subscription operations
- **Endpoints:**
  - ListSubscriptionPlansEndpoint: `/api/subscription-plans`
  - CreateSubscriptionEndpoint: `POST /api/subscriptions`
  - ListUserSubscriptionsEndpoint: `/api/my-subscriptions`

All endpoints require JWT authentication and extract user identity from JWT claims.
