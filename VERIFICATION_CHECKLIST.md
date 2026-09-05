# Maxio Integration Verification Checklist

Follow these steps to verify the subscription integration is working correctly.

## Pre-Requisites

- [ ] .NET 8.0 SDK or .NET 10 SDK installed
- [ ] ASP.NET Core 8.0 runtime installed (or use rollForward: Major)
- [ ] Maxio Advanced Billing sandbox account with API key
- [ ] Git clone or access to eShopOnWeb repository

## Step 1: Build Verification

```bash
cd /path/to/eShopOnWeb
dotnet build -c Release
```

**Expected Result:** ✅ Build succeeds with 0 errors (some warnings are OK)

**If it fails:**
- Check .NET SDK version: `dotnet --version`
- Check all projects restore: `dotnet restore`
- Review error messages in output

## Step 2: Configuration Setup

Navigate to PublicApi directory:
```bash
cd src/PublicApi
```

### Option A: Using .NET User Secrets (Recommended for Development)

```bash
# These should already be configured from setup, verify:
dotnet user-secrets list
```

**Expected Output:**
```
Maxio:ApiKey = test-api-key-placeholder
Maxio:Subdomain = cp-exp-3
Maxio:ProductFamilyHandle = eshop-subscribe
```

**Update with real credentials:**
```bash
# Replace with your actual Maxio credentials
dotnet user-secrets set "Maxio:ApiKey" "YOUR_ACTUAL_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "YOUR_PRODUCT_FAMILY"
```

### Option B: Using Environment Variables

```cmd
REM Windows Command Prompt
set MAXIO_API_KEY=YOUR_ACTUAL_API_KEY
set MAXIO_SITE_SUBDOMAIN=YOUR_SUBDOMAIN
set MAXIO_DEFAULT_PRODUCT_FAMILY=YOUR_PRODUCT_FAMILY
set UseOnlyInMemoryDatabase=true
```

Or PowerShell:
```powershell
$env:MAXIO_API_KEY="YOUR_ACTUAL_API_KEY"
$env:MAXIO_SITE_SUBDOMAIN="YOUR_SUBDOMAIN"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="YOUR_PRODUCT_FAMILY"
$env:UseOnlyInMemoryDatabase="true"
```

### Option C: Modify appsettings.json (Development Only)

Edit `src/PublicApi/appsettings.json`:
```json
{
  "Maxio": {
    "ApiKey": "YOUR_ACTUAL_API_KEY",
    "Subdomain": "YOUR_SUBDOMAIN",
    "ProductFamilyHandle": "YOUR_PRODUCT_FAMILY"
  }
}
```

⚠️ **WARNING**: Do NOT commit this file with real credentials!

## Step 3: Start the API Server

From `src/PublicApi` directory:

```bash
dotnet run
```

**Expected Output:**
```
info: Microsoft.eShopWeb.PublicApi.Program[0]
      LAUNCHING PublicApi
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:24703
```

**If it fails to start:**
- Check port 24703 is not in use: `netstat -ano | findstr :24703`
- Check certificate is trusted: `dotnet dev-certs https --check`
- If not trusted: `dotnet dev-certs https --trust`

**Leave this terminal running** and open a new terminal for the next steps.

## Step 4: Get Authentication Token

Open a new terminal or PowerShell window and run:

```powershell
# Disable certificate verification for self-signed dev cert
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

$response = Invoke-RestMethod -Uri "https://localhost:24703/api/authenticate" `
    -Method Post `
    -ContentType "application/json" `
    -Body '{"username":"demouser@microsoft.com","password":"Pass@word1"}'

$token = $response.token
Write-Host "Token: $token"
```

**Expected Result:** Token is returned (a long base64-encoded string)

**Save the token** for use in subsequent tests.

## Step 5: Test List Subscription Plans

```powershell
$token = "YOUR_TOKEN_FROM_STEP_4"
$headers = @{"Authorization" = "Bearer $token"; "Accept" = "application/json"}

$response = Invoke-RestMethod -Uri "https://localhost:24703/api/subscription-plans" `
    -Method Get `
    -Headers $headers

$response | ConvertTo-Json -Depth 10
```

**Expected Result:**
- HTTP 200 OK
- Response contains `plans` array
- Each plan has: `id`, `name`, `handle`, `description`, `price`, `interval`, `intervalUnit`
- At least one plan is returned from your configured product family

**If you get an error:**
- Check product family handle is correct
- Verify Maxio API key has read permissions
- Check Maxio site is accessible

## Step 6: Test Create Subscription

```powershell
$token = "YOUR_TOKEN_FROM_STEP_4"
$headers = @{"Authorization" = "Bearer $token"; "Content-Type" = "application/json"}

$body = '{"productHandle":"eshop-pro"}' # Use a handle from Step 5 results

$response = Invoke-RestMethod -Uri "https://localhost:24703/api/subscriptions" `
    -Method Post `
    -Headers $headers `
    -Body $body

$response | ConvertTo-Json
```

**Expected Result:**
- HTTP 201 Created
- Response contains subscription details:
  - `subscriptionId` (integer, e.g., 12345)
  - `state` (should be "active" or "trialing")
  - `productName` (name of the plan)
  - `monthlyPrice` (in dollars, e.g., 299.00)
  - `nextBillingDate` (ISO 8601 datetime)

**If you get an error:**
- Check product handle exists in your family
- Verify customer can be created (unique email check)
- Check payment method is compatible with product

## Step 7: Test List User Subscriptions

```powershell
$token = "YOUR_TOKEN_FROM_STEP_4"
$headers = @{"Authorization" = "Bearer $token"; "Accept" = "application/json"}

$response = Invoke-RestMethod -Uri "https://localhost:24703/api/my-subscriptions" `
    -Method Get `
    -Headers $headers

$response | ConvertTo-Json -Depth 10
```

**Expected Result:**
- HTTP 200 OK
- Response contains `subscriptions` array
- The subscription created in Step 6 is in the list
- Each subscription shows: `id`, `state`, `productName`, `monthlyPrice`, `nextBillingDate`

## Step 8: Test Authorization Enforcement

```powershell
# Request WITHOUT authentication header
$response = Invoke-RestMethod -Uri "https://localhost:24703/api/subscription-plans" `
    -Method Get -ErrorAction SilentlyContinue
```

**Expected Result:**
- HTTP 401 Unauthorized
- Request is rejected
- No subscription plans are returned

## Step 9: Test API Documentation

In a web browser, navigate to:
```
https://localhost:24703/swagger
```

**Expected Result:**
- Swagger UI loads
- Three new endpoints are visible:
  - `GET /api/subscription-plans`
  - `POST /api/subscriptions`
  - `GET /api/my-subscriptions`
- Each endpoint shows parameter requirements and response schemas

## Step 10: Verify Database Changes

Check the database to confirm user-subscription mapping:

```sql
-- If using SQL Server
SELECT Id, UserName, MaxioCustomerId FROM AspNetUsers 
WHERE MaxioCustomerId IS NOT NULL;
```

**Expected Result:**
- Users who created subscriptions have a `MaxioCustomerId` value
- The ID matches the customer ID in Maxio

## Automated Test Suite

Run the provided PowerShell test script for comprehensive validation:

```powershell
cd /path/to/eShopOnWeb
.\TEST_SUBSCRIPTION_API.ps1 -BaseUrl "https://localhost:24703"
```

This script runs all 5 verification steps automatically.

## Troubleshooting Guide

| Error | Cause | Solution |
|-------|-------|----------|
| `Failed to list products` | Product family handle wrong or Maxio key invalid | Verify Maxio credentials and family handle |
| `Failed to create subscription` | Product doesn't exist in family | Check product handle is in family and is active |
| `Failed to get or create customer` | Email already exists in Maxio | Use different email or check Maxio customer list |
| `401 Unauthorized` | No token or expired token | Get new token from authenticate endpoint |
| `Certificate validation failed` | Dev cert not trusted | Run: `dotnet dev-certs https --trust` |
| `Port 24703 already in use` | Another service using the port | Stop other process or change port in launchSettings.json |
| `Maxio API returns 401` | Invalid API key | Verify key in user-secrets or environment |

## Success Criteria

✅ All tests pass when you can:

- [ ] Build solution successfully
- [ ] Start PublicApi server without errors  
- [ ] Authenticate and receive JWT token
- [ ] List subscription plans from Maxio
- [ ] Create a new subscription
- [ ] Retrieve created subscription in user's list
- [ ] API rejects unauthenticated requests
- [ ] Swagger documentation is accessible
- [ ] Database stores MaxioCustomerId mapping

## Next Steps

Once all verifications pass:

1. **Test Different Scenarios**
   - Create multiple subscriptions for same user
   - Create subscription with different user account
   - Test with different product handles

2. **Integration Testing**
   - Integrate subscription UI into web frontend
   - Add subscription management pages
   - Connect webhooks for Maxio events

3. **Production Deployment**
   - Move credentials to Azure Key Vault / AWS Secrets Manager
   - Set up application monitoring
   - Configure backup and disaster recovery
   - Load test the subscription endpoints

## Support

For detailed information, refer to:
- `IMPLEMENTATION_SUMMARY.md` - Architecture and design decisions
- `MAXIO_INTEGRATION_GUIDE.md` - Complete setup and API documentation
- `TEST_SUBSCRIPTION_API.ps1` - Automated test script

For Maxio API details, see:
- `maxio-spec/openapi.yaml` - Maxio OpenAPI specification
- https://docs.maxio.com - Maxio documentation

## Performance Notes

- **First-time subscription creation**: ~500-1000ms (customer creation + subscription creation + database update)
- **List plans**: ~200-400ms (depends on number of plans)
- **List subscriptions**: ~300-500ms (depends on number of user subscriptions)

These times depend on:
- Network latency to Maxio API
- Database response time
- Serialization/deserialization overhead
