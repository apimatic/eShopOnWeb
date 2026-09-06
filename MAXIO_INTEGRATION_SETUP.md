# Maxio Subscription Billing Integration Setup Guide

This guide provides step-by-step instructions to set up and verify the Maxio subscription billing integration for eShopOnWeb.

## Prerequisites

- .NET SDK with rollForward support (global.json configured with `rollForward: latestMajor`)
- ASP.NET Core 8.0+ runtime or willingness to use `DOTNET_ROLL_FORWARD=Major`
- Maxio Advanced Billing sandbox account with demo credentials
- curl or Postman for API testing

## Environment Setup

### 1. Obtain Maxio Sandbox Credentials

Maxio sandbox credentials are provided as environment variables. You should receive:
- `MAXIO_API_KEY`: Your Maxio API key
- `MAXIO_SITE_SUBDOMAIN`: Your Maxio site subdomain (e.g., "cp-exp-4")
- `MAXIO_ENVIRONMENT`: Should be "sandbox" or similar (for your reference)
- `MAXIO_DEFAULT_PRODUCT_FAMILY`: The product family handle (e.g., "eshop-subscribe")

The demo sandbox includes:
- **Product Family**: `eshop-subscribe` (ID 3023074)
- **Pro Plan**: `eshop-pro` ($299/mo)
- **Basic Plan**: `basic-plan` ($29/mo)
- **Metered Component**: `api-call` ($0.01/unit)

### 2. Configure User Secrets (Recommended for Development)

```bash
cd src/PublicApi

# Set Maxio API Key
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"

# Set Maxio Subdomain
dotnet user-secrets set "Maxio:Subdomain" "YOUR_MAXIO_SUBDOMAIN"

# Set Maxio Product Family Handle
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: Set custom base URL (if needed)
dotnet user-secrets set "Maxio:BaseUrl" "https://your-custom-url.chargify.com"
```

### Alternative: Environment Variables

Set these environment variables before running the application:
```bash
export MAXIO_API_KEY="your_api_key"
export MAXIO_SITE_SUBDOMAIN="your_subdomain"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

### 3. Handle SDK/Runtime Mismatch

The project is pinned to .NET 8.0 (global.json), but only .NET 10 SDK may be installed.

Option A: Allow rollForward (already configured in global.json)
```bash
export DOTNET_ROLL_FORWARD=Major
```

Option B: Install ASP.NET Core 8.0 runtime
```bash
# Windows
dotnet-install.ps1 -Channel 8.0 -Runtime aspnetcore

# Linux/macOS
./dotnet-install.sh --channel 8.0 --runtime aspnetcore
```

## Building the Application

```bash
# From repository root
export DOTNET_ROLL_FORWARD=Major  # If needed
dotnet build src/PublicApi/PublicApi.csproj
```

## Running the Application

The application uses an in-memory database by default (configured in launchSettings.json):

```bash
cd src/PublicApi
dotnet run
```

The application will:
1. Start the PublicApi service on `https://localhost:25843`
2. Initialize the in-memory database
3. Show Swagger UI at `https://localhost:25843/swagger`

**Note**: The in-memory database loses all data on restart. User IDs and subscription mappings only persist within a single run.

## Verification Steps

### Step 1: Generate a JWT Token

First, authenticate to get a JWT token:

```bash
# Authenticate
curl -X POST https://localhost:25843/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }' \
  --insecure
```

Expected response:
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com",
  "correlationId": "..."
}
```

Copy the `token` value for use in subsequent requests.

### Step 2: List Available Subscription Plans

```bash
curl -X GET https://localhost:25843/api/subscription-plans \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  --insecure
```

Expected response:
```json
{
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00
    },
    {
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00
    }
  ],
  "success": true
}
```

### Step 3: Create a Subscription

Subscribe the authenticated user to the Pro plan:

```bash
curl -X POST https://localhost:25843/api/subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  --insecure
```

Expected response:
```json
{
  "success": true,
  "subscriptionId": 12345,
  "planHandle": "eshop-pro",
  "state": "active",
  "currentPrice": 299.00,
  "nextBillingAt": "2026-10-06T12:00:00Z",
  "correlationId": "..."
}
```

### Step 4: Retrieve User Subscriptions

Get the authenticated user's current subscriptions:

```bash
curl -X GET https://localhost:25843/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  --insecure
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 1,
      "planHandle": "eshop-pro",
      "state": "active",
      "currentPrice": 299.00,
      "nextBillingAt": "2026-10-06T12:00:00Z",
      "createdAt": "2026-09-06T12:00:00Z"
    }
  ],
  "success": true
}
```

## API Endpoints

### GET /api/subscription-plans
**Authorization**: Required (JWT Bearer token)

List all available subscription plans from Maxio.

**Response**: `200 OK`
```json
{
  "plans": [SubscriptionPlanDto],
  "success": boolean,
  "errorMessage": string | null
}
```

### POST /api/subscriptions
**Authorization**: Required (JWT Bearer token)

Create a new subscription for the authenticated user.

**Request Body**:
```json
{
  "planHandle": "string"
}
```

**Response**: `201 Created`
```json
{
  "success": boolean,
  "subscriptionId": int,
  "planHandle": "string",
  "state": "string",
  "currentPrice": decimal,
  "nextBillingAt": datetime | null,
  "errorMessage": string | null
}
```

### GET /api/my-subscriptions
**Authorization**: Required (JWT Bearer token)

Retrieve all subscriptions for the authenticated user.

**Response**: `200 OK`
```json
{
  "subscriptions": [UserSubscriptionDto],
  "success": boolean,
  "errorMessage": string | null
}
```

## Architecture Overview

### Integration Flow

```
eShopOnWeb User (JWT Auth)
       ↓
    PublicApi Endpoints
       ↓
   Maxio Service (HTTP Client)
       ↓
   Maxio Advanced Billing API
       ↓
   Sandbox Site (cp-exp-4)
```

### Key Components

1. **Subscription Entity** (`ApplicationCore/Entities/SubscriptionAggregate/`)
   - Stores user subscription data with Maxio IDs
   - Tracks subscription state and pricing
   - Implements IAggregateRoot for repository pattern

2. **Maxio Service** (`PublicApi/MaxioIntegration/MaxioService.cs`)
   - Handles HTTP communication with Maxio API
   - Implements HTTP Basic Auth (API Key + "x" as password)
   - Manages customer creation/retrieval and subscription operations
   - Provides idempotent customer creation (double-click safe)

3. **Subscription Endpoints** (`PublicApi/SubscriptionEndpoints/`)
   - `GetSubscriptionPlansEndpoint`: Lists available plans
   - `CreateSubscriptionEndpoint`: Creates new subscriptions
   - `GetMySubscriptionsEndpoint`: Retrieves user's subscriptions

4. **Database** (`Infrastructure/Data/`)
   - In-memory database for development
   - Migrations support for production database
   - Subscription table with user/Maxio linkage

## Troubleshooting

### 401 Unauthorized on Endpoints
- Ensure you have a valid JWT token
- Check that the Bearer token format is correct: `Authorization: Bearer <token>`
- Verify the token hasn't expired

### 400 Bad Request from Maxio Endpoints
- Verify Maxio credentials are correctly set in environment/secrets
- Check that the product family handle is correct (`eshop-subscribe` for demo)
- Ensure plan handles match the sandbox products (`eshop-pro`, `basic-plan`)

### "Maxio credentials not configured"
- Verify environment variables are set: `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`
- Check that user secrets are properly set if using secrets manager
- Restart the application after setting environment variables

### HTTPS Certificate Errors
- The dev certificate must be trusted: `dotnet dev-certs https --check`
- If not trusted, run: `dotnet dev-certs https --trust`
- Use `--insecure` flag with curl if certificate issues persist

## Production Deployment Considerations

When deploying to production:

1. **Credentials Management**
   - Never store secrets in configuration files
   - Use Azure Key Vault, AWS Secrets Manager, or similar
   - Ensure `appsettings.json` has empty placeholders only

2. **Database**
   - Replace in-memory database with SQL Server
   - Update connection strings in configuration
   - Run migrations: `dotnet ef database update -c CatalogContext`

3. **Maxio Site**
   - Switch from sandbox to production Maxio site
   - Update `MAXIO_SITE_SUBDOMAIN` to production subdomain
   - Update product family and plan handles to production values

4. **Security**
   - Ensure HTTPS is enforced (`UseHttpsRedirection()`)
   - Implement rate limiting on subscription endpoints
   - Add audit logging for subscription changes
   - Validate user payment method if required

5. **Error Handling**
   - Implement comprehensive error logging
   - Add webhook handlers for Maxio events (subscription state changes)
   - Implement retry logic for transient failures
   - Add circuit breaker for Maxio API unavailability

## Additional Resources

- [Maxio Advanced Billing API Documentation](https://developers.maxio.com/)
- [Maxio Authentication](https://developers.maxio.com/http/getting-started/authentication)
- [Maxio Create Customer Endpoint](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/create-customer)
- [Maxio Create Subscription Endpoint](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/subscriptions/create-subscription)
