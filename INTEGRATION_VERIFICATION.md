# Maxio Integration Verification Guide

This guide provides step-by-step instructions to verify the Maxio subscription billing integration is working correctly.

## Prerequisites

1. Maxio sandbox credentials for site `cp-exp-2`
2. .NET SDK 8.0 or later (or .NET 10 with rollforward enabled)
3. PowerShell or a Unix shell for running test scripts
4. `curl` command-line tool

## Environment Setup

### Step 1: Set Environment Variables

**Windows PowerShell:**
```powershell
$env:MAXIO_API_KEY = "your-actual-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
```

**Windows Command Prompt:**
```cmd
set MAXIO_API_KEY=your-actual-api-key
set MAXIO_SITE_SUBDOMAIN=cp-exp-2
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
set UseOnlyInMemoryDatabase=true
```

**Linux/macOS Bash:**
```bash
export MAXIO_API_KEY="your-actual-api-key"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
```

### Step 2: Handle SDK/Runtime Mismatch

If using .NET 10 SDK with an 8.0-pinned global.json:

**Windows PowerShell:**
```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
```

**Linux/macOS Bash:**
```bash
export DOTNET_ROLL_FORWARD=Major
```

## Verification Procedure

### Step 1: Build the Solution

```bash
cd src/PublicApi
dotnet build
```

Expected output: `Build succeeded.` (or similar success message)

### Step 2: Start the API

```bash
cd src/PublicApi
dotnet run --environment Development
```

Expected output should include:
```
...
PublicApi App created...
Seeding Database...
LAUNCHING PublicApi
```

The API should be listening on `https://localhost:27543`

### Step 3: Authenticate

In a new terminal, get a JWT token:

```bash
# Windows PowerShell
$auth = curl -X POST https://localhost:27543/api/authenticate `
  -H "Content-Type: application/json" `
  -d '{"username":"demouser@microsoft.com","password":"[GH0st]*"}' `
  -w "%{json}" | ConvertFrom-Json
$token = $auth.token

# Linux/macOS
response=$(curl -s -X POST https://localhost:27543/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"[GH0st]*"}')
token=$(echo $response | jq -r '.token')
echo "Token: $token"
```

Expected: A JWT token is returned. It will be long (several hundred characters).

### Step 4: Verify Subscription Plans Endpoint

**Windows PowerShell:**
```powershell
curl -X GET https://localhost:27543/api/subscription-plans `
  -H "Authorization: Bearer $token" -k | ConvertFrom-Json | ConvertTo-Json
```

**Linux/macOS:**
```bash
curl -s -X GET https://localhost:27543/api/subscription-plans \
  -H "Authorization: Bearer $token" | jq .
```

Expected response should contain:
- A list with at least 2 plans
- Plans with handles: `eshop-pro` (Pro Plan, $299/month) and `basic-plan` (Basic Plan, $29/month)
- Fields: id, handle, name, description, pricePerMonth, billingInterval, hasTrial, trialDays

Example:
```json
{
  "correlationId": "...",
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with features",
      "pricePerMonth": 299.00,
      "billingInterval": "Every 1 month",
      "hasTrial": false,
      "trialDays": null
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic plan for getting started",
      "pricePerMonth": 29.00,
      "billingInterval": "Every 1 month",
      "hasTrial": false,
      "trialDays": null
    }
  ]
}
```

**Success Criteria:**
- Status: 200 OK
- Response contains 2 plans
- Plans have correct handles and prices

### Step 5: Verify Subscription Creation Endpoint

**Windows PowerShell:**
```powershell
$subResponse = curl -X POST https://localhost:27543/api/subscriptions `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{"planHandle":"eshop-pro"}' -k | ConvertFrom-Json
$subResponse | ConvertTo-Json
$subscriptionId = $subResponse.subscriptionId
```

**Linux/macOS:**
```bash
subResponse=$(curl -s -X POST https://localhost:27543/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}')
echo "$subResponse" | jq .
subscriptionId=$(echo "$subResponse" | jq -r '.subscriptionId')
echo "Subscription ID: $subscriptionId"
```

Expected response:
```json
{
  "correlationId": "...",
  "success": true,
  "message": "Successfully subscribed to Pro Plan",
  "subscriptionId": <number>,
  "state": "active",
  "customerMaxioId": <number>,
  "planName": "Pro Plan",
  "pricePerMonth": 299.00,
  "nextBillingAt": "2026-10-07T00:00:00",
  "errorMessage": null
}
```

**Success Criteria:**
- Status: 200 OK
- `success` is `true`
- `state` is `"active"`
- `subscriptionId` is a positive integer
- `customerMaxioId` is a positive integer
- `nextBillingAt` is a future date

### Step 6: Verify My Subscriptions Endpoint

**Windows PowerShell:**
```powershell
curl -X GET https://localhost:27543/api/my-subscriptions `
  -H "Authorization: Bearer $token" -k | ConvertFrom-Json | ConvertTo-Json
```

**Linux/macOS:**
```bash
curl -s -X GET https://localhost:27543/api/my-subscriptions \
  -H "Authorization: Bearer $token" | jq .
```

Expected response:
```json
{
  "correlationId": "...",
  "success": true,
  "message": "Found 1 subscription(s)",
  "subscriptions": [
    {
      "subscriptionId": <same-as-step-5>,
      "state": "active",
      "productHandle": "eshop-pro",
      "nextBillingAt": "2026-10-07T00:00:00",
      "mrrPerMonth": 299.00,
      "createdAt": "2026-09-07T...",
      "updatedAt": "2026-09-07T..."
    }
  ],
  "errorMessage": null
}
```

**Success Criteria:**
- Status: 200 OK
- `success` is `true`
- `subscriptions` array contains at least 1 subscription
- The subscription matches what was created in Step 5

### Step 7: Verify Idempotency (Optional)

Create another subscription with the same plan for the same user:

**Windows PowerShell:**
```powershell
$sub2 = curl -X POST https://localhost:27543/api/subscriptions `
  -H "Authorization: Bearer $token" `
  -H "Content-Type: application/json" `
  -d '{"planHandle":"basic-plan"}' -k | ConvertFrom-Json
$sub2 | ConvertTo-Json
```

**Linux/macOS:**
```bash
curl -s -X POST https://localhost:27543/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"basic-plan"}' | jq .
```

Expected: A new subscription is created with the Basic Plan ($29/month).

Then run the my-subscriptions endpoint again to verify both subscriptions appear.

## Automated Test Script

**For PowerShell (Windows):**

Save the following as `test-maxio-integration.ps1`:

```powershell
param(
    [string]$ApiUrl = "https://localhost:27543",
    [string]$Username = "demouser@microsoft.com",
    [string]$Password = "[GH0st]*"
)

Write-Host "=== Maxio Integration Test ===" -ForegroundColor Cyan

# Suppress certificate warnings for self-signed dev cert
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
$PSDefaultParameterValues = @{
    "Invoke-WebRequest:SkipCertificateCheck" = $true
}

# Step 1: Authenticate
Write-Host "`n[1/4] Authenticating..." -ForegroundColor Yellow
try {
    $authResponse = Invoke-WebRequest -Uri "$ApiUrl/api/authenticate" `
        -Method POST `
        -ContentType "application/json" `
        -Body (@{username=$Username; password=$Password} | ConvertTo-Json) `
        -SkipCertificateCheck -ErrorAction Stop
    
    $authData = $authResponse.Content | ConvertFrom-Json
    $token = $authData.token
    Write-Host "✓ Authentication successful" -ForegroundColor Green
} catch {
    Write-Host "✗ Authentication failed: $_" -ForegroundColor Red
    exit 1
}

# Step 2: List Plans
Write-Host "`n[2/4] Listing subscription plans..." -ForegroundColor Yellow
try {
    $plansResponse = Invoke-WebRequest -Uri "$ApiUrl/api/subscription-plans" `
        -Method GET `
        -Headers @{"Authorization" = "Bearer $token"} `
        -SkipCertificateCheck -ErrorAction Stop
    
    $plansData = $plansResponse.Content | ConvertFrom-Json
    Write-Host "✓ Found $($plansData.plans.Count) plans" -ForegroundColor Green
    $plansData.plans | ForEach-Object {
        Write-Host "  - $($_.name) ($($_.handle)): `$$($_.pricePerMonth)/month"
    }
} catch {
    Write-Host "✗ List plans failed: $_" -ForegroundColor Red
    exit 1
}

# Step 3: Subscribe to Plan
Write-Host "`n[3/4] Creating subscription..." -ForegroundColor Yellow
try {
    $subResponse = Invoke-WebRequest -Uri "$ApiUrl/api/subscriptions" `
        -Method POST `
        -ContentType "application/json" `
        -Headers @{"Authorization" = "Bearer $token"} `
        -Body (@{planHandle = "eshop-pro"} | ConvertTo-Json) `
        -SkipCertificateCheck -ErrorAction Stop
    
    $subData = $subResponse.Content | ConvertFrom-Json
    if ($subData.success) {
        Write-Host "✓ Subscription created: ID=$($subData.subscriptionId), State=$($subData.state)" -ForegroundColor Green
        Write-Host "  Plan: $($subData.planName), Next billing: $($subData.nextBillingAt)"
    } else {
        Write-Host "✗ Subscription failed: $($subData.errorMessage)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Create subscription failed: $_" -ForegroundColor Red
    exit 1
}

# Step 4: Get My Subscriptions
Write-Host "`n[4/4] Retrieving my subscriptions..." -ForegroundColor Yellow
try {
    $mySubsResponse = Invoke-WebRequest -Uri "$ApiUrl/api/my-subscriptions" `
        -Method GET `
        -Headers @{"Authorization" = "Bearer $token"} `
        -SkipCertificateCheck -ErrorAction Stop
    
    $mySubsData = $mySubsResponse.Content | ConvertFrom-Json
    if ($mySubsData.success) {
        Write-Host "✓ Found $($mySubsData.subscriptions.Count) subscription(s)" -ForegroundColor Green
        $mySubsData.subscriptions | ForEach-Object {
            Write-Host "  - $($_.productHandle): State=$($_.state), MRR=`$$($_.mrrPerMonth)/month"
        }
    } else {
        Write-Host "✗ Get subscriptions failed: $($mySubsData.errorMessage)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "✗ Get subscriptions failed: $_" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== All Tests Passed ✓ ===" -ForegroundColor Green
```

Run it with:
```powershell
.\test-maxio-integration.ps1
```

## Common Issues and Solutions

### Error: "Failed to create or retrieve customer"
- Verify MAXIO_API_KEY is correct
- Verify MAXIO_SITE_SUBDOMAIN is "cp-exp-2"
- Check that your IP has API access on the Maxio site

### Error: "Plan not found"
- Verify plan handles are correct: "eshop-pro" and "basic-plan"
- Verify they exist in Maxio site "cp-exp-2"
- Check the MAXIO_DEFAULT_PRODUCT_FAMILY setting

### Error: "Failed to create subscription"
- Check the customer was created successfully (previous step)
- Verify the plan exists in the product family
- Ensure payment method requirement is disabled on the plans

### 401 Unauthorized
- Token may have expired; re-authenticate
- Verify token is passed in Authorization header with "Bearer" prefix
- Ensure the token is from a successful authentication call

### HTTPS Certificate Errors
```bash
dotnet dev-certs https --check
dotnet dev-certs https --trust
```

## Performance Notes

- Initial calls may take a moment as they establish connections to Maxio
- Customer creation is fast (usually under 100ms)
- Subscription creation may take 1-2 seconds due to Maxio processing
- Subsequent calls with existing customers are typically under 100ms

## Production Checklist

Before deploying to production:

- [ ] Replace sandbox credentials with production Maxio API key
- [ ] Change MAXIO_SITE_SUBDOMAIN to your production site
- [ ] Enable SQL Server database (don't use in-memory)
- [ ] Set up proper secret management (Azure Key Vault, etc.)
- [ ] Implement error logging and monitoring
- [ ] Set up webhook handlers for subscription events
- [ ] Add rate limiting to prevent abuse
- [ ] Implement subscription status synchronization
- [ ] Test with real payment methods
- [ ] Document customer support procedures
