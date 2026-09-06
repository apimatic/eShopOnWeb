# Maxio Subscription Billing Integration Setup

This document provides step-by-step instructions to configure and verify the Maxio subscription billing integration for eShopOnWeb.

## Prerequisites

- .NET 10 SDK (or .NET 8 SDK with rollForward enabled)
- ASP.NET Core 8.0 runtime
- Maxio (formerly Chargify) sandbox account with:
  - API key
  - Site subdomain
  - Product Family handle: `eshop-subscribe`
  - Plans already seeded (e.g., `eshop-pro` and `basic-plan`)

## Environment Variable Setup

The integration reads Maxio credentials from environment variables and stores them in user-secrets. Set these variables before running:

```bash
# Set these in your terminal or system environment
set MAXIO_API_KEY=your_api_key_here
set MAXIO_SITE_SUBDOMAIN=your_subdomain_here
set MAXIO_ENVIRONMENT=production
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
```

### For PowerShell:
```powershell
$env:MAXIO_API_KEY = "your_api_key_here"
$env:MAXIO_SITE_SUBDOMAIN = "your_subdomain_here"
$env:MAXIO_ENVIRONMENT = "production"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

## User Secrets Configuration

User secrets store sensitive configuration locally (never committed to git). Initialize them with:

```bash
cd src/PublicApi

# Initialize user secrets (usually already done)
dotnet user-secrets init

# Store the Maxio configuration
dotnet user-secrets set "Maxio:ApiKey" "%MAXIO_API_KEY%"
dotnet user-secrets set "Maxio:Subdomain" "%MAXIO_SITE_SUBDOMAIN%"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "%MAXIO_DEFAULT_PRODUCT_FAMILY%"

cd ../..
```

### For PowerShell:
```powershell
cd src/PublicApi

dotnet user-secrets init

dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY

cd ../..
```

## Building and Running

### Build the solution:
```bash
dotnet build eShopOnWeb.sln -c Debug
```

### Run PublicApi:
```bash
dotnet run --project src/PublicApi/PublicApi.csproj --environment Development
```

The API should start on `https://localhost:27403` with Swagger UI available at `https://localhost:27403/swagger`.

## Testing the Integration

### 1. Authenticate and Get JWT Token

First, get a token by authenticating with test credentials:

```bash
curl -X POST https://localhost:27403/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

**Note**: Adjust credentials to match your test user. Default test user is `demouser@microsoft.com` / `Pass@word1`.

You'll receive a response with a `token` field:
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com"
}
```

Save this token for the next requests.

### 2. Get Available Subscription Plans

```bash
curl -X GET https://localhost:27403/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json"
```

Expected response (200 OK):
```json
{
  "correlationId": "...",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "price": 299.00,
      "description": "Professional plan with advanced features"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "price": 29.00,
      "description": "Basic plan for getting started"
    }
  ]
}
```

### 3. Create a Subscription (Hero Flow)

Subscribe the authenticated user to a plan:

```bash
curl -X POST https://localhost:27403/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}'
```

Expected response (200 OK):
```json
{
  "correlationId": "...",
  "subscriptionId": 12345678,
  "customerId": 98765432,
  "state": "active",
  "activatedAt": "2026-09-07T12:34:56Z",
  "nextBillingDate": "2026-10-07T12:34:56Z",
  "monthlyPrice": 0
}
```

**Important Notes**:
- The first subscription creation for a user will automatically create a Maxio customer (idempotent)
- No payment method is required (configured in Maxio)
- The subscription state should be "active" immediately
- Subsequent API calls with the same email will reuse the customer

### 4. Get User's Subscriptions

Retrieve all subscriptions for the authenticated user:

```bash
curl -X GET https://localhost:27403/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Accept: application/json"
```

Expected response (200 OK):
```json
{
  "correlationId": "...",
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "activatedAt": "2026-09-07T12:34:56Z",
      "nextBillingDate": "2026-10-07T12:34:56Z"
    }
  ]
}
```

## Swagger UI Testing

You can also test interactively via Swagger:

1. Navigate to `https://localhost:27403/swagger`
2. Click "Authorize" button
3. Enter: `Bearer YOUR_TOKEN_HERE`
4. Click "Authorize"
5. Try out endpoints from the UI

## Troubleshooting

### "Failed to create or retrieve customer" Error

**Cause**: User email not found in JWT token.

**Solution**: Ensure:
1. Token is valid and not expired
2. Your test user has an email claim (check the authentication endpoint response)
3. User-secrets are correctly configured with Maxio credentials

### "Product family 'eshop-subscribe' not found" Error

**Cause**: Product family doesn't exist or credentials are wrong.

**Solution**:
1. Verify Maxio credentials in user-secrets: `dotnet user-secrets list`
2. Verify product family handle in Maxio dashboard
3. Ensure correct Maxio site subdomain

### 401 Unauthorized

**Cause**: Missing or invalid JWT token.

**Solution**:
1. Get a fresh token using the authenticate endpoint
2. Include `Authorization: Bearer <token>` header
3. Token may have expired (get a new one)

### Connection Refused on https://localhost:27403

**Cause**: API not running or dev certificate issues.

**Solution**:
1. Ensure dev cert is trusted: `dotnet dev-certs https --check`
2. If not trusted: `dotnet dev-certs https --trust`
3. Start the API: `dotnet run --project src/PublicApi/PublicApi.csproj`

## API Endpoints Reference

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | `/api/subscription-plans` | List available subscription plans | Required |
| POST | `/api/subscriptions` | Subscribe authenticated user to a plan | Required |
| GET | `/api/my-subscriptions` | Get subscriptions for authenticated user | Required |
| POST | `/api/authenticate` | Authenticate and receive JWT token | Not Required |

## Architecture Details

### Components

1. **IMaxioClient** (ApplicationCore/Interfaces)
   - Abstraction for Maxio API interactions
   - Methods: CreateOrGetCustomerAsync, CreateSubscriptionAsync, GetProductsAsync, GetCustomerSubscriptionsAsync

2. **MaxioClient** (Infrastructure/Services)
   - Implementation using HttpClient with Basic Auth
   - Handles JSON serialization/deserialization
   - Implements idempotent customer creation (lookup before create)

3. **SubscriptionEndpoints** (PublicApi)
   - GetSubscriptionPlansEndpoint: Lists available plans
   - CreateSubscriptionEndpoint: Creates subscription (hero flow)
   - GetMySubscriptionsEndpoint: Retrieves user's subscriptions
   - All require JWT authentication

4. **MaxioSettings** (ApplicationCore)
   - Configuration holder for Maxio credentials
   - Loads from IConfiguration (user-secrets in dev, environment in prod)

### Data Flow

```
User (JWT Token)
    ↓
[Subscription Endpoint]
    ↓
[Extract Email from JWT Claims]
    ↓
[IMaxioClient.CreateOrGetCustomer]
    ↓
[Maxio API: Lookup/Create Customer]
    ↓
[IMaxioClient.CreateSubscription]
    ↓
[Maxio API: Create Subscription]
    ↓
[Return Confirmation to User]
```

## Production Considerations

1. **Error Handling**: Current implementation includes try-catch with appropriate HTTP status codes
2. **Logging**: Add ILogger<T> for production debugging
3. **Idempotency**: Customer lookup before creation prevents duplicates
4. **Configuration**: Use Azure Key Vault or similar for production secrets
5. **Database Persistence**: Currently relies on Maxio as source of truth (consider persisting state in EF Core)
6. **Rate Limiting**: Consider adding rate limiting for subscription creation
7. **Validation**: Add model validation for request DTOs

## Next Steps

1. Set up environment variables and user-secrets (see above)
2. Build: `dotnet build eShopOnWeb.sln`
3. Run PublicApi: `dotnet run --project src/PublicApi/PublicApi.csproj`
4. Test endpoints (see Testing section above)
5. Monitor logs for any integration issues
6. Review Maxio dashboard to confirm subscriptions are being created

## Additional Resources

- Maxio OpenAPI Spec: `maxio-spec/openapi.yaml`
- PublicApi README: `src/PublicApi/README.md`
- Maxio Documentation: https://docs.maxio.com/
- Maxio API Reference: https://maxio.zendesk.com/hc/en-us/articles/24294819360525-API-Keys
