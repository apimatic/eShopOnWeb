# Maxio Subscription Billing Integration Guide

This document provides step-by-step instructions to verify the Maxio subscription billing integration in eShopOnWeb.

## Architecture Overview

The integration adds a parallel subscription billing capability to eShopOnWeb, separate from the existing one-time checkout flow. It consists of:

- **Maxio API Client**: Communicates with Maxio Advanced Billing sandbox via OpenAPI spec
- **User Subscription Entities**: Tracks user-to-Maxio customer mapping and subscription state locally
- **PublicApi Endpoints**: Three REST endpoints for subscription management, JWT-authenticated
- **Database**: Two new tables (`UserMaxioCustomers`, `UserSubscriptions`) store subscription metadata

## Setup Instructions

### 1. Configure Maxio Credentials

Store Maxio sandbox credentials in user secrets:

```bash
cd src/PublicApi

# Set the four required Maxio settings from environment variables
dotnet user-secrets set "Maxio:ApiKey" "YOUR_SANDBOX_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SANDBOX_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
# BaseUrl is optional; omit it to auto-derive from Subdomain
# dotnet user-secrets set "Maxio:BaseUrl" ""
```

These come from your Maxio Advanced Billing sandbox account. The product family handle references the seeded demo catalog on site `cp-exp-3`.

### 2. Set Environment Variables

For local development, ensure:

```bash
set DOTNET_ROLL_FORWARD=Major
set UseOnlyInMemoryDatabase=true
```

(The in-memory database loses data on restart; subscriptions only persist within a single run.)

### 3. Build and Run

```bash
cd repo-root
dotnet build
cd src/PublicApi
dotnet run
```

The API starts on `https://localhost:25823`.

## API Endpoints

All subscription endpoints live under `/api/` and require JWT authentication (except for test setup).

### GET /api/subscription-plans

Lists available subscription plans from Maxio.

**Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "$299/mo Pro Plan",
      "handle": "eshop-pro",
      "pricePerMonth": 299.00,
      "description": "Professional tier"
    },
    {
      "id": 7126958,
      "name": "$29/mo Basic Plan",
      "handle": "basic-plan",
      "pricePerMonth": 29.00,
      "description": "Basic tier"
    }
  ]
}
```

### POST /api/subscriptions

Subscribe the authenticated user to a plan. Idempotently creates a Maxio customer (maps eShopOnWeb userId to Maxio customerId) and enrolls them.

**Request:**
```json
{
  "productHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "success": true,
  "subscriptionId": 12345678,
  "state": "active",
  "productName": "$299/mo Pro Plan",
  "nextBillingDate": "2026-10-06T15:30:45Z",
  "error": null
}
```

### GET /api/my-subscriptions

List all active subscriptions for the authenticated user.

**Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "productHandle": "eshop-pro",
      "state": "active",
      "nextBillingDate": "2026-10-06T15:30:45Z",
      "createdAt": "2026-09-06T15:30:45Z"
    }
  ]
}
```

## Verification Steps

### 1. Authenticate (get a JWT token)

Use the existing auth endpoint:

```bash
curl -X POST https://localhost:25823/api/login \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

(See PublicApi's authenticate endpoint for user/pass.)

Save the returned `accessToken`.

### 2. Test List Plans

```bash
curl -X GET https://localhost:25823/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json"
```

Should return the two seeded plans (`eshop-pro`, `basic-plan`).

### 3. Test Create Subscription

```bash
curl -X POST https://localhost:25823/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

**Success**: Returns subscription ID and `state: active`.

**What happens internally:**
- A Maxio customer is created for the user (mapped by their userId)
- The user is enrolled in the Pro plan
- The subscription and customer mappings are stored in the local database
- Double-click is safe: re-running the same request uses the existing customer

### 4. Test List User Subscriptions

```bash
curl -X GET https://localhost:25823/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json"
```

Should return the subscription you just created.

### 5. Verify Local Database State

If using SQL Server (not in-memory), query the new tables:

```sql
SELECT * FROM UserMaxioCustomers;
SELECT * FROM UserSubscriptions;
```

Each row tracks the userId ↔ Maxio customer/subscription mapping.

## Implementation Details

### Maxio API Contract

All requests to Maxio follow the OpenAPI spec in `maxio-spec/openapi.yaml`:

- **Auth**: Basic authentication (API Key + `x`)
- **Base URL**: `https://{subdomain}.chargify.com` (or custom `BaseUrl`)
- **Key endpoints used**:
  - `GET /product_families/handle:{handle}/products.json` – list plans
  - `POST /customers.json` – create/register a customer
  - `POST /subscriptions.json` – enroll a customer in a plan
  - `GET /subscriptions/{id}.json` – read subscription details
  - `GET /customers/{id}/subscriptions.json` – list customer's subscriptions

### No Payment Method Required

The seeded plans have `payment_method_not_required: true`, so subscriptions can be created without card capture or 3-D Secure. Perfect for testing.

### Entity Mapping

**UserMaxioCustomer**: Idempotent 1:1 mapping of eShopOnWeb user (userId) to Maxio customer ID.
- Ensures that double-clicking "subscribe" doesn't create two Maxio customers.

**UserSubscription**: Tracks local state of each subscription (product handle, billing state, next billing date).
- Used for quick lookup without hitting Maxio API on every request.

### Error Handling

- If Maxio API is unreachable: requests fail with descriptive errors (e.g., "Failed to create customer in billing system").
- If customer creation fails (e.g., invalid email): subscription creation fails gracefully.
- All exceptions are logged via `IAppLogger`.

## Constraints & Known Issues

1. **In-memory Database**: Using `UseOnlyInMemoryDatabase=true` means all subscription state is lost on restart.
2. **No .NET 8 Runtime**: If the ASP.NET Core 8.0 runtime is missing, set `DOTNET_ROLL_FORWARD=Major` to use .NET 10 SDK.
3. **No SQL Server LocalDB**: Default connection strings point to `(localdb)\\mssqllocaldb`. Use `UseOnlyInMemoryDatabase=true` instead.
4. **Secrets Binding**: Maxio credentials must come from environment variables or user secrets, never committed to the repo.

## File Manifest

**New files added:**

- `src/ApplicationCore/MaxioSettings.cs` – Maxio configuration class
- `src/ApplicationCore/Interfaces/IMaxioApiClient.cs` – Maxio HTTP client interface & DTOs
- `src/ApplicationCore/Entities/Subscription/UserMaxioCustomer.cs` – User ↔ Maxio customer mapping entity
- `src/ApplicationCore/Entities/Subscription/UserSubscription.cs` – Subscription state entity
- `src/ApplicationCore/Specifications/UserCustomerByUserIdSpec.cs` – Query spec for customer lookup
- `src/ApplicationCore/Specifications/UserSubscriptionsByUserIdSpec.cs` – Query spec for subscription lookup
- `src/Infrastructure/Services/MaxioApiClient.cs` – Maxio HTTP client implementation
- `src/Infrastructure/Data/Config/UserMaxioCustomerConfiguration.cs` – EF configuration
- `src/Infrastructure/Data/Config/UserSubscriptionConfiguration.cs` – EF configuration
- `src/Infrastructure/Data/Migrations/20260906000000_AddSubscriptionTables.cs` – Database migration
- `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs` – GET /api/subscription-plans
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` – POST /api/subscriptions
- `src/PublicApi/SubscriptionEndpoints/ListUserSubscriptionsEndpoint.cs` – GET /api/my-subscriptions
- `src/PublicApi/SubscriptionEndpoints/SubscriptionPlanDto.cs` – Response model

**Modified files:**

- `src/ApplicationCore/appsettings.json` – Added Maxio configuration section
- `src/Infrastructure/Dependencies.cs` – Registered Maxio services & HttpClient
- `src/Infrastructure/Data/CatalogContext.cs` – Added subscription DbSets

## Production Readiness

This integration is production-grade in its architecture but requires:

1. **Live Maxio Credentials**: Swap sandbox credentials for production ones.
2. **Persistent Database**: Use SQL Server or PostgreSQL (via EF provider) instead of in-memory.
3. **Webhook Handling** (optional): Add endpoint for Maxio subscription lifecycle webhooks (e.g., payment_success, renewal, cancellation).
4. **Error Tracking**: Integrate with Sentry or similar for monitoring Maxio API errors.
5. **Rate Limiting**: Implement rate limiting on endpoints if high volume is expected.
6. **Audit Logging**: Log all subscription changes (creates, state changes) for compliance.

## Support & Troubleshooting

### "Failed to load subscription plans"

- Verify `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` are set correctly.
- Ensure the Maxio sandbox site `cp-exp-3` is accessible.
- Check that the product family handle matches a real family on that site.

### "Failed to create customer in billing system"

- Verify the user's email is a valid, unique identifier (Maxio may reject duplicates).
- Check Maxio API key has create customer permissions.

### "User not found" (401)

- Ensure you're passing a valid JWT token with `ClaimTypes.NameIdentifier`.
- Verify the token is not expired.

### Subscription created but not appearing in "my subscriptions"

- The in-memory database is isolated per request. Check the CreateSubscriptionEndpoint response confirms success.
- If using SQL Server, query `UserSubscriptions` table directly.
