# Maxio Subscription Billing - Verification Guide

This guide provides step-by-step instructions to verify the Maxio subscription billing integration is working correctly.

## Prerequisites

1. **Maxio Sandbox Account**: You need access to a Maxio (formerly Chargify) sandbox site with:
   - API Key for authentication
   - Site subdomain (e.g., `cp-exp-3`)
   - Pre-configured product family with handle `eshop-subscribe`
   - At least two subscription plans in that family

2. **Credentials**: You should have received the following from Maxio sandbox setup:
   - `MAXIO_API_KEY`: Your API key
   - `MAXIO_SITE_SUBDOMAIN`: Your site subdomain
   - Product family handle: `eshop-subscribe` (default)

3. **.NET Environment**:
   - .NET 8.0 SDK or .NET 10 SDK (global.json allows rollForward)
   - PowerShell 5.1+ or PowerShell Core

## Verification Steps

### 1. Build the Solution

```bash
cd C:\claude-runs\t1h45ali-openapi-haiku45high-029\repo
dotnet build eShopOnWeb.sln
```

**Expected Result**: Build succeeds with no errors (warnings about vulnerabilities are pre-existing).

### 2. Configure Maxio Credentials

Choose ONE of the following approaches:

#### Option A: User Secrets (Recommended for Development)

```powershell
cd src/PublicApi
dotnet user-secrets init
dotnet user-secrets set "Maxio:ApiKey" "your-actual-api-key"
dotnet user-secrets set "Maxio:Subdomain" "your-actual-subdomain"
dotnet user-secrets set "Maxio:Environment" "sandbox"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
cd ..\..
```

#### Option B: Environment Variables

```powershell
$env:MAXIO_API_KEY = "your-actual-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "your-actual-subdomain"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

#### Option C: Using Setup Script

```powershell
.\setup-secrets.ps1 -ApiKey "your-api-key" -Subdomain "your-subdomain"
```

### 3. Run the PublicApi Service

```bash
cd src/PublicApi
dotnet run
```

**Expected Output**:
```
... (initialization logs)
PublicApi App created...
Seeding Database...
LAUNCHING PublicApi
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:28623
```

The service should be running and accessible at `https://localhost:28623`.

### 4. Test the API Endpoints

In a new terminal/PowerShell window, run the test script:

```powershell
.\test-subscription-api.ps1
```

This will:
1. Authenticate as the demo user
2. List available subscription plans
3. Create a subscription
4. Retrieve the user's subscriptions

**Expected Output**:
```
Testing Subscription API Endpoints
=================================

Step 1: Authenticating...
✓ Authentication successful!
Token: eyJhbGciOiJIUzI1NiIs...

Step 2: Listing subscription plans...
✓ Retrieved plans successfully!

  - $299/month Pro Plan (eshop-pro)
    Price: $299.00/month
    ID: 7126957

  - $29/month Basic Plan (basic-plan)
    Price: $29.00/month
    ID: 7126958

Step 3: Creating subscription to eshop-pro...
✓ Subscription created successfully!
  Subscription ID: 12345678
  State: active
  Product: $299/month Pro Plan
  Price: $299.00
  Next Assessment: 2024-10-07T14:00:00Z

Step 4: Retrieving user's subscriptions...
✓ Retrieved subscriptions successfully!
  Count: 1

  Subscription: 12345678
    Product: $299/month Pro Plan
    State: active
    Price: $299.00
    Active Since: 2024-09-07T14:00:00Z
    Next Billing: 2024-10-07T14:00:00Z

=================================
All tests passed! ✓

The subscription API is working correctly.
```

### 5. Manual API Testing (Optional)

If you prefer to test manually with curl or Postman:

#### Get Authentication Token

```bash
curl -X POST https://localhost:28623/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  -k
```

Copy the returned token.

#### List Plans

```bash
curl -X GET https://localhost:28623/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -k
```

#### Create Subscription

```bash
curl -X POST https://localhost:28623/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "planHandle": "eshop-pro"
  }' \
  -k
```

#### Get User's Subscriptions

```bash
curl -X GET https://localhost:28623/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -k
```

## Verification Checklist

- [ ] Solution builds without errors
- [ ] User secrets or environment variables are set
- [ ] PublicApi starts successfully
- [ ] Authentication endpoint works (returns token)
- [ ] List subscription plans endpoint works (returns plans)
- [ ] Create subscription endpoint works (subscription created in Maxio)
- [ ] Get my subscriptions endpoint works (returns user's subscriptions)
- [ ] Verify in Maxio admin that customer was created
- [ ] Verify in Maxio admin that subscription was created
- [ ] Multiple subscription creates don't create duplicate customers (idempotent)

## Expected Behavior

### Idempotent Customer Creation
If you call the create subscription endpoint multiple times with the same user:
- First call: Creates a new Maxio customer with reference `eshop-{userId}`
- Subsequent calls: Reuses the existing customer (no duplicates)

### Database Mapping
The application tracks the mapping between:
- `ApplicationUser.Id` (eShopWeb user)
- `MaxioCustomer.Id` (Maxio customer)

This mapping is stored in the `MaxioCustomerMappings` table in the Identity database.

### Subscription State
Subscriptions created without payment method (as per sandbox setup) will be in `active` state immediately because:
- Plans have `payment_method_not_required = true`
- Subscriptions are created with `payment_collection_method = "remittance"`

## Troubleshooting

### Issue: "Maxio is not configured"
**Cause**: ApiKey or Subdomain not set
**Solution**: Ensure you set the user secrets or environment variables before starting the app

### Issue: "Failed to create or get Maxio customer"
**Cause**: Invalid credentials or Maxio API error
**Solution**: 
- Verify API key is correct
- Check subdomain is correct
- Verify Maxio site is accessible

### Issue: "No plans found"
**Cause**: Product family doesn't exist or handle is wrong
**Solution**:
- Verify product family handle is `eshop-subscribe`
- Check product family exists in Maxio with seeded plans
- Verify `MAXIO_DEFAULT_PRODUCT_FAMILY` is set correctly

### Issue: "Plan handle is required"
**Cause**: Client didn't send planHandle in request body
**Solution**: Include `planHandle` in the POST /api/subscriptions request

### Issue: HTTPS certificate errors
**Cause**: Self-signed dev certificate
**Solution**: Use `-k` flag in curl or disable cert verification in your client

## Architecture Summary

### Database Entities
- **MaxioCustomerMapping**: Links ApplicationUser to Maxio Customer ID
  - ApplicationUserId (primary key)
  - MaxioCustomerId (Maxio customer ID)
  - CreatedAt, UpdatedAt timestamps

### Services
- **IMaxioApiService**: Handles all Maxio API communication
  - CreateOrGetCustomerAsync: Idempotent customer creation
  - GetProductsByFamilyHandleAsync: Lists plans in a family
  - CreateSubscriptionAsync: Creates subscription
  - GetSubscriptionAsync: Retrieves subscription
  - GetCustomerSubscriptionsAsync: Lists customer's subscriptions

### Endpoints
All endpoints require JWT authentication (Bearer token)

1. **GET /api/subscription-plans**
   - Returns list of available plans from the configured product family
   - No user-specific data needed

2. **POST /api/subscriptions**
   - Request body: `{ "planHandle": "string" }`
   - Creates customer (if needed) and subscription
   - Returns subscription details with ID, state, price, next billing date

3. **GET /api/my-subscriptions**
   - Returns all subscriptions for the authenticated user
   - Includes state, product info, billing dates, balance

### Configuration
Maxio settings loaded from (in priority order):
1. User secrets (development)
2. Environment variables
3. appsettings.json defaults
4. appsettings.Development.json overrides

## Production Considerations

For production deployment:

1. **Secrets Management**: Use Azure Key Vault or similar for credentials
2. **Database**: Switch from in-memory to SQL Server or similar
3. **Error Handling**: Add comprehensive logging and error reporting
4. **Monitoring**: Set up alerts for subscription creation failures
5. **Rate Limiting**: Consider adding rate limiting to subscription endpoints
6. **Validation**: Add input validation for plan handles
7. **Audit Logging**: Log all subscription changes for compliance
8. **Webhook Handling**: Implement Maxio webhooks for subscription events

## Support

For issues with Maxio API, refer to the OpenAPI specification in `maxio-spec/openapi.yaml`.

The implementation uses the specification as the authoritative contract for all API interactions.
