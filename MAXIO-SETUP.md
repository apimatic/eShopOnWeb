# Maxio Subscription Billing Setup

## Prerequisites

- .NET SDK 8.0 or later (with rollForward enabled to use .NET 10)
- Maxio sandbox account credentials
- HTTPS dev certificate trusted (`dotnet dev-certs https --check`)

## Environment Setup

### 1. Configure Maxio Credentials

Store your Maxio credentials in user secrets (never commit them to the repository):

```bash
cd src/PublicApi

# Set the API key
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"

# Set the subdomain (e.g., "cp-exp-3")
dotnet user-secrets set "Maxio:Subdomain" "your-subdomain"

# Set the product family handle (e.g., "eshop-subscribe")
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: Override the base URL (leave empty to auto-derive from subdomain)
dotnet user-secrets set "Maxio:BaseUrl" ""
```

### 2. Configure Database

The integration uses in-memory database by default for development. If you want to use SQL Server:

Set the environment variable:
```bash
set UseOnlyInMemoryDatabase=false
```

Or run with:
```bash
$env:UseOnlyInMemoryDatabase = $false
```

### 3. Handle .NET Runtime Mismatch

If global.json pins .NET 8.0 but you only have .NET 10:

```bash
set DOTNET_ROLL_FORWARD=Major
```

Or in PowerShell:
```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
```

## Running the Application

```bash
cd src/PublicApi
dotnet run
```

The API will be available at: `https://localhost:24783`

Swagger documentation: `https://localhost:24783/swagger`

## Testing the Integration

### 1. Create a Test User

First, authenticate to get a JWT token:

```bash
curl -X POST https://localhost:24783/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username": "demouser", "password": "DemoPassword123!"}'
```

Response includes the `token` field.

### 2. Get Available Subscription Plans

```bash
curl -X GET https://localhost:24783/api/subscription-plans \
  -H "Authorization: Bearer <TOKEN>"
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional plan",
      "price": 299.00,
      "billingCycle": "1 month"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic plan",
      "price": 29.00,
      "billingCycle": "1 month"
    }
  ]
}
```

### 3. Create a Subscription

```bash
curl -X POST https://localhost:24783/api/subscriptions \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}'
```

Expected response:
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "nextBillingDate": "2024-10-06T12:00:00Z",
  "message": "Subscription created successfully"
}
```

### 4. Get My Subscriptions

```bash
curl -X GET https://localhost:24783/api/my-subscriptions \
  -H "Authorization: Bearer <TOKEN>"
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productName": "Pro Plan",
      "price": 299.00,
      "currentPeriodEndsAt": "2024-10-06T12:00:00Z",
      "nextAssessmentAt": "2024-10-06T12:00:00Z",
      "activatedAt": "2024-09-06T12:00:00Z"
    }
  ]
}
```

## Idempotency

The subscription creation is idempotent:
- Double-clicking the subscribe button will NOT create duplicate subscriptions
- The first call creates a Maxio customer and subscription
- Subsequent calls for the same user reuse the existing Maxio customer ID
- Attempting to create another subscription with the same plan will fail with Maxio's validation (user already subscribed)

## Troubleshooting

### "Maxio service is not configured"
- Verify that `Maxio:Subdomain` and `Maxio:ProductFamilyHandle` are set in user secrets

### "HTTP Response Not OK. Status code: 401"
- Verify your Maxio API key is correct
- Ensure the key is set in user secrets: `dotnet user-secrets set "Maxio:ApiKey" "..."`

### "Subscription created successfully" but no data in response
- Check that the Maxio product family handle is correct
- Verify products exist in the family using Maxio dashboard

### Connection errors
- Ensure HTTPS dev cert is trusted: `dotnet dev-certs https --check`
- If not trusted: `dotnet dev-certs https --trust`

## Production Deployment

When deploying to production:

1. Never hardcode credentials in appsettings files
2. Use environment variables or a secrets management system (Azure Key Vault, AWS Secrets Manager)
3. Set these configuration values:
   - `Maxio:ApiKey` - from secure storage
   - `Maxio:Subdomain` - production Maxio site subdomain
   - `Maxio:ProductFamilyHandle` - handle of products to offer
   - `Maxio:BaseUrl` - optional override for API base URL

4. Configure proper logging and monitoring
5. Test subscription lifecycle thoroughly
6. Implement webhook handling for subscription events (future enhancement)
