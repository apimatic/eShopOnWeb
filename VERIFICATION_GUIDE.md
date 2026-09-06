# Maxio Integration - Step-by-Step Verification Guide

Follow these steps to verify the Maxio subscription billing integration is working correctly.

## Prerequisites

- .NET 10 SDK installed
- Maxio sandbox API credentials (API Key and Subdomain)
- Access to the sandbox environment at `cp-exp-2`

## Step 1: Build the Project

```powershell
# Set environment variables
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"

# Navigate to repo root
cd C:\claude-runs\t1h45ali-maxio-docs-mcp-haiku45high-016\repo

# Build the solution
dotnet build eShopOnWeb.sln -c Debug

# Expected: Build succeeded with 0 errors
```

## Step 2: Configure Maxio Credentials

Choose **one** of these methods:

### Method A: Environment Variables (Recommended for CI/CD)

```powershell
$env:Maxio__ApiKey = "YOUR_MAXIO_API_KEY"
$env:Maxio__Subdomain = "cp-exp-2"
$env:Maxio__ProductFamilyHandle = "eshop-subscribe"
```

### Method B: .NET User Secrets (Recommended for Development)

```powershell
cd src/PublicApi

# Set each secret
dotnet user-secrets set "Maxio:ApiKey" "YOUR_MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Verify secrets are set
dotnet user-secrets list

# Expected output:
# Maxio:ApiKey = YOUR_MAXIO_API_KEY
# Maxio:Subdomain = cp-exp-2
# Maxio:ProductFamilyHandle = eshop-subscribe
```

## Step 3: Start the PublicApi Server

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"

cd C:\claude-runs\t1h45ali-maxio-docs-mcp-haiku45high-016\repo\src\PublicApi

dotnet run --launch-profile PublicApi

# Expected output:
# Now listening on: https://localhost:25563
# Now listening on: http://localhost:25564
# Application started. Press Ctrl+C to shut down.
```

The server should start without errors. The application creates an in-memory database and seeds initial data.

## Step 4: Access Swagger Documentation

Open a browser and navigate to:
```
https://localhost:25563/swagger/index.html
```

You should see the Swagger UI with all API endpoints listed, including:
- `/api/authenticate`
- `/api/subscription-plans`
- `/api/subscriptions`
- `/api/my-subscriptions`

## Step 5: Authenticate and Get a JWT Token

### Via Swagger UI:
1. Click on `POST /api/authenticate` endpoint
2. Click "Try it out"
3. Enter credentials:
   ```json
   {
     "username": "demouser@microsoft.com",
     "password": "Pass@word1"
   }
   ```
4. Click "Execute"
5. Copy the `token` value from the response

### Via curl:
```bash
curl -k -X POST https://localhost:25563/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

**Expected Response:**
```json
{
  "result": true,
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com",
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false
}
```

## Step 6: Test Subscription Plans Endpoint

### Via Swagger UI:
1. Click on `GET /api/subscription-plans`
2. Click the lock icon and paste your JWT token (just the token value, without "Bearer")
3. Click "Try it out" → "Execute"

### Via curl:
```bash
curl -k -X GET https://localhost:25563/api/subscription-plans \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

**If you see this, ✅ subscription plans are working!**

## Step 7: Test Create Subscription Endpoint

### Via Swagger UI:
1. Click on `POST /api/subscriptions`
2. Click the lock icon to authenticate with your JWT token
3. Click "Try it out"
4. Enter request body:
   ```json
   {
     "planHandle": "eshop-pro",
     "firstName": "John",
     "lastName": "Doe",
     "email": "john.doe@test.example.com"
   }
   ```
5. Click "Execute"

### Via curl:
```bash
curl -k -X POST https://localhost:25563/api/subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro",
    "firstName": "Test",
    "lastName": "User",
    "email": "test@example.com"
  }'
```

**Expected Response:**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productName": "Pro Plan",
  "price": 299.00,
  "nextBillingAt": "2026-10-06T12:00:00Z",
  "message": "Subscription created successfully"
}
```

**If you see this, ✅ subscription creation is working!**

Note: The subscription ID should be a real ID from Maxio. You can verify it exists by logging into the Maxio dashboard at `https://cp-exp-2.maxio.com`.

## Step 8: Test List User Subscriptions Endpoint

### Via Swagger UI:
1. Click on `GET /api/my-subscriptions`
2. Click the lock icon to authenticate with your JWT token
3. Click "Try it out" → "Execute"

### Via curl:
```bash
curl -k -X GET https://localhost:25563/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "price": 299.00,
      "balance": 0.00,
      "currentPeriodEndsAt": "2026-10-06T12:00:00Z",
      "nextBillingAt": "2026-10-06T12:00:00Z",
      "createdAt": "2026-09-06T12:00:00Z"
    }
  ],
  "message": null
}
```

**If you see this, ✅ listing subscriptions is working!**

## Verification Checklist

After completing the steps above, verify:

- [ ] Project builds without errors
- [ ] PublicApi starts and listens on https://localhost:25563
- [ ] Swagger UI is accessible and lists all subscription endpoints
- [ ] `/api/authenticate` returns a valid JWT token
- [ ] `/api/subscription-plans` returns list of available plans (Pro Plan, Basic Plan)
- [ ] `/api/subscriptions` creates a new subscription and returns subscription ID
- [ ] `/api/my-subscriptions` returns the subscriptions created in previous step
- [ ] All responses include correct pricing and plan information

## Troubleshooting

### 401 Unauthorized on subscription endpoints
**Issue**: JWT token is missing or invalid
**Solution**: 
- Verify token was copied correctly from authenticate response
- In Swagger UI, click the lock icon and enter token without "Bearer" prefix
- Try getting a fresh token

### 500 Internal Server Error
**Issue**: Could be Maxio connection issue or configuration problem
**Solution**:
- Check that `Maxio:ApiKey`, `Maxio:Subdomain`, and `Maxio:ProductFamilyHandle` are set
- Verify credentials are correct by testing in Maxio dashboard
- Check server console logs for detailed error messages

### "Invalid plan handle" error
**Issue**: Plan handle doesn't exist in Maxio
**Solution**:
- Use one of the seeded handles: `eshop-pro` or `basic-plan`
- Verify the product family handle is `eshop-subscribe`

### No subscriptions returned from GET /api/my-subscriptions
**Issue**: Subscription was created with different user credentials
**Solution**:
- Each JWT token is tied to a specific user
- Create subscriptions with the same account you're querying with
- The userId from the JWT claim is used as the Maxio customer reference

### "The hostname could not be parsed" error
**Issue**: Maxio Subdomain is not configured
**Solution**:
```powershell
# Check if environment variable is set
$env:Maxio__Subdomain

# Or check user secrets
dotnet user-secrets list

# Then restart the application
```

## Performance Notes

- **First request**: May take 2-3 seconds due to EF Core initialization
- **Subsequent requests**: Should respond in <500ms
- **Plan listing**: Usually very fast, cached by Maxio response structure
- **Creating subscriptions**: May take 1-2 seconds due to Maxio API latency

## Security Verification

The integration includes proper security:
- ✅ All subscription endpoints require JWT authentication
- ✅ Credentials are not logged or exposed in responses
- ✅ HTTPS is enforced for all external API calls
- ✅ Basic auth is used for Maxio API (credentials never in query params/URLs)
- ✅ No secrets committed to repository

## Next Steps

If all verifications pass:

1. **Production Setup**: See [MAXIO_SETUP.md](./MAXIO_SETUP.md) for production configuration guidance
2. **Integration Testing**: Write automated tests for subscription workflows
3. **Webhook Setup**: Configure Maxio webhooks for subscription lifecycle events
4. **Monitoring**: Add application insights and logging

If you encounter issues, check:
1. Server console logs for detailed error messages
2. Maxio dashboard to verify customer and subscription were created
3. Verify network connectivity to Maxio API
4. Ensure credentials have appropriate permissions in Maxio

## Support Resources

- [Maxio Billing API Docs](https://docs.maxio.com) - Official API documentation
- [MAXIO_SETUP.md](./MAXIO_SETUP.md) - Setup and configuration guide
- [IMPLEMENTATION_SUMMARY.md](./IMPLEMENTATION_SUMMARY.md) - Architecture and design details
- Maxio Sandbox Dashboard: `https://cp-exp-2.maxio.com`
