# Maxio Subscription Integration Verification Guide

This guide walks through verifying the Maxio Advanced Billing subscription integration for eShopOnWeb.

## Prerequisites

1. **Environment Variables Set**: The following Maxio sandbox credentials are already configured:
   - `MAXIO_API_KEY`
   - `MAXIO_SITE_SUBDOMAIN` (should be `cp-exp-3`)
   - `MAXIO_DEFAULT_PRODUCT_FAMILY` (should be `eshop-subscribe`)
   - `MAXIO_ENVIRONMENT` (should be `US`)

2. **User Secrets Configured**: Maxio configuration is stored in .NET user-secrets for the PublicApi project:
   - `Maxio:ApiKey`
   - `Maxio:Subdomain`
   - `Maxio:ProductFamilyHandle`

3. **Solution Built**: The full solution compiles without errors.

## Running the Integration Tests

### Step 1: Start the PublicApi Service

```bash
cd src/PublicApi

# Set environment for in-memory database and proper .NET rollforward
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"

# Run with development settings
dotnet run --configuration Debug
```

The API will start on `https://localhost:28783` (or the port specified in launchSettings).

### Step 2: Obtain an Authentication Token

The PublicApi uses JWT authentication. First, authenticate:

```bash
# Authenticate and get a token
$response = Invoke-WebRequest -Uri "https://localhost:28783/api/authenticate" `
  -Method POST `
  -ContentType "application/json" `
  -SkipCertificateCheck `
  -Body (@{
      username = "test@example.com"
      password = "P@ssw0rd!1"
  } | ConvertTo-Json)

$token = ($response.Content | ConvertFrom-Json).token
Write-Host "Token: $token"
```

Or use curl:

```bash
curl -X POST https://localhost:28783/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"test@example.com","password":"P@ssw0rd!1"}' \
  -k
```

### Step 3: Test the Subscription Endpoints

#### 3a. List Available Plans

```bash
# Using curl
curl -X GET https://localhost:28783/api/subscription-plans \
  -H "Authorization: Bearer $token" \
  -k | jq .

# Using PowerShell
$headers = @{
    "Authorization" = "Bearer $token"
}
$response = Invoke-WebRequest -Uri "https://localhost:28783/api/subscription-plans" `
  -Headers $headers `
  -SkipCertificateCheck
$response.Content | ConvertFrom-Json | Format-List
```

**Expected Response:**
- Array of subscription plans from Maxio sandbox (e.g., `eshop-pro` at $299/mo, `basic-plan` at $29/mo)
- Each plan includes: `planId`, `planName`, `planHandle`, `priceInCents`, `priceFormatted`, `billingPeriod`

#### 3b. Create a Subscription

```bash
# Using curl
curl -X POST https://localhost:28783/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k | jq .

# Using PowerShell
$headers = @{
    "Authorization" = "Bearer $token"
}
$body = @{
    planHandle = "eshop-pro"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:28783/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $body `
  -SkipCertificateCheck
$response.Content | ConvertFrom-Json | Format-List
```

**Expected Response:**
- HTTP 201 Created
- Subscription object with: `subscriptionId`, `state` (should be "active"), `priceInCents`, `nextBillingDate`, `activeSince`

#### 3c. Get User's Subscriptions

```bash
# Using curl
curl -X GET https://localhost:28783/api/my-subscriptions \
  -H "Authorization: Bearer $token" \
  -k | jq .

# Using PowerShell
$headers = @{
    "Authorization" = "Bearer $token"
}
$response = Invoke-WebRequest -Uri "https://localhost:28783/api/my-subscriptions" `
  -Headers $headers `
  -SkipCertificateCheck
$response.Content | ConvertFrom-Json | Format-List
```

**Expected Response:**
- HTTP 200 OK
- Object with `userId` and array of `subscriptions`
- Should include the subscription created in step 3b

## Key Integration Points

### 1. Maxio Client Configuration
- **File**: `src/PublicApi/Services/MaxioSubscriptionService.cs`
- **Pattern**: Singleton client initialized with Basic auth (username = API key, password = "x")
- **Server**: US environment pointing to `{subdomain}.chargify.com`

### 2. Operations Implemented
- **ListProducts**: Retrieves available subscription plans (filtered by product family)
- **CreateCustomer** (idempotent): Creates Maxio customer linked to eShopOnWeb user by reference
- **CreateSubscription**: Enrolls customer in a plan
- **ListCustomerSubscriptions**: Retrieves all active/past subscriptions for a customer
- **ReadCustomerByReference**: Lookup existing customer by user ID (for idempotency)

### 3. Error Handling
- **Case A Errors** (typed): `CreateCustomer`, `CreateSubscription` → `TryGet*` accessors for specific error details
- **Case B Errors** (generic `RawError`): `ListProducts`, `ReadCustomerByReference`, `ListCustomerSubscriptions` → direct `StatusCode` and `ReadAsString()`
- All operations throw `SdkException<TError>` on failure (no Result variants used)

### 4. Customer Idempotency
- User ID is passed as `Reference` field in Maxio customer
- On re-subscribe, customer is looked up by reference (`ReadCustomerByReference`)
- Prevents duplicate customer creation even if the endpoint is called twice

### 5. Authentication & Authorization
- All endpoints require JWT bearer token (`[Authorize]` attribute)
- User identity extracted from JWT claims: `sub` (user ID) and `email`
- Maxio customer reference is the eShopOnWeb user ID

## Troubleshooting

### Issue: "Maxio API key and subdomain must be configured"
**Solution**: Verify user-secrets are set:
```bash
cd src/PublicApi
dotnet user-secrets list
```

### Issue: HTTP 404 on subscription endpoints
**Solution**: Verify the PublicApi is running and the endpoints are registered. Check that `MapEndpoints()` is called in Program.cs.

### Issue: HTTP 401 Unauthorized
**Solution**: Ensure JWT token is included in the `Authorization: Bearer {token}` header. Get a fresh token from `/api/authenticate`.

### Issue: "Unable to find package AsadAli.AdvancedBilling.Sdk with version"
**Solution**: The correct version is 1.0.2 (not 3.0.0). This should be specified in `Directory.Packages.props`.

### Issue: SDK operation throws "method not found"
**Note**: The Maxio SDK does NOT use "Async" suffix on method names (e.g., `ListProducts()` not `ListProductsAsync()`), even though all methods return `Task<T>`.

## What to Verify

1. ✅ All three endpoints are reachable and return expected responses
2. ✅ Creating a subscription creates a Maxio customer with the user's email and reference
3. ✅ Re-subscribing the same user does not create a duplicate customer
4. ✅ Subscription state is "active" after creation
5. ✅ Next billing date is correctly set (one month from activation for monthly plans)
6. ✅ Error handling works: invalid plan handle → 400; missing auth → 401; server error → 5xx
7. ✅ Endpoints reject unauthenticated requests

## Architecture Notes

- **Service Layer**: `MaxioSubscriptionService` encapsulates all Maxio SDK interactions
- **Endpoint Layer**: Three Ardalis.ApiEndpoints classes expose HTTP handlers
- **DI Registration**: Service registered as scoped in Program.cs; uses injected `IHttpClientFactory`
- **Resilience**: HTTP client configured with standard timeout (100s per attempt), retries on idempotent verbs only
- **Logging**: Integration logs Maxio API errors (status, messages) via `ILogger<MaxioSubscriptionService>`
- **Data Model**: In-memory database only (set `UseOnlyInMemoryDatabase=true`); no persistent subscription state stored in eShopOnWeb DB

## Next Steps for Production

1. **Persistent Storage**: Extend the data model to store Maxio customer ID and subscription ID in eShopOnWeb user record
2. **Webhook Handling**: Listen for Maxio subscription lifecycle events (activated, canceled, expired)
3. **Billing History**: Store and display invoices and billing events
4. **Cancellation**: Implement subscription cancellation endpoint
5. **Plan Changes**: Implement subscription upgrade/downgrade
6. **Testing**: Add integration tests with live Maxio sandbox; add unit tests with mocked Maxio SDK
