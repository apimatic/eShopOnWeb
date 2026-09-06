# Maxio Subscription Billing Integration

This document describes how to set up and test the Maxio subscription billing integration in eShopOnWeb.

## Prerequisites

- .NET 8.0+ SDK installed
- Maxio Advanced Billing sandbox account with credentials
- The sandbox environment must have the following resources seeded (available on site `cp-exp-2`):
  - Product Family: `eshop-subscribe`
  - Plans: `eshop-pro` ($299/mo), `basic-plan` ($29/mo)

## Environment Setup

The PublicApi application reads Maxio configuration from environment variables:

| Environment Variable | Configuration Key | Purpose |
|---|---|---|
| `MAXIO_API_KEY` | `Maxio:ApiKey` | API key for Maxio sandbox |
| `MAXIO_SITE_SUBDOMAIN` | `Maxio:Subdomain` | Maxio site subdomain |
| `MAXIO_DEFAULT_PRODUCT_FAMILY` | `Maxio:ProductFamilyHandle` | Default product family handle (`eshop-subscribe`) |
| `MAXIO_BASE_URL` | `Maxio:BaseUrl` | Optional: Override base URL (defaults to `https://{subdomain}.chargify.com`) |

Additionally, the application uses in-memory database for this demo:
| Environment Variable | Purpose |
|---|---|
| `UseOnlyInMemoryDatabase` | Set to `true` to use in-memory database (no SQL Server required) |
| `DOTNET_ROLL_FORWARD` | Set to `Major` to allow .NET SDK version roll-forward |

## Running the Application

### Step 1: Set Environment Variables

**Windows (PowerShell):**
```powershell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "your-subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

**Linux/macOS (bash):**
```bash
export MAXIO_API_KEY="your-api-key"
export MAXIO_SITE_SUBDOMAIN="your-subdomain"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
export DOTNET_ROLL_FORWARD="Major"
```

### Step 2: Run the PublicApi

From the repository root:
```bash
dotnet run --project src/PublicApi/PublicApi.csproj
```

The API will start on the configured ports (check `launchSettings.json`).

## API Endpoints

### 1. Get Subscription Plans (Unauthenticated)
```
GET /api/subscription-plans
```

Returns a list of available subscription plans from Maxio.

**Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 2. Create Subscription (Authenticated)
```
POST /api/subscriptions
Authorization: Bearer {jwt-token}
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

Creates a subscription for the authenticated user. Automatically creates a Maxio customer if one doesn't exist.

**Response:**
```json
{
  "subscription": {
    "id": 123456789,
    "state": "active",
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "nextBillingAt": "2024-10-07T00:00:00Z",
    "currentPeriodEndsAt": "2024-10-07T00:00:00Z",
    "createdAt": "2024-09-07T12:34:56Z"
  }
}
```

### 3. Get User's Subscriptions (Authenticated)
```
GET /api/my-subscriptions
Authorization: Bearer {jwt-token}
```

Returns all subscriptions for the authenticated user.

**Response:**
```json
{
  "subscriptions": [
    {
      "id": 123456789,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "nextBillingAt": "2024-10-07T00:00:00Z",
      "currentPeriodEndsAt": "2024-10-07T00:00:00Z",
      "createdAt": "2024-09-07T12:34:56Z"
    }
  ]
}
```

## Testing the Integration

### Step 1: Authenticate
First, get a JWT token:
```bash
curl -X POST https://localhost:27383/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

Save the returned `token` value for the next requests.

### Step 2: Get Available Plans
```bash
curl -X GET https://localhost:27383/api/subscription-plans \
  -H "Accept: application/json"
```

This should return a list of subscription plans from Maxio.

### Step 3: Subscribe to a Plan
```bash
curl -X POST https://localhost:27383/api/subscriptions \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

This creates a subscription for the authenticated user in Maxio. The user is automatically created as a customer if not already present (idempotent based on eShop user ID).

### Step 4: View User's Subscriptions
```bash
curl -X GET https://localhost:27383/api/my-subscriptions \
  -H "Authorization: Bearer {token}" \
  -H "Accept: application/json"
```

This returns all active subscriptions for the user.

## Design Notes

- **Idempotent Customer Creation**: Each eShop user is mapped to a Maxio customer using a `eshop-{userId}` reference. The service checks if a customer already exists before creating a new one, preventing duplicate customers.
- **Sandbox-Only**: The integration targets the Maxio sandbox environment only.
- **In-Memory Database**: For demo purposes, the application uses an in-memory database that resets on each restart. User-subscription mappings are maintained within a single application instance.
- **JWT Authentication**: The `/api/subscriptions` and `/api/my-subscriptions` endpoints require JWT authentication via the Authorization Bearer token.
- **No Payment Method Required**: The seeded plans in the Maxio sandbox don't require payment method capture, allowing immediate subscriptions without card details.

## Architecture

- **Infrastructure/Services/MaxioBillingService.cs**: Core service that interacts with the Maxio API using basic authentication
- **PublicApi/SubscriptionEndpoints/**: Three endpoints implementing the subscription flow
- **ApplicationCore/MaxioSettings.cs**: Configuration object for Maxio credentials

The service makes direct HTTP calls to the Maxio Billing API, parsing JSON responses to extract subscription and plan data.
