# Maxio Subscription Billing Integration - Testing Guide

This guide walks you through testing the Maxio subscription billing integration with eShopOnWeb.

## Prerequisites

- .NET 8 SDK installed (or .NET 10 SDK with DOTNET_ROLL_FORWARD=Major)
- Maxio sandbox credentials (API Key, Subdomain)
- curl or Postman for API testing
- The application is running on `https://localhost:25083`

## Setup Steps

### 1. Configure Maxio Credentials

Set your Maxio API credentials as environment variables before running the application:

**Windows PowerShell:**
```powershell
$env:MAXIO_API_KEY = "your-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

**Windows Command Prompt:**
```cmd
set MAXIO_API_KEY=your-api-key
set MAXIO_SITE_SUBDOMAIN=cp-exp-2
set MAXIO_ENVIRONMENT=sandbox
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
set UseOnlyInMemoryDatabase=true
set DOTNET_ROLL_FORWARD=Major
```

**Or use the setup script:**
```powershell
./setup-maxio-dev.ps1 -ApiKey "your-api-key" -Subdomain "cp-exp-2"
```

### 2. Run the PublicApi Project

```bash
cd src/PublicApi
dotnet run
```

The API will start on `https://localhost:25083`

### 3. Get an Authentication Token

First, authenticate to get a JWT token:

```bash
curl -X POST "https://localhost:25083/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }' \
  --insecure
```

Response includes an `token` field. Save this value:
```powershell
$token = "your-token-here"
```

## Test Endpoints

All subscription endpoints require the bearer token in the Authorization header.

### Test 1: Get Available Subscription Plans

Lists all subscription plans in the product family.

```bash
curl -X GET "https://localhost:25083/api/subscription-plans" \
  -H "Authorization: Bearer $token" \
  --insecure
```

**Expected response (200 OK):**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with advanced features",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "correlationId": "..."
}
```

### Test 2: Create a Subscription

Subscribe an authenticated user to a plan.

```bash
curl -X POST "https://localhost:25083/api/subscriptions" \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

**Expected response (201 Created):**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productName": "Pro Plan",
  "currentPeriodEndsAt": "2024-10-06T00:00:00Z",
  "nextAssessmentAt": "2024-10-06T00:00:00Z",
  "createdAt": "2024-09-06T14:30:00Z",
  "correlationId": "..."
}
```

### Test 3: Get User's Subscriptions

Retrieve all subscriptions for the authenticated user.

```bash
curl -X GET "https://localhost:25083/api/my-subscriptions" \
  -H "Authorization: Bearer $token" \
  --insecure
```

**Expected response (200 OK):**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "currentPeriodStartsAt": "2024-09-06T00:00:00Z",
      "currentPeriodEndsAt": "2024-10-06T00:00:00Z",
      "nextAssessmentAt": "2024-10-06T00:00:00Z",
      "activatedAt": "2024-09-06T00:00:00Z",
      "createdAt": "2024-09-06T14:30:00Z",
      "updatedAt": "2024-09-06T14:30:00Z"
    }
  ],
  "correlationId": "..."
}
```

## Complete Test Workflow

Here's a complete PowerShell script to test the entire flow:

```powershell
$apiUrl = "https://localhost:25083"
$auth = @{
    username = "demouser@microsoft.com"
    password = "Pass@word123"
}

# Step 1: Authenticate
Write-Host "Step 1: Authenticating..."
$authResponse = Invoke-WebRequest -Uri "$apiUrl/api/authenticate" `
  -Method Post `
  -ContentType "application/json" `
  -Body ($auth | ConvertTo-Json) `
  -SkipCertificateCheck

$token = ($authResponse.Content | ConvertFrom-Json).token
Write-Host "Token received: $($token.Substring(0, 20))..."

# Step 2: Get subscription plans
Write-Host "`nStep 2: Getting subscription plans..."
$headers = @{ Authorization = "Bearer $token" }
$plansResponse = Invoke-WebRequest -Uri "$apiUrl/api/subscription-plans" `
  -Method Get `
  -Headers $headers `
  -SkipCertificateCheck

$plans = $plansResponse.Content | ConvertFrom-Json
Write-Host "Available plans: $($plans.plans.Count)"
$plans.plans | ForEach-Object { Write-Host "  - $($_.name) ($($_.handle)): $$($_.priceInCents / 100)/month" }

# Step 3: Create a subscription
Write-Host "`nStep 3: Creating subscription to 'eshop-pro' plan..."
$subscribeRequest = @{ productHandle = "eshop-pro" }
$subscribeResponse = Invoke-WebRequest -Uri "$apiUrl/api/subscriptions" `
  -Method Post `
  -ContentType "application/json" `
  -Body ($subscribeRequest | ConvertTo-Json) `
  -Headers $headers `
  -SkipCertificateCheck

$subscription = $subscribeResponse.Content | ConvertFrom-Json
Write-Host "Subscription created!"
Write-Host "  ID: $($subscription.subscriptionId)"
Write-Host "  State: $($subscription.state)"
Write-Host "  Product: $($subscription.productName)"
Write-Host "  Next Billing: $($subscription.nextAssessmentAt)"

# Step 4: Get user subscriptions
Write-Host "`nStep 4: Retrieving user's subscriptions..."
$subsResponse = Invoke-WebRequest -Uri "$apiUrl/api/my-subscriptions" `
  -Method Get `
  -Headers $headers `
  -SkipCertificateCheck

$subs = $subsResponse.Content | ConvertFrom-Json
Write-Host "User has $($subs.subscriptions.Count) subscriptions:"
$subs.subscriptions | ForEach-Object {
  Write-Host "  - $($_.productName) (ID: $($_.id), State: $($_.state))"
}

Write-Host "`n✓ All tests completed successfully!"
```

## Troubleshooting

### 401 Unauthorized
- Verify the bearer token is valid and not expired
- Check the Authorization header format: `Authorization: Bearer <token>`

### 400 Bad Request - Missing ProductHandle
- Ensure the subscription create request includes `productHandle`
- Valid handles: `eshop-pro`, `basic-plan`, `eshop-subscribe`

### Connection refused
- Ensure PublicApi is running on the correct port (25083)
- Check firewall settings
- Verify HTTPS is being used

### Maxio API errors
- Verify Maxio credentials (ApiKey, Subdomain) are correct
- Ensure the product family handle matches Maxio sandbox setup
- Check network connectivity to Maxio API endpoints

## Database Note

The application uses an in-memory database for development (`UseOnlyInMemoryDatabase=true`). This means:
- All data is lost when the application stops
- Subscriptions created in one session won't persist after restart
- Perfect for testing, but not for production

## Next Steps

- Integrate the subscription endpoints into the eShopOnWeb web UI
- Add subscription management features (cancel, upgrade, downgrade)
- Implement webhook handlers for Maxio events
- Add billing portal integration for customer self-service
