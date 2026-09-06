# Maxio Subscription Billing Integration

This guide explains how to verify the Maxio subscription billing integration for eShopOnWeb.

## What Was Added

The integration adds recurring subscription capabilities to eShopOnWeb using Maxio Advanced Billing as the billing system of record. This is an additive capability that runs in parallel with the existing one-time commerce flow.

### New API Endpoints

All endpoints are JWT-authenticated and exposed under `/api/`:

- **`GET /api/subscription-plans`** — List available subscription plans from Maxio
- **`POST /api/subscriptions`** — Create a new subscription for the authenticated user
- **`GET /api/my-subscriptions`** — Get the authenticated user's active subscriptions

### Implementation Details

- **Maxio Service** (`src/PublicApi/Services/MaxioService.cs`): Core service for interacting with Maxio APIs
- **Maxio API Client** (`src/PublicApi/Services/MaxioApiClient.cs`): HTTP client wrapper with Basic Authentication
- **Configuration** (`src/PublicApi/Services/MaxioSettings.cs`): Settings model for Maxio credentials
- **Subscription Endpoints** (`src/PublicApi/SubscriptionEndpoints/`): Three REST endpoints for subscription management
- **DTOs**: Request/response models for API operations

## Setup Instructions

### 1. Prerequisites

- .NET 10 SDK (or 8.0.x with `DOTNET_ROLL_FORWARD=Major`)
- HTTPS dev certificate must be trusted: `dotnet dev-certs https --check`
- Maxio sandbox account with:
  - API Key
  - Site subdomain (e.g., `cp-exp-2`)
  - Product Family handle (e.g., `eshop-subscribe`)
  - Available subscription plans

### 2. Configure Maxio Credentials

Credentials are stored in .NET user-secrets, **never in repository files**. Set them up:

```bash
cd src/PublicApi

# Set Maxio API Key
dotnet user-secrets set "Maxio:ApiKey" "your_api_key_here"

# Set Maxio Site Subdomain
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain_here"

# Set Maxio Product Family Handle
dotnet user-secrets set "Maxio:ProductFamilyHandle" "your_product_family_handle_here"

# Optional: Override API base URL (if not using standard chargify.com subdomain)
dotnet user-secrets set "Maxio:BaseUrl" "https://custom-api.example.com"
```

Alternatively, set environment variables before running:

```bash
set MAXIO_API_KEY=your_api_key_here
set MAXIO_SITE_SUBDOMAIN=your_subdomain_here
set MAXIO_DEFAULT_PRODUCT_FAMILY=your_product_family_handle_here
```

### 3. Running the Application

From the repository root:

```bash
# Set environment variables for in-memory database
set DOTNET_ROLL_FORWARD=Major
set UseOnlyInMemoryDatabase=true

# Run PublicApi
cd src/PublicApi
dotnet run
```

The API will start at `https://localhost:25883` with Swagger documentation at `/swagger`.

## Quick Start: Automated Test

A test script is included to validate the entire flow:

```bash
cd /path/to/repo

# In one terminal, start the API
cd src/PublicApi
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:UseOnlyInMemoryDatabase = 'true'
dotnet run

# In another terminal, run the test script
./test-subscriptions.ps1 -ApiUrl "https://localhost:25883" -PlanHandle "eshop-pro"
```

The test script will:
1. Authenticate with demo credentials
2. Retrieve available plans
3. Create a subscription
4. Verify the subscription appears in the user's subscription list

## Manual Verification Steps

### Step 1: Authenticate

Get a JWT token by authenticating with demo credentials:

```bash
curl -X POST https://localhost:25883/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

**Note**: Demo credentials are seeded in the application:
- Username: `demouser@microsoft.com`
- Password: `Pass@word1`

The response contains a `token` field. Store it for the next requests:

```bash
set TOKEN=<token_from_response>
```

### Step 2: Retrieve Available Plans

```bash
curl -X GET https://localhost:25883/api/subscription-plans \
  -H "Authorization: Bearer %TOKEN%"
```

**Expected Response** (200 OK):
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with advanced features",
      "price": 299.00,
      "intervalUnit": "month",
      "interval": 1
    }
  ]
}
```

### Step 3: Create a Subscription

Subscribe to a plan (using the plan handle from Step 2):

```bash
curl -X POST https://localhost:25883/api/subscriptions \
  -H "Authorization: Bearer %TOKEN%" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }'
```

**Expected Response** (201 Created):
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "productName": "Pro Plan",
    "price": 299.00,
    "nextBillingDate": "2026-10-06T00:00:00Z",
    "createdAt": "2026-09-06T10:30:00Z"
  }
}
```

### Step 4: Retrieve User's Subscriptions

```bash
curl -X GET https://localhost:25883/api/my-subscriptions \
  -H "Authorization: Bearer %TOKEN%"
```

**Expected Response** (200 OK):
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productName": "Pro Plan",
      "price": 299.00,
      "nextBillingDate": "2026-10-06T00:00:00Z",
      "createdAt": "2026-09-06T10:30:00Z"
    }
  ]
}
```

### Step 5: Verify Maxio Customer Creation (Idempotency)

Call the create subscription endpoint twice with the same user. The second call should:
- Recognize the existing customer
- Create a new subscription (or reuse if the same plan)
- **Not** create duplicate customers

In Maxio UI, verify under `Customers` → `Customer Reference` that only one customer record exists per eShopOnWeb user.

## Verification Checklist

- [ ] Application builds successfully with `dotnet build src/PublicApi/PublicApi.csproj`
- [ ] Application runs without errors: `dotnet run`
- [ ] Swagger UI is accessible at `https://localhost:25883/swagger`
- [ ] Authentication endpoint returns valid JWT token
- [ ] `/api/subscription-plans` returns 200 with plan list
- [ ] `/api/subscriptions` POST creates subscription and returns 201
- [ ] `/api/my-subscriptions` returns user's subscriptions
- [ ] Creating a subscription for the same user twice reuses the Maxio customer (no duplicates)
- [ ] Maxio customer is created with correct user reference
- [ ] Subscription state is `active` or appropriate per plan configuration
- [ ] Next billing date is correctly calculated from plan interval
- [ ] Unauthenticated requests to protected endpoints return 401

## Architecture Notes

### Maxio Integration Points

1. **Product Family**: Filters plans from `product_family.handle`
2. **Customer Lookup**: Uses `reference` field for idempotent customer creation (`GET /customers/lookup.json`)
3. **Customer Creation**: Creates Maxio customer with eShopOnWeb user ID as `reference`
4. **Subscription Creation**: Links customer to product by handle, no payment method required
5. **Subscription Listing**: Filters by customer ID

### Security Considerations

- JWT tokens are required for all subscription endpoints
- Maxio credentials are stored in .NET user-secrets, never committed to repository
- Basic Auth (API Key) is used for Maxio API calls
- HTTPS is enforced for all API communication
- User ID is extracted from JWT claims to prevent cross-user access

### Database

- Uses in-memory database for development (in-memory by configuration)
- Subscription data is retrieved from Maxio on-demand (no local persistence)
- User identity is managed via ASP.NET Core Identity

## Troubleshooting

### "Maxio configuration incomplete"

**Cause**: Missing Maxio credentials in user-secrets or environment variables

**Solution**: Set credentials as described in Setup Instructions, Step 2

### "HttpRequestException: Maxio API returned 401"

**Cause**: Invalid API key or credentials

**Solution**: Verify API key in user-secrets: `dotnet user-secrets list`

### "Subscription creation failed: Cannot find plan..."

**Cause**: Plan handle doesn't exist or is in different product family

**Solution**: Verify plan handle matches `Maxio:ProductFamilyHandle` configuration

### HTTPS certificate errors

**Cause**: Dev certificate not trusted

**Solution**: Run `dotnet dev-cert https --clean` then `dotnet dev-cert https --trust`

## Next Steps

For production:
1. Move Maxio credentials to Azure Key Vault or similar secure storage
2. Implement webhook handlers for Maxio events (renewal, cancellation, etc.)
3. Add subscription management endpoints (upgrade, downgrade, cancel)
4. Implement metered component tracking for usage-based billing
5. Add tests for edge cases (payment failures, subscription state transitions)
6. Implement billing history and invoice retrieval
