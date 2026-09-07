# Maxio Subscription Integration for eShopOnWeb

## Overview

This document describes the subscription billing integration added to eShopOnWeb using Maxio Advanced Billing as the billing system of record.

## Architecture

### Endpoints

Three JWT-authenticated endpoints have been added to the PublicApi project:

#### 1. GET /api/subscription-plans
Lists available subscription plans from Maxio's product family.

**Response:**
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Professional Plan",
      "description": "Professional plan description",
      "priceInCents": 29900
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic plan description",
      "priceInCents": 2900
    }
  ],
  "correlationId": "uuid"
}
```

#### 2. POST /api/subscriptions
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
  "nextBillingAt": "2026-10-07T00:00:00Z",
  "productHandle": "eshop-pro",
  "productName": "Professional Plan",
  "productPriceInCents": 29900,
  "correlationId": "uuid"
}
```

#### 3. GET /api/my-subscriptions
Lists the authenticated user's subscriptions.

**Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345,
      "productHandle": "eshop-pro",
      "productName": "Professional Plan",
      "productPriceInCents": 29900,
      "state": "active",
      "nextBillingAt": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:00:00Z"
    }
  ],
  "correlationId": "uuid"
}
```

### Data Model

**ApplicationUser Extension:**
- Added `MaxioCustomerId` (string) field to store the Maxio customer ID for idempotent customer sync

**Configuration:**
- Maxio credentials read from environment variables: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_ENVIRONMENT` (default: US)
- Mapped to .NET configuration section `Maxio:` with keys: `ApiKey`, `Subdomain`, `Environment`, `ProductFamilyHandle`
- Product family handle defaults to `eshop-subscribe`

### Integration Flow

1. **Browse Plans** → GET /api/subscription-plans
   - Lists products from the Maxio product family handle
   - No customer sync needed

2. **Subscribe** → POST /api/subscriptions
   - User is authenticated via JWT token
   - System checks if user has a Maxio customer ID
   - If not, creates/retrieves Maxio customer by email (idempotent)
   - Creates subscription with specified plan handle
   - Stores Maxio customer ID in user record for future operations

3. **View Subscriptions** → GET /api/my-subscriptions
   - Retrieves user's Maxio customer ID from application user record
   - Lists subscriptions for that customer
   - Returns subscription state, next billing date, plan details

## Setup & Testing

### Environment Setup

1. **Set Maxio Credentials** (Development)
   ```bash
   cd src/PublicApi
   dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
   dotnet user-secrets set "Maxio:Subdomain" "cp-exp-1"
   ```

2. **Environment Variables** (Production/Testing)
   Set these environment variables before running:
   - `MAXIO_API_KEY` — API key from Maxio
   - `MAXIO_SITE_SUBDOMAIN` — Maxio site subdomain (e.g., `cp-exp-1`)
   - `MAXIO_ENVIRONMENT` — US or EU (default: US)
   - `MAXIO_DEFAULT_PRODUCT_FAMILY` — Product family handle (default: eshop-subscribe)

3. **Database Setup**
   The integration uses an in-memory database by default. For persistence testing, adjust connection strings:
   ```bash
   # Development (if using LocalDB)
   dotnet ef database update --project src/Infrastructure --startup-project src/PublicApi
   
   # Or use in-memory (default)
   UseOnlyInMemoryDatabase=true
   ```

### Testing the Integration

#### Step 1: Start the PublicApi Server
```bash
cd src/PublicApi
dotnet run
```

The API will start on: `https://localhost:28503/api/`

#### Step 2: Get an Authentication Token
```bash
curl -X POST "https://localhost:28503/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username": "demouser@microsoft.com", "password": "Pass@word1"}' \
  --insecure
```

Response:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "result": true,
  "username": "demouser@microsoft.com",
  ...
}
```

#### Step 3: Browse Subscription Plans
```bash
curl -X GET "https://localhost:28503/api/subscription-plans" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  --insecure
```

Response shows available plans from the Maxio product family.

#### Step 4: Create a Subscription
```bash
curl -X POST "https://localhost:28503/api/subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}' \
  --insecure
```

Expected response:
- HTTP 201 Created with subscription details
- Maxio customer created (if first time)
- Subscription created on Maxio

#### Step 5: Retrieve User's Subscriptions
```bash
curl -X GET "https://localhost:28503/api/my-subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN" \
  --insecure
```

Response shows all subscriptions for the user.

### Troubleshooting

**"Maxio Customer ID not found"** — The customer was created on Maxio but the ID wasn't stored. Check that the ApplicationUser.MaxioCustomerId field is properly persisted.

**"401 Unauthorized"** — Ensure you have a valid JWT bearer token. Get one from the `/api/authenticate` endpoint first.

**"404 Not Found on Plan"** — Verify the product family handle matches `eshop-subscribe` and contains the seeded plans (`eshop-pro`, `basic-plan`).

**"422 Unprocessable Entity on subscription create"** — Check the error response for validation details. Common causes:
- Product handle doesn't exist in the family
- Customer email already has a subscription (depends on Maxio product config)

**Connection Error** — Verify Maxio credentials are correct and the site subdomain is accessible. Test connectivity with:
```bash
curl -X GET "https://cp-exp-1.chargify.com/products.json" \
  -u "YOUR_API_KEY:x" \
  --insecure
```

## Production Considerations

### Security
- Credentials are read from environment variables and user secrets, never committed to the repo
- API key is never logged or displayed in responses
- Endpoints require JWT authentication

### Error Handling
- SDK exceptions (`SdkException<CreateSubscriptionError>`, `SdkException<RawError>`) are caught and mapped to appropriate HTTP status codes
- Unhandled exceptions return HTTP 500 to prevent information disclosure
- Validation errors from Maxio are returned as HTTP 400 with error details

### Resilience
- HttpClient is registered with `IHttpClientFactory` for reuse and connection pooling
- Timeout is set to 30 seconds per attempt
- SDK has built-in retry logic for transient failures (Polly)

### Data Persistence
- Customer ID is stored in the ApplicationUser record to avoid repeated Maxio lookups
- Idempotent customer lookup by email prevents duplicate customers
- In-memory database loses data on app restart; use SQL Server for persistence

## Files Changed

- `src/Infrastructure/Identity/ApplicationUser.cs` — Added MaxioCustomerId field
- `src/PublicApi/Program.cs` — Added Maxio client DI setup
- `src/PublicApi/appsettings.json` — Added Maxio configuration defaults
- `src/PublicApi/MaxioConfiguration.cs` — Configuration class (new)
- `src/PublicApi/SubscriptionEndpoints/GetSubscriptionPlansEndpoint.cs` — List plans (new)
- `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` — Create subscription (new)
- `src/PublicApi/SubscriptionEndpoints/GetMySubscriptionsEndpoint.cs` — List user subscriptions (new)
- `src/Infrastructure/Identity/Migrations/[timestamp]_AddMaxioCustomerIdToApplicationUser.cs` — Database migration (new)
- `PublicApi.csproj` — Added `AsadAli.AdvancedBilling.Sdk` NuGet package

## Next Steps

1. **Verify endpoints work** with the test steps above
2. **Integrate into UI** — Call these endpoints from the Blazor or Web frontend
3. **Handle subscription state changes** — Add webhooks/polling for state transitions (active, canceled, past_due, etc.)
4. **Implement subscription management** — Add endpoints to upgrade/downgrade plans, update billing info, cancel subscriptions
5. **Add logging/monitoring** — Instrument calls to track subscription operations and Maxio API health
