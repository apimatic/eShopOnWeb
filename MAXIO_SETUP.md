# Maxio Subscription Integration Setup

## Prerequisites

- .NET 8.0 SDK (or use rollForward with .NET 10 SDK)
- ASP.NET Core 8.0 runtime
- Maxio sandbox account with:
  - API Key
  - Site subdomain
  - Product family handle
  - At least one subscription plan

## Environment Setup

### 1. Set Environment Variables

Set the following environment variables for the Maxio API:

```bash
# On Windows (PowerShell)
$env:MAXIO_API_KEY = "your_api_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_site_subdomain"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "your_product_family_handle"

# On Windows (CMD)
set MAXIO_API_KEY=your_api_key
set MAXIO_SITE_SUBDOMAIN=your_site_subdomain
set MAXIO_ENVIRONMENT=sandbox
set MAXIO_DEFAULT_PRODUCT_FAMILY=your_product_family_handle

# On Linux/macOS
export MAXIO_API_KEY="your_api_key"
export MAXIO_SITE_SUBDOMAIN="your_site_subdomain"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="your_product_family_handle"
```

### 2. Configure for Development

For development, the application uses an in-memory database. To use it, set:

```bash
# On Windows (PowerShell)
$env:UseOnlyInMemoryDatabase = "true"

# On Windows (CMD)
set UseOnlyInMemoryDatabase=true

# On Linux/macOS
export UseOnlyInMemoryDatabase=true
```

To rollforward the .NET SDK when only .NET 10 is installed:

```bash
# On Windows (PowerShell)
$env:DOTNET_ROLL_FORWARD = "Major"

# On Windows (CMD)
set DOTNET_ROLL_FORWARD=Major

# On Linux/macOS
export DOTNET_ROLL_FORWARD=Major
```

## Running the Application

### 1. Build the project:
```bash
dotnet build
```

### 2. Run the PublicApi project:
```bash
cd src/PublicApi
dotnet run
```

The API will be available at `https://localhost:27323/api/`

## API Endpoints

### 1. Get Available Subscription Plans
```
GET /api/subscription-plans
Authorization: Bearer {token}
```

**Response:**
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceUSD": 299.00,
      "billingUnit": "month"
    }
  ]
}
```

### 2. Subscribe to a Plan
```
POST /api/subscriptions
Authorization: Bearer {token}
Content-Type: application/json

{
  "planHandle": "eshop-pro"
}
```

**Response:**
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "subscription": {
    "id": 15236915,
    "planHandle": "eshop-pro",
    "state": "active",
    "currentPeriodEndsAt": "2026-10-07T12:00:00Z",
    "nextAssessmentAt": "2026-10-07T12:00:00Z"
  }
}
```

### 3. Get User's Active Subscriptions
```
GET /api/my-subscriptions
Authorization: Bearer {token}
```

**Response:**
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "subscriptions": [
    {
      "id": 15236915,
      "planHandle": "eshop-pro",
      "state": "active",
      "currentPeriodEndsAt": "2026-10-07T12:00:00Z",
      "nextAssessmentAt": "2026-10-07T12:00:00Z"
    }
  ]
}
```

## Getting a Bearer Token

To test the endpoints, first authenticate:

```
POST /api/authenticate
Content-Type: application/json

{
  "username": "demouser@microsoft.com",
  "password": "Pass@word1"
}
```

Use the returned token in the `Authorization: Bearer {token}` header for subsequent requests.

## Troubleshooting

### Issue: No ASP.NET Core 8.0 Runtime
If you only have .NET 10 SDK installed:
1. Set `DOTNET_ROLL_FORWARD=Major` environment variable
2. Or install ASP.NET Core 8.0 runtime

### Issue: SQL Server LocalDB Not Available
The application uses in-memory database in development. Set `UseOnlyInMemoryDatabase=true`

### Issue: HTTPS Certificate Error
Ensure dev cert is trusted:
```bash
dotnet dev-certs https --check
```

## Architecture Notes

### User-to-Customer Mapping
The integration stores a mapping between eShopOnWeb ApplicationUser IDs and Maxio customer IDs in the `MaxioCustomerMappings` table. This enables:
- Idempotent customer creation (same user doesn't create duplicate customers)
- Efficient subscription lookups
- Clean audit trail

### Idempotent Operations
All operations are idempotent:
- Subscription creation fails if user already has an active subscription for the plan
- Customer creation uses `reference` field (eshop-{userId}) to prevent duplicates
- Double-clicking "Subscribe" button won't create duplicate subscriptions

### Configuration
Maxio settings are loaded from environment variables and configuration:
- `Maxio:ApiKey` - API authentication key
- `Maxio:Subdomain` - Maxio site subdomain
- `Maxio:ProductFamilyHandle` - Product family to list/subscribe
- `Maxio:BaseUrl` - Optional override for API base URL (defaults to https://{subdomain}.chargify.com)

## Payment Configuration

The sandbox subscriptions are configured with:
- No trial period
- No setup fee
- No payment method required (payment_collection_method: remittance)
- Automatic billing on renewal

For production, adjust these settings in the Maxio portal.
