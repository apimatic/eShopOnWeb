# Maxio Subscription Integration Setup

This guide covers setting up and running the eShopOnWeb subscription billing integration with Maxio Advanced Billing.

## Prerequisites

- .NET 10 SDK (or .NET 8 with updated global.json)
- Environment variables from Maxio:
  - `MAXIO_API_KEY`: Your Maxio API key
  - `MAXIO_SITE_SUBDOMAIN`: Your Maxio sandbox subdomain (e.g., `sandbox-example`)
  - `MAXIO_DEFAULT_PRODUCT_FAMILY`: The product family handle (default: `eshop-subscribe`)
  - `MAXIO_BASE_URL` (optional): Override the base URL if needed

## Setup Instructions

### 1. Configure Environment Variables

Set the following environment variables in your system, or use a `.env` file:

```powershell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "your-sandbox-subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

### 2. Initialize User Secrets (for PublicApi)

From the repo root, run the setup script:

```powershell
.\setup-maxio-secrets.ps1
```

Or manually configure secrets:

```powershell
cd src/PublicApi
dotnet user-secrets init --force
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-sandbox-subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

### 3. Running the Application

The application requires an in-memory database since LocalDB is not available. Run with:

```powershell
cd src/PublicApi
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run
```

Or set the environment variable in your system/shell permanently and just run:

```powershell
dotnet run
```

The PublicApi will start on the port defined in `appsettings.json` (default: https://localhost:25603).

## API Endpoints

### 1. Get JWT Token

Before calling subscription endpoints, authenticate:

```http
POST https://localhost:25603/api/authenticate
Content-Type: application/json

{
  "username": "demouser@example.com",
  "password": "Pass@word1"
}
```

Response includes a `token` field - use this for subsequent requests.

### 2. List Subscription Plans

```http
GET https://localhost:25603/api/subscription-plans
```

Returns available plans from the configured Maxio product family.

### 3. Create a Subscription

```http
POST https://localhost:25603/api/subscriptions
Authorization: Bearer <your-jwt-token>
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

Creates a subscription for the authenticated user. The system automatically:
- Creates or retrieves the Maxio customer (using eShopOnWeb user ID as reference)
- Creates the subscription
- Returns confirmation with billing details

### 4. List My Subscriptions

```http
GET https://localhost:25603/api/my-subscriptions
Authorization: Bearer <your-jwt-token>
```

Returns all subscriptions for the authenticated user.

## Verification Checklist

### Build & Start
- [ ] Project builds without errors: `dotnet build src/PublicApi/PublicApi.csproj`
- [ ] Application starts: `dotnet run` (in src/PublicApi)
- [ ] Swagger UI available at https://localhost:25603/swagger

### Authentication
- [ ] Can authenticate with demo user credentials
- [ ] JWT token includes both Name and NameIdentifier claims
- [ ] Token is accepted by subscription endpoints

### Subscription Plans
- [ ] GET /api/subscription-plans returns list of plans
- [ ] Each plan includes name, handle, price, billing interval
- [ ] Plans from Maxio product family "eshop-subscribe" are listed

### Create Subscription
- [ ] POST /api/subscriptions (unauthorized) returns 401
- [ ] POST /api/subscriptions (with token) creates subscription
- [ ] Maxio customer is created if not exist
- [ ] Subscription is created with product handle
- [ ] Response includes subscription ID, state, and billing info

### My Subscriptions
- [ ] GET /api/my-subscriptions (unauthorized) returns 401
- [ ] GET /api/my-subscriptions returns user's subscriptions
- [ ] Returns empty list if user has no subscriptions
- [ ] Returns subscriptions with full details (state, pricing, dates)

### Idempotency
- [ ] Creating subscription twice doesn't create duplicate customers
- [ ] Second subscription attempt uses existing customer
- [ ] Both subscriptions appear in list

## Configuration

Settings are configured through three layers (in order of precedence):

1. **User Secrets** (during development): `dotnet user-secrets set "key" "value"`
2. **Environment Variables**: `Maxio__ApiKey`, `Maxio__Subdomain`, etc.
3. **appsettings.json**: Default/placeholder values

### Maxio Configuration Keys

```json
{
  "Maxio": {
    "ApiKey": "from secrets/env",
    "Subdomain": "your-sandbox-name",
    "ProductFamilyHandle": "eshop-subscribe",
    "BaseUrl": "" // Leave empty to derive from Subdomain
  }
}
```

## Troubleshooting

### Issue: "Maxio:ApiKey must be configured"
- Verify environment variables are set
- Run `dotnet user-secrets list` to check secrets
- Ensure you're in the PublicApi directory when setting secrets

### Issue: "The type or namespace name 'InvalidOperationException' could not be found"
- Run `dotnet clean` and rebuild
- Ensure all using statements are properly included

### Issue: Connection errors to Maxio
- Verify API key is correct
- Verify subdomain matches your sandbox
- Check network connectivity
- Ensure you're targeting the correct Maxio environment (sandbox vs production)

### Issue: 401 Unauthorized on subscription endpoints
- Verify you're passing the JWT token in Authorization header: `Authorization: Bearer <token>`
- Verify token is valid and hasn't expired
- Check the token was obtained from the authenticate endpoint

### Issue: In-memory database lost after restart
- This is expected behavior - in-memory DB is not persisted
- Each run starts with fresh data
- For persistent storage, configure SQL Server connection strings instead

## Production Deployment

For production deployment:

1. **Remove in-memory database**: Configure real SQL Server databases
2. **Secure credentials**: Never commit secrets - use Azure Key Vault or similar
3. **API Key management**: Rotate Maxio API keys regularly
4. **HTTPS enforcement**: Ensure UseHttpsRedirection is enabled
5. **Error handling**: Implement proper error logging and monitoring
6. **Rate limiting**: Add rate limiting to subscription endpoints
7. **Audit logging**: Log all subscription creation/modification events

## Architecture

```
Endpoints (HTTP handlers)
    ↓
ISubscriptionService (Business Logic)
    ↓
IMaxioApiClient (External API Integration)
    ↓
MaxioOptions (Configuration)
```

- **Endpoints**: Minimal API handlers that accept HTTP requests, extract user context, call service
- **SubscriptionService**: Business logic for subscription operations (plans, create, list)
- **MaxioApiClient**: HTTP client that communicates with Maxio API per the OpenAPI spec
- **MaxioOptions**: Configuration binding for Maxio credentials and URLs

## References

- Maxio OpenAPI Specification: `maxio-spec/openapi.yaml`
- eShopOnWeb Reference Architecture
- JWT Authentication Flow (PublicApi)
