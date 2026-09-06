# Verifying Maxio Subscription Billing Integration

Follow these steps to verify the integration is working end-to-end.

## Step 1: Configure Maxio Credentials

Set environment variables with your Maxio sandbox credentials:

```powershell
# PowerShell example
$env:MAXIO_API_KEY = "your_maxio_api_key"
$env:MAXIO_SITE_SUBDOMAIN = "your_sandbox_subdomain"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"

# Example:
# $env:MAXIO_API_KEY = "abc123def456"
# $env:MAXIO_SITE_SUBDOMAIN = "cp-exp-3"
# $env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

## Step 2: Build the Solution

Verify the build succeeds:

```powershell
cd C:\path\to\repo
dotnet build eShopOnWeb.sln -c Debug
```

Expected: Build succeeds with no errors.

## Step 3: Start the PublicApi

```powershell
cd src/PublicApi
$env:UseOnlyInMemoryDatabase = "true"
dotnet run
```

Expected output should include:
```
PublicApi App created...
Seeding Database...
LAUNCHING PublicApi
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:25263
```

Keep this running for the next steps.

## Step 4: Authenticate (in a new terminal)

Get a JWT token:

```powershell
$headers = @{"Content-Type" = "application/json"}
$body = @{"username" = "demouser@microsoft.com"; "password" = "Pass@word1"} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:25263/api/authenticate" `
  -Method POST `
  -Headers $headers `
  -Body $body `
  -SkipCertificateCheck

$token = ($response.Content | ConvertFrom-Json).token
Write-Host "Token: $token"
```

Expected: You receive a JWT token in the response. Copy the token for the next steps.

## Step 5: List Subscription Plans

Verify Maxio connection and plans are fetched:

```powershell
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$response = Invoke-WebRequest -Uri "https://localhost:25263/api/subscription-plans" `
  -Method GET `
  -Headers $headers `
  -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json)
```

Expected response (plans available from Maxio):
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "$299 Pro Plan",
      "handle": "eshop-pro",
      "description": "...",
      "pricePerMonth": 299.0,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "$29 Basic Plan",
      "handle": "basic-plan",
      "description": "...",
      "pricePerMonth": 29.0,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

Troubleshooting:
- If empty array: Check Maxio credentials and that plans exist in the sandbox site
- If 401/403: API key or subdomain is incorrect
- If 500: Check PublicApi console for errors

## Step 6: Create a Subscription

Subscribe to the Pro plan:

```powershell
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$body = @{"planHandle" = "eshop-pro"} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:25263/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -Body $body `
  -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json)
```

Expected response (201 Created):
```json
{
  "subscription": {
    "id": 12345678,
    "state": "active",
    "customerId": 98765432,
    "activatedAt": "2026-09-06T12:34:56Z",
    "canceledAt": null,
    "currentPeriodStartsAt": "2026-09-06T12:34:56Z",
    "currentPeriodEndsAt": "2026-10-06T12:34:56Z",
    "nextAssessmentAt": "2026-10-06T12:34:56Z",
    "productPricePerMonth": 299.0,
    "productName": "$299 Pro Plan",
    "productHandle": "eshop-pro"
  }
}
```

Troubleshooting:
- If customer creation fails: Check Maxio API docs for customer creation requirements
- If 422 (validation error): Plan handle may not exist; verify with step 5
- If 500: Check PublicApi logs for Maxio API errors

## Step 7: Get User Subscriptions

Retrieve all subscriptions for the logged-in user:

```powershell
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$response = Invoke-WebRequest -Uri "https://localhost:25263/api/my-subscriptions" `
  -Method GET `
  -Headers $headers `
  -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json)
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "customerId": 98765432,
      ...
    }
  ]
}
```

If you created a subscription in step 6, it should appear here.

## Step 8: Test Idempotency

Create another subscription for the same user with the same plan:

```powershell
# Repeat Step 6 with the same token and planHandle
$response = Invoke-WebRequest -Uri "https://localhost:25263/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -Body (@{"planHandle" = "eshop-pro"} | ConvertTo-Json) `
  -SkipCertificateCheck
```

Expected:
- Should succeed and create a NEW subscription (Maxio allows multiple subscriptions per customer)
- Customer ID should be the same as before (proving customer mapping was reused)
- New subscription ID and dates should be different

## Step 9: Try a Different Plan

Subscribe to the Basic plan:

```powershell
$body = @{"planHandle" = "basic-plan"} | ConvertTo-Json

$response = Invoke-WebRequest -Uri "https://localhost:25263/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -Body $body `
  -SkipCertificateCheck

Write-Host ($response.Content | ConvertFrom-Json | ConvertTo-Json)
```

Expected:
- Same customerId as before
- Different subscription ID
- Price is $29.00 (basic-plan price)

## Step 10: Verify Data Persistence

Get subscriptions again:

```powershell
$response = Invoke-WebRequest -Uri "https://localhost:25263/api/my-subscriptions" `
  -Method GET `
  -Headers $headers `
  -SkipCertificateCheck

$subs = ($response.Content | ConvertFrom-Json).subscriptions
Write-Host "Number of subscriptions: $($subs.Count)"
$subs | ForEach-Object { Write-Host "- $($_.productHandle): $($_.productPricePerMonth)/mo (State: $($_.state))" }
```

Expected: Should list all subscriptions created in steps 6, 8, and 9.

## Integration Summary

If all steps complete successfully, the integration is working:

✅ **Credentials & Auth**: API can authenticate to Maxio
✅ **Plans Fetched**: Product list retrieved from Maxio
✅ **Customer Creation**: Maxio customers created automatically
✅ **Subscription Lifecycle**: Subscriptions created with correct details
✅ **Idempotency**: User-to-customer mappings reused correctly
✅ **Data Retrieval**: Subscriptions retrieved for the user

## Common Issues and Solutions

### "Unable to reach Maxio API" / Timeout
- Check internet connection
- Verify subdomain is correct (format: `name.chargify.com`)
- Verify API key is valid
- Check firewall/proxy settings

### "Unauthorized" (401/403)
- API key is incorrect
- API key is not enabled for your Maxio site
- Check Maxio Settings → API Keys

### "Plan not found" (422)
- Plan handle must match exactly (case-sensitive)
- Plan must exist in the product family
- Verify handles in Maxio: Products → {Family} → Plan → Handle field

### "Customer creation failed"
- Email format invalid
- Reference (user ID) already exists for another customer
- Required customer fields missing
- Check Maxio API docs for customer creation validation rules

### Empty subscriptions list
- Try authenticating with a different user email if you created test users
- Check Maxio site directly to verify subscriptions were created
- User may genuinely have no subscriptions yet

### In-Memory Database Data Lost
- Normal behavior: in-memory DB resets on app restart
- Use SQL Server LocalDB or a real database for persistent testing
- Or accept losing user mappings (customers recreated on new app start)

## Testing with Curl (Alternative)

If PowerShell isn't convenient:

```bash
# Get token
TOKEN=$(curl -s -k -X POST https://localhost:25263/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | jq -r '.token')

# List plans
curl -s -k -H "Authorization: Bearer $TOKEN" \
  https://localhost:25263/api/subscription-plans | jq

# Create subscription
curl -s -k -X POST https://localhost:25263/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' | jq

# Get user subscriptions
curl -s -k -H "Authorization: Bearer $TOKEN" \
  https://localhost:25263/api/my-subscriptions | jq
```

## Next Steps

Once verified:
1. Deploy to staging/production
2. Set credentials via environment variables or secrets management
3. Monitor API logs for errors
4. Set up webhook handling for subscription events (cancel, renewal, etc.)
