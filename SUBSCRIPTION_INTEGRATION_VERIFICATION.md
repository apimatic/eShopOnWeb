# eShopOnWeb Subscription Billing Integration - Verification Guide

This guide provides step-by-step instructions to verify the Maxio subscription billing integration is working correctly.

## Prerequisites

- .NET SDK 8.0.x (or 10.x with `DOTNET_ROLL_FORWARD=Major`)
- The application built successfully
- Environment variables configured:
  - `MAXIO_API_KEY`: Your Maxio sandbox API key
  - `MAXIO_SITE_SUBDOMAIN`: Sandbox site (e.g., `cp-exp-1`)
  - `MAXIO_ENVIRONMENT`: Environment (e.g., `US`)
  - `MAXIO_DEFAULT_PRODUCT_FAMILY`: Product family handle (e.g., `eshop-subscribe`)

## Environment Setup

1. **Set environment variables:**
   ```bash
   # Windows PowerShell
   $env:MAXIO_API_KEY = "your-api-key"
   $env:MAXIO_SITE_SUBDOMAIN = "cp-exp-1"
   $env:MAXIO_ENVIRONMENT = "US"
   $env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
   $env:UseOnlyInMemoryDatabase = "true"
   ```

2. **Navigate to the project:**
   ```bash
   cd src\PublicApi
   ```

## Starting the Application

3. **Run the PublicApi server:**
   ```bash
   dotnet run
   ```
   
   The server should start at:
   - API: `https://localhost:28163/api/`
   - Swagger: `https://localhost:28163/swagger`

## Testing the Integration

### 1. Authenticate and Get a Bearer Token

```bash
# POST to authenticate endpoint (no auth required)
curl -X POST "https://localhost:28163/api/authenticate" `
  -H "Content-Type: application/json" `
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' `
  -k  # Allow self-signed cert for development

# Response will contain a "token" field - copy this value
# You'll use it as: Authorization: Bearer <token>
```

**Expected Response:**
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "username": "admin@microsoft.com",
  ...
}
```

### 2. List Available Subscription Plans

```bash
curl -X GET "https://localhost:28163/api/subscription-plans" `
  -H "Authorization: Bearer YOUR_TOKEN" `
  -k
```

**Expected Response (200 OK):**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month",
      "handle": "eshop-pro"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month",
      "handle": "basic-plan"
    }
  ]
}
```

### 3. Create a Subscription

```bash
curl -X POST "https://localhost:28163/api/subscriptions" `
  -H "Authorization: Bearer YOUR_TOKEN" `
  -H "Content-Type: application/json" `
  -d '{"productHandle":"eshop-pro"}' `
  -k
```

**Expected Response (201 Created):**
```json
{
  "subscriptionId": 12345678,
  "customerId": 87654321,
  "state": "active",
  "activatedAt": "2026-09-07T12:00:00+00:00"
}
```

**Verification:**
- Status code is `201 Created`
- `subscriptionId` is a positive integer (from Maxio)
- `state` is `"active"`
- `activatedAt` is a valid ISO 8601 timestamp

### 4. List User's Subscriptions

```bash
curl -X GET "https://localhost:28163/api/my-subscriptions" `
  -H "Authorization: Bearer YOUR_TOKEN" `
  -k
```

**Expected Response (200 OK):**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productId": 7126957,
      "activatedAt": "2026-09-07T12:00:00+00:00",
      "nextAssessmentAt": "2026-10-07T12:00:00+00:00"
    }
  ]
}
```

**Verification:**
- Status code is `200 OK`
- Subscription appears in the list
- State is `"active"`
- Contains the productId from the plan created in step 3

### 5. Verify Idempotency (Subscribe Again with Same User)

```bash
# Create another subscription with the same user and product
curl -X POST "https://localhost:28163/api/subscriptions" `
  -H "Authorization: Bearer YOUR_TOKEN" `
  -H "Content-Type: application/json" `
  -d '{"productHandle":"basic-plan"}' `
  -k
```

**Expected Response (201 Created):**
- A new subscription with a different ID
- Same customer ID as before
- State is `"active"`

Then run step 4 again to verify both subscriptions appear in the list.

## Testing Error Cases

### Missing Authentication

```bash
curl -X GET "https://localhost:28163/api/subscription-plans" -k
```

**Expected Response:** `401 Unauthorized`

### Invalid Product Handle

```bash
curl -X POST "https://localhost:28163/api/subscriptions" `
  -H "Authorization: Bearer YOUR_TOKEN" `
  -H "Content-Type: application/json" `
  -d '{"productHandle":"non-existent-product"}' `
  -k
```

**Expected Response:** `400 Bad Request` or `422 Unprocessable Entity`

## Verifying in Maxio Dashboard

1. Log in to your Maxio sandbox account
2. Navigate to **Customers** → **Subscriptions**
3. Verify:
   - A new customer was created with reference `user-admin@microsoft.com`
   - Subscription(s) appear with state `Active`
   - Plan matches what was requested (eshop-pro or basic-plan)
   - Billing period is correctly set to monthly

## Architecture Overview

```
PublicApi Endpoint (REST)
    ↓
IMaxioSubscriptionService (Business Logic)
    ↓
MaxioAdvancedBillingClient (SDK)
    ↓
Maxio API (Sandbox at https://cp-exp-1.chargify.com)
```

## Key Implementation Details

- **Idempotent Customer Creation:** Uses `ReadCustomerByReference` followed by `CreateCustomer` if not found
- **In-Memory Mapping:** userId ↔ Maxio customerId stored in-memory (lost on restart)
- **Error Handling:** Caught SDK exceptions and converted to HTTP responses
- **JWT Authentication:** All subscription endpoints require valid JWT bearer token

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Connection refused" on API call | Ensure server is running and listening on correct port |
| `401 Unauthorized` on subscription endpoints | Verify JWT token is valid and included in Authorization header |
| `422 Unprocessable Entity` | Check that product handle matches seeded plan handles |
| "Failed to list subscription plans" | Verify `MAXIO_PRODUCT_FAMILY_HANDLE` environment variable is set correctly |
| Build errors about Maxio types | Ensure `AsadAli.AdvancedBilling.Sdk` NuGet package is restored |

## Completion Criteria

✅ Subscription plans can be listed  
✅ New subscriptions can be created  
✅ User's subscriptions can be retrieved  
✅ Customer is created once and reused (idempotent)  
✅ JWT authentication is enforced  
✅ Subscriptions appear in Maxio dashboard with correct state  
✅ Build succeeds with no errors  
