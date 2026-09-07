# Maxio Subscription Billing Integration - Verification Guide

This guide provides step-by-step instructions to verify that the Maxio subscription billing integration is fully functional in eShopOnWeb.

## Prerequisites

### Environment Setup

1. **SDK/Runtime Configuration:**
   - Set environment variable: `DOTNET_ROLL_FORWARD=Major` to allow .NET 10 SDK to run .NET 8 projects
   - Ensure .NET 10 SDK is installed and ASP.NET Core 8.0 runtime is available

2. **Database Configuration:**
   - Use in-memory database (recommended for testing):
     ```bash
     set UseOnlyInMemoryDatabase=true
     ```
   - Alternatively, set up SQL Server or use `(localdb)` if available

3. **Maxio Credentials:**
   Load sandbox credentials into user-secrets:
   ```bash
   dotnet user-secrets set "MAXIO_API_KEY" "your-api-key-here" --project src/PublicApi
   dotnet user-secrets set "MAXIO_SITE_SUBDOMAIN" "cp-exp-2" --project src/PublicApi
   dotnet user-secrets set "MAXIO_DEFAULT_PRODUCT_FAMILY" "eshop-subscribe" --project src/PublicApi
   ```

4. **Development Certificate:**
   Ensure HTTPS dev certificate is trusted:
   ```bash
   dotnet dev-certs https --check
   dotnet dev-certs https --trust  # if needed
   ```

## Building the Solution

```bash
# From the repository root
dotnet build Everything.sln
```

Expected result: Build should succeed with only package vulnerability warnings (no compilation errors).

## Running the Services

### Start PublicApi Service

```bash
cd src/PublicApi
set DOTNET_ROLL_FORWARD=Major
set UseOnlyInMemoryDatabase=true
dotnet run
```

Expected: Service starts on `https://localhost:28763` (or assigned port block)

## Verification Steps

### Step 1: Authenticate and Get JWT Token

First, get a JWT token by authenticating:

```bash
# Using Windows PowerShell
$headers = @{
    "Content-Type" = "application/json"
}
$body = @{
    "username" = "demouser@microsoft.com"
    "password" = "Pass@word1"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:28763/api/authenticate" `
    -Method POST `
    -Headers $headers `
    -Body $body `
    -SkipCertificateCheck

$token = ($response.Content | ConvertFrom-Json).token
Write-Host "Token: $token"
```

Or using curl:
```bash
curl -X POST https://localhost:28763/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  -k
```

### Step 2: List Available Subscription Plans

```bash
# PowerShell
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$response = Invoke-WebRequest -Uri "https://localhost:28763/api/subscription-plans" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 5)
```

Or using curl:
```bash
curl -X GET https://localhost:28763/api/subscription-plans \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -k
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "...",
      "price": 29.00,
      "billingCycle": "monthly"
    },
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "...",
      "price": 299.00,
      "billingCycle": "monthly"
    }
  ]
}
```

### Step 3: Create a Subscription

Subscribe the authenticated user to a plan:

```bash
# PowerShell
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}
$body = @{
    "productHandle" = "eshop-pro"
} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:28763/api/subscriptions" `
    -Method POST `
    -Headers $headers `
    -Body $body `
    -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json)
```

Or using curl:
```bash
curl -X POST https://localhost:28763/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  -k
```

**Expected Response:**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "pricePerMonth": 299.00,
  "nextBillingDate": "2026-10-07T00:00:00Z",
  "message": "Subscription created successfully"
}
```

**Key Behaviors:**
- First subscription creation for a user will also create a Maxio customer (idempotent)
- Subsequent subscriptions for the same user will reuse the existing Maxio customer
- Subscriptions start in "active" state immediately (no trial, no payment required)
- Next billing date is set to 1 month from creation

### Step 4: List User's Subscriptions

Retrieve all subscriptions for the authenticated user:

```bash
# PowerShell
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$response = Invoke-WebRequest -Uri "https://localhost:28763/api/my-subscriptions" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 5)
```

Or using curl:
```bash
curl -X GET https://localhost:28763/api/my-subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -k
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "productName": "Pro Plan",
      "pricePerMonth": 299.00,
      "state": "active",
      "nextBillingDate": "2026-10-07T00:00:00Z",
      "createdAt": "2026-09-07T12:34:56Z"
    }
  ]
}
```

## Test Scenarios

### Scenario 1: New User Subscription Creation
1. Authenticate as a new user
2. Call POST /api/subscriptions with "eshop-pro" handle
3. Verify: 
   - Response contains subscriptionId and "active" state
   - Maxio customer is created (visible in Maxio dashboard)
   - Subscription is linked to that customer

### Scenario 2: Idempotent Customer Creation
1. Subscribe user to "eshop-pro"
2. Subscribe same user to "basic-plan"
3. Verify:
   - Both subscriptions exist
   - Only one Maxio customer was created (same customer_reference)
   - Each subscription has its own Maxio subscription_id

### Scenario 3: Unauthenticated Access
1. Call any subscription endpoint without Authorization header
2. Verify: 401 Unauthorized response

### Scenario 4: Invalid Product Handle
1. Authenticate as user
2. Call POST /api/subscriptions with non-existent handle (e.g., "fake-plan")
3. Verify: 400 Bad Request with appropriate error message

### Scenario 5: List Subscriptions (Empty)
1. Authenticate as brand-new user (no prior subscriptions)
2. Call GET /api/my-subscriptions
3. Verify: Empty subscriptions array returned

## Maxio Sandbox Verification

After testing, verify data in Maxio sandbox dashboard (`cp-exp-2.chargify.com`):

1. **Customers Section:**
   - Look for customers with reference matching eShopOnWeb user IDs
   - Verify email matches user email from JWT token

2. **Subscriptions Section:**
   - Find subscriptions for the created customers
   - Verify product matches requested plan handle
   - Verify subscription state is "Active"

3. **Dashboard/Revenue:**
   - New MRR should reflect created subscriptions
   - Example: One "Pro Plan" at $299/month = +$299 MRR

## Troubleshooting

### Issue: "Maxio credentials not configured"
- **Solution:** Ensure environment variables are set:
  ```bash
  echo %MAXIO_API_KEY%
  echo %MAXIO_SITE_SUBDOMAIN%
  echo %MAXIO_DEFAULT_PRODUCT_FAMILY%
  ```

### Issue: 401 Unauthorized on subscription endpoints
- **Solution:** Verify JWT token is valid and has not expired
- **Check:** Token should be passed in Authorization header as `Bearer <token>`

### Issue: 400 Bad Request on subscription creation
- **Solution:** Check error message in response
- **Verify:** Product handle is correct (e.g., "eshop-pro" not "pro-plan")

### Issue: Connection refused to Maxio
- **Solution:** Verify Maxio API credentials and subnet accessibility
- **Check:** If behind corporate firewall, may need proxy configuration

### Issue: In-memory database loses data between runs
- **Expected:** This is normal. Use SQL Server for persistent testing:
  ```bash
  set UseOnlyInMemoryDatabase=false
  ```

## Code Location Reference

- **Maxio Configuration:** `src/PublicApi/Maxio/MaxioSettings.cs`
- **Maxio API Client:** `src/PublicApi/Maxio/MaxioApiClient.cs`
- **Subscription Endpoints:**
  - `src/PublicApi/SubscriptionEndpoints/ListSubscriptionPlansEndpoint.cs`
  - `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
  - `src/PublicApi/SubscriptionEndpoints/ListMySubscriptionsEndpoint.cs`
- **Configuration:** `src/PublicApi/appsettings.json`
- **Dependency Registration:** `src/PublicApi/Program.cs`

## Success Criteria

The integration is fully functional when:

1. ✅ All three endpoints are accessible and respond without errors
2. ✅ Plans are retrieved from Maxio correctly
3. ✅ Subscriptions can be created for authenticated users
4. ✅ Customer creation is idempotent (no duplicate customers)
5. ✅ User subscriptions can be listed with correct state and pricing
6. ✅ All created data is visible in Maxio sandbox dashboard
7. ✅ Unauthenticated requests are rejected with 401
8. ✅ Invalid product handles are rejected with 400

When all criteria are met, the Maxio subscription billing integration is ready for further development and testing.
