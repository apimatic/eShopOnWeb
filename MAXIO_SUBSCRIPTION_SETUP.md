# Maxio Subscription Billing Integration

This document explains how to set up and verify the Maxio subscription billing integration for eShopOnWeb.

## Architecture Overview

The subscription billing capability is added as parallel, additive functionality to the existing cart/checkout flow. It includes:

1. **Database**: `MaxioCustomerMapping` entity in `AppIdentityDbContext` tracks the mapping between eShopOnWeb users and Maxio customers
2. **Service**: `MaxioApiClient` handles all HTTP requests to the Maxio Advanced Billing API
3. **Endpoints**: Three new REST endpoints expose subscription functionality via the PublicApi project

## Configuration

### Environment Variables Required

Set the following environment variables before running the application:

```bash
MAXIO_API_KEY=<your-maxio-api-key>
MAXIO_SITE_SUBDOMAIN=<your-maxio-subdomain>
MAXIO_ENVIRONMENT=sandbox
MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### Configuration Binding

The `appsettings.json` configuration section `Maxio:` binds to these environment variables:
- `Maxio:ApiKey` ← `MAXIO_API_KEY`
- `Maxio:Subdomain` ← `MAXIO_SITE_SUBDOMAIN`
- `Maxio:ProductFamilyHandle` ← `MAXIO_DEFAULT_PRODUCT_FAMILY` (optional override)
- `Maxio:BaseUrl` ← `MAXIO_BASE_URL` (optional override for custom API base URL)

**Never commit the actual values to the repository** — only the variable names.

### Database Configuration

The in-memory database is enabled with:
```bash
UseOnlyInMemoryDatabase=true
```

This is recommended for development since:
- No SQL Server LocalDB is required
- All data is self-contained in the running process
- Perfect for testing without persistence concerns

## API Endpoints

All endpoints require JWT authentication (Bearer token). Get a token from `/api/authenticate` first.

### 1. Get Available Subscription Plans
```
GET /api/subscription-plans
Authorization: Bearer <token>
```

Response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional plan for teams",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 2. Create a Subscription
```
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

Response:
```json
{
  "subscriptionId": 18220670,
  "state": "active",
  "productName": "Pro Plan",
  "productHandle": "eshop-pro",
  "priceInCents": 29900,
  "nextBillingDate": "2026-10-07T00:00:00Z",
  "createdAt": "2026-09-07T12:00:00Z"
}
```

### 3. Get My Subscriptions
```
GET /api/my-subscriptions
Authorization: Bearer <token>
```

Response:
```json
{
  "subscriptions": [
    {
      "subscriptionId": 18220670,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "priceInCents": 29900,
      "currentPeriodEndsAt": "2026-10-07T00:00:00Z",
      "nextAssessmentAt": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:00:00Z"
    }
  ]
}
```

## Verification Steps

### Step 1: Build the Project
```bash
cd repo
dotnet build
```

### Step 2: Set Environment Variables

#### On Windows (PowerShell):
```powershell
$env:UseOnlyInMemoryDatabase = "true"
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "your-subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

#### On Linux/macOS:
```bash
export UseOnlyInMemoryDatabase=true
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="your-subdomain"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

### Step 3: Run the PublicApi
```bash
cd src/PublicApi
dotnet run
```

The API will be available at `https://localhost:28203/swagger`

### Step 4: Get an Authentication Token

Use curl or Postman to authenticate:
```bash
curl -X POST https://localhost:28203/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'
```

This returns a response with a `token` field. Copy this token.

### Step 5: Verify the Endpoints

#### Test Get Plans
```bash
curl -X GET https://localhost:28203/api/subscription-plans \
  -H "Authorization: Bearer <your-token>" \
  --insecure
```

#### Test Create Subscription
```bash
curl -X POST https://localhost:28203/api/subscriptions \
  -H "Authorization: Bearer <your-token>" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure
```

#### Test Get My Subscriptions
```bash
curl -X GET https://localhost:28203/api/my-subscriptions \
  -H "Authorization: Bearer <your-token>" \
  --insecure
```

## How It Works

### User-to-Customer Mapping Flow

1. When a user creates their first subscription, the system:
   - Checks if a `MaxioCustomerMapping` exists for this user
   - If not, creates a new Maxio customer with reference `eshop-{userId}`
   - Stores the mapping in the database

2. All subsequent subscriptions for the same user use the existing Maxio customer

### Subscription Creation Flow

1. User calls POST `/api/subscriptions` with a product handle
2. Endpoint extracts userId from JWT token
3. Ensures a Maxio customer exists for this user
4. Creates a subscription in Maxio with `payment_collection_method: "remittance"` (no payment required)
5. Returns subscription details to the user

### Key Design Decisions

- **No Payment Required**: Subscriptions are created with payment collection method set to "remittance" to avoid requiring card capture for sandbox testing
- **Idempotent Customer Creation**: The same user trying to subscribe twice won't create duplicate customers
- **In-Memory Database**: For development, all subscription mappings are lost on application restart (as documented in task constraints)
- **Reference-Based Linking**: Users are linked to Maxio customers via reference field (`eshop-{userId}`) for easy debugging

## Maxio API Specifications

The integration strictly follows the Maxio OpenAPI specification located in `maxio-spec/openapi.yaml`:

- **Endpoint**: `POST /subscriptions.json` - Create Subscription
- **Endpoint**: `GET /customers/{customer_id}/subscriptions.json` - List Subscriptions
- **Endpoint**: `GET /product_families/lookup.json` - Get Products by Family Handle
- **Endpoint**: `POST /customers.json` - Create Customer
- **Endpoint**: `GET /customers/lookup.json` - Find Customer by Reference
- **Authentication**: Basic HTTP auth with API key as username and "x" as password

## Troubleshooting

### "Failed to fetch subscription plans"
- Verify `MAXIO_DEFAULT_PRODUCT_FAMILY` is set correctly (should be "eshop-subscribe")
- Verify `MAXIO_API_KEY` and `MAXIO_SITE_SUBDOMAIN` are correct
- Check Maxio sandbox site has the product family created

### "Unauthorized" when calling endpoints
- Verify you have a valid JWT token
- Use `/api/authenticate` to get a fresh token

### "Failed to create customer in Maxio"
- Check Maxio API key has permission to create customers
- Verify network connectivity to Maxio sandbox

## Database Schema

### MaxioCustomerMapping Table

```
Id (int, Primary Key)
UserId (string, Unique, Foreign Key to AspNetUsers)
MaxioCustomerId (int, Unique)
MaxioCustomerReference (string) - Format: "eshop-{userId}"
CreatedAt (DateTime)
```

## Security Considerations

1. **No Secrets in Repo**: Maxio API keys are never stored in the repository
2. **User Isolation**: Each user's subscriptions are tied to their JWT identity
3. **Authenticated Endpoints**: All subscription endpoints require JWT authentication
4. **Reference-Based Linking**: Customer references use userId which cannot be spoofed via JWT

## Future Enhancements

Possible features for future implementation:
- Subscription cancellation endpoint
- Update subscription (change plan)
- Webhook integration for subscription status changes
- Usage tracking for metered components
- Invoice history retrieval
