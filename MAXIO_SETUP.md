# Maxio Subscription Billing Integration - Setup & Verification Guide

## Overview

This guide walks you through setting up and verifying the Maxio subscription billing integration added to eShopOnWeb. The integration adds three new endpoints to the PublicApi for managing recurring subscriptions alongside the existing cart/checkout flow.

## Architecture

**Components Added:**
- `MaxioSettings` — Configuration model for Maxio credentials and settings
- `MaxioService` — HTTP client wrapper around Maxio OpenAPI v3.1.0
- `MaxioCustomerMapping` — Entity tracking eShopOnWeb user → Maxio customer relationship
- Three new endpoints under `/api/`:
  - `GET /api/subscription-plans` — List available subscription plans
  - `POST /api/subscriptions` — Create a subscription for the authenticated user
  - `GET /api/my-subscriptions` — Retrieve the user's active subscriptions

**Endpoints:** All require JWT authentication (Bearer token).

## Prerequisites

1. **Maxio Sandbox Account:** Sign up at https://app.chargify.com/signup/maxio-billing-sandbox
2. **.NET 8.0 SDK:** Required for building
3. **ASP.NET Core 8.0 Runtime** (if not using .NET 10 SDK with rollForward)
4. **Postman or curl:** For testing endpoints

## Step 1: Maxio Sandbox Setup

1. Log into your Maxio sandbox site
2. Navigate to **Settings → API Keys** and create an API key (or copy an existing one)
3. Note the following from your site settings:
   - **API Key:** Your authentication credential
   - **Site Subdomain:** The `cp-exp-3` part of your URL (if your sandbox is `https://cp-exp-3.chargify.com`)
   - **Product Family Handle:** The handle for the product family containing your subscription plans (e.g., `eshop-subscribe`)

Default sandbox entities (if seeded on `cp-exp-3`):
- Product Family: `eshop-subscribe`
- Pro Plan: `eshop-pro` ($299/mo)
- Basic Plan: `basic-plan` ($29/mo)

## Step 2: Configure User Secrets

Store Maxio credentials in .NET user-secrets to keep them out of the repository:

```bash
cd src/PublicApi

# Set the API key
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"

# Set the site subdomain
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-3"

# Set the product family handle
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: Set a custom base URL if not using standard chargify.com domain
# dotnet user-secrets set "Maxio:BaseUrl" "https://cp-exp-3.chargify.com"
```

Verify secrets were set:
```bash
dotnet user-secrets list
```

## Step 3: Environment Configuration

For local development:

```bash
# Use in-memory database (no LocalDB required)
export UseOnlyInMemoryDatabase=true

# Allow .NET to roll forward from 8.0 to 10 SDK if needed
export DOTNET_ROLL_FORWARD=Major
```

## Step 4: Build & Run

```bash
# Build the entire solution
dotnet build eShopOnWeb.sln

# Start the PublicApi
cd src/PublicApi
dotnet run
```

The API will be available at `https://localhost:25183`.

## Step 5: Authenticate

Before calling subscription endpoints, get a JWT token:

```bash
curl -X POST https://localhost:25183/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

The response will include a `token` field. Use this token as the Bearer token for subsequent requests:

```bash
export TOKEN="your_token_here"
```

## Step 6: Test the Endpoints

### 6a. List Subscription Plans

```bash
curl -X GET https://localhost:25183/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json"
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "$299 Pro Plan",
      "handle": "eshop-pro",
      "price": 299.00,
      "billingPeriod": "month"
    },
    {
      "id": 7126958,
      "name": "$29 Basic Plan",
      "handle": "basic-plan",
      "price": 29.00,
      "billingPeriod": "month"
    }
  ]
}
```

### 6b. Create a Subscription

```bash
curl -X POST https://localhost:25183/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

**Expected Response:**
```json
{
  "id": 123456789,
  "state": "active",
  "productName": "$299 Pro Plan",
  "productHandle": "eshop-pro",
  "price": 299.00,
  "nextBillingDate": "2026-10-06T12:00:00Z"
}
```

### 6c. Retrieve User's Subscriptions

```bash
curl -X GET https://localhost:25183/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json"
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 123456789,
      "state": "active",
      "productName": "$299 Pro Plan",
      "productHandle": "eshop-pro",
      "price": 299.00,
      "nextBillingDate": "2026-10-06T12:00:00Z"
    }
  ]
}
```

## Key Design Decisions

1. **Maxio API Contract:** All interactions derive from the OpenAPI spec in `maxio-spec/openapi.yaml`. No fields or endpoints are inferred beyond the spec.

2. **Customer Mapping:** Users are identified to Maxio by their eShopOnWeb `userId` stored in the Maxio customer's `reference` field. This ensures idempotent subscription creation—a second call for the same user never creates duplicate Maxio customers.

3. **Payment Method Not Required:** The sandbox plans (`eshop-pro` and `basic-plan`) are configured with `payment_collection_method: remittance`, meaning payment information is not collected during signup. This simplifies testing.

4. **In-Memory Database:** By default, the integration uses an in-memory database for development. All data persists only within a single app run. To use SQL Server LocalDB:
   - Remove `UseOnlyInMemoryDatabase=true`
   - Ensure LocalDB is installed
   - Run EF Core migrations

5. **Minimal Logging:** Production-ready error handling is in place but logging is minimal. Extend `MaxioService` with additional telemetry as needed.

## Troubleshooting

**401 Unauthorized on subscription endpoints:**
- Ensure your JWT token is valid and not expired
- The Bearer prefix in the Authorization header is required

**400 Failed to create/retrieve Maxio customer:**
- Verify `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` are set correctly in user-secrets
- Confirm the Maxio sandbox site is accessible and the API key is valid
- Check the eShopOnWeb user email is correctly populated

**422 errors from Maxio:**
- Verify the product handle exists on your sandbox and is part of the configured product family
- Check that the plan does not require a payment method if `payment_collection_method` is set to something other than `remittance`

**HTTPS certificate issues:**
- Ensure the .NET dev certificate is trusted: `dotnet dev-certs https --check`
- If untrusted, install it: `dotnet dev-certs https --trust`

## File Structure

```
src/
  ApplicationCore/
    Entities/
      MaxioCustomerMapping.cs          # User↔Maxio customer mapping entity
    MaxioSettings.cs                   # Configuration model
  Infrastructure/
    Services/
      MaxioService.cs                  # Maxio API client & service interface
  PublicApi/
    SubscriptionEndpoints/
      GetSubscriptionPlansEndpoint.cs   # GET /api/subscription-plans
      CreateSubscriptionEndpoint.cs     # POST /api/subscriptions
      GetMySubscriptionsEndpoint.cs     # GET /api/my-subscriptions
    appsettings.json                   # Placeholder Maxio config
    Program.cs                         # DI registration for Maxio services
```

## Next Steps for Production

1. **Persistence:** Integrate the `MaxioCustomerMapping` entity into the database and create EF Core migrations
2. **Error Handling:** Wrap Maxio service calls in a circuit breaker or retry policy
3. **Webhooks:** Implement Maxio webhook handlers for subscription state changes (e.g., renewal, cancellation)
4. **Subscription Management:** Add endpoints for upgrading/downgrading plans, canceling subscriptions
5. **Analytics:** Track subscription metrics (churn rate, MRR, etc.)
6. **Rate Limiting:** Implement rate limiting on subscription endpoints to prevent abuse

## Questions?

Refer to the Maxio OpenAPI specification at `maxio-spec/openapi.yaml` for endpoint details, parameter schema, and error models.
