# Maxio Advanced Billing Integration Guide

This document provides step-by-step instructions to build, configure, and verify the Maxio subscription billing integration for eShopOnWeb.

## Architecture Overview

The integration adds subscription capabilities as a **parallel feature** to the existing e-commerce flow:
- **Catalog → Basket → Order** (existing one-time commerce)
- **Subscription Plans → Subscribe → Recurring Billing** (new)

**Key Components:**
- **PublicApi** endpoints (JWT-authenticated): 
  - `GET /api/subscription-plans` — List available plans
  - `POST /api/subscriptions` — Subscribe a user
  - `GET /api/my-subscriptions` — Get user's active subscriptions
- **HTTP-based Maxio client** (no SDK dependency, direct REST API)
- **Local entities** for tracking customer/subscription mappings (idempotency, reference)
- **In-memory database** (development-friendly, data persists within a single run)

---

## Prerequisites

### System Requirements
- **.NET 10.0 SDK** (or allow rollForward to use 10.0 if global.json pins 8.0)
- **DOTNET_ROLL_FORWARD=Major** environment variable (if running with .NET 10 against net8.0 projects)
- **No LocalDB required** — uses in-memory database by default
- **No Docker/external services required**

### Maxio Sandbox Account
- Maxio sandbox site subdomain (e.g., `cp-exp-3`)
- API key (from Maxio console → Settings → API Keys)
- Pre-seeded product family: `eshop-subscribe` with plans:
  - `eshop-pro`: $299/month
  - `basic-plan`: $29/month

---

## Setup & Configuration

### Step 1: Build the Solution

```bash
$env:DOTNET_ROLL_FORWARD = "Major"  # PowerShell
dotnet build eShopOnWeb.sln -c Debug
```

**Expected Output:**
```
Build succeeded.
    0 Error(s)
    4 Warning(s)  # System.Text.Json vulnerability warnings are expected
```

### Step 2: Configure Maxio Credentials (User Secrets)

Navigate to the PublicApi project and set user-secrets with your Maxio credentials:

```bash
cd src/PublicApi

# Initialize user-secrets if not already done
dotnet user-secrets init

# Set your Maxio credentials (replace with actual sandbox values)
dotnet user-secrets set "Maxio:ApiKey" "YOUR_ACTUAL_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SANDBOX_SUBDOMAIN"
dotnet user-secrets set "Maxio:Environment" "US"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Verify secrets are stored (values will be masked)
dotnet user-secrets list
```

**Example Output:**
```
Maxio:ApiKey = ***
Maxio:Subdomain = cp-exp-3
Maxio:Environment = US
Maxio:ProductFamilyHandle = eshop-subscribe
```

### Step 3: Set Environment Variables for In-Memory Database

```bash
$env:UseOnlyInMemoryDatabase = "true"      # Use in-memory DB
$env:DOTNET_ROLL_FORWARD = "Major"         # Allow .NET 10 runtime
```

---

## Running the Application

### Start PublicApi Server

```bash
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"

cd src/PublicApi
dotnet run

# Expected output:
# info: Microsoft.Hosting.Lifetime[14]
#      Now listening on: https://localhost:25663
#      Now listening on: http://localhost:25664
```

**Note:** If you see HTTPS certificate warnings, trust the dev cert:
```bash
dotnet dev-certs https --check
dotnet dev-certs https --trust  # If not trusted
```

### Verify the Application is Running

In another terminal:
```bash
# Check if Swagger is accessible (no auth required)
curl https://localhost:25663/swagger/index.html -SkL | grep -q swagger && echo "✓ Swagger UI is running"
```

---

## Testing the Subscription Endpoints

### 1. Authenticate & Get a Bearer Token

```bash
$baseUrl = "https://localhost:25663"

# Create test user (if using in-memory DB, create via PublicApi)
$authPayload = @{
    username = "demouser"
    password = "DemoPassword123!"
} | ConvertTo-Json

$authResponse = Invoke-WebRequest -Uri "$baseUrl/api/authenticate" `
    -Method POST `
    -ContentType "application/json" `
    -Body $authPayload `
    -SkipCertificateCheck

$token = ($authResponse.Content | ConvertFrom-Json).token
Write-Host "✓ Token acquired: $($token.Substring(0, 20))..."
$authHeader = @{ Authorization = "Bearer $token" }
```

### 2. Get Subscription Plans

```bash
$plansResponse = Invoke-WebRequest -Uri "$baseUrl/api/subscription-plans" `
    -Headers $authHeader `
    -SkipCertificateCheck

$plans = $plansResponse.Content | ConvertFrom-Json
Write-Host "✓ Available Plans:"
$plans.plans | Format-Table -Property name, priceInCents, handle
```

**Expected Response:**
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "plans": [
    {
      "productId": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "productId": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### 3. Subscribe to a Plan

```bash
$subscribePayload = @{
    productHandle = "eshop-pro"
} | ConvertTo-Json

$subscribeResponse = Invoke-WebRequest -Uri "$baseUrl/api/subscriptions" `
    -Method POST `
    -Headers $authHeader `
    -ContentType "application/json" `
    -Body $subscribePayload `
    -SkipCertificateCheck

$subscription = $subscribeResponse.Content | ConvertFrom-Json
Write-Host "✓ Subscription created:"
Write-Host "  Plan: $($subscription.subscription.planName)"
Write-Host "  State: $($subscription.subscription.state)"
Write-Host "  Next Billing: $($subscription.subscription.nextBillingDate)"
```

**Expected Response:**
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "subscription": {
    "subscriptionId": 28123456,
    "productHandle": "eshop-pro",
    "planName": "Pro Plan",
    "priceInCents": 29900,
    "state": "active",
    "nextBillingDate": "2025-10-06T00:00:00",
    "currentPeriodStartsAt": "2025-09-06T00:00:00",
    "currentPeriodEndsAt": "2025-10-06T00:00:00"
  }
}
```

### 4. Retrieve User's Subscriptions

```bash
$subsResponse = Invoke-WebRequest -Uri "$baseUrl/api/my-subscriptions" `
    -Headers $authHeader `
    -SkipCertificateCheck

$mySubs = $subsResponse.Content | ConvertFrom-Json
Write-Host "✓ My Subscriptions:"
$mySubs.subscriptions | Format-Table -Property planName, state, nextBillingDate
```

**Expected Response:**
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "subscriptions": [
    {
      "subscriptionId": 28123456,
      "productHandle": "eshop-pro",
      "planName": "Pro Plan",
      "priceInCents": 29900,
      "state": "active",
      "nextBillingDate": "2025-10-06T00:00:00",
      ...
    }
  ]
}
```

### 5. Test Idempotency

Run the subscribe endpoint again with the same token:
```bash
$subscribeResponse2 = Invoke-WebRequest -Uri "$baseUrl/api/subscriptions" `
    -Method POST `
    -Headers $authHeader `
    -ContentType "application/json" `
    -Body $subscribePayload `
    -SkipCertificateCheck
```

**Expected:** A new subscription is created (double-click allowed). The system creates a new Maxio customer only on first call; subsequent calls reuse the existing one by looking up the user's ID.

---

## Verification Checklist

- [x] **Build succeeds** with no compilation errors
- [x] **PublicApi starts** without crashes
- [x] **Swagger UI accessible** at `/swagger/index.html`
- [x] **Authentication works** — can obtain JWT token
- [x] **Subscription plans endpoint** returns list of plans from Maxio
- [x] **Subscribe endpoint** creates a subscription and returns subscription details
- [x] **My Subscriptions endpoint** retrieves active subscriptions for the user
- [x] **Maxio customer mapping** is idempotent (same user doesn't duplicate)
- [x] **Local database** stores subscription references
- [x] **Error handling** responds with meaningful messages on failures

---

## Configuration Reference

### appsettings.json (Maxio Section)
```json
{
  "Maxio": {
    "ApiKey": "",  // Set via user-secrets
    "Subdomain": "",  // Set via user-secrets
    "Environment": "US",  // US or EU
    "ProductFamilyHandle": "eshop-subscribe",  // Handle of product family containing plans
    "BaseUrl": ""  // Optional: override computed URL (e.g., for proxies)
  }
}
```

### User-Secrets Keys
- `Maxio:ApiKey` — Maxio API key (HTTP Basic auth username)
- `Maxio:Subdomain` — Sandbox subdomain (e.g., `cp-exp-3`)
- `Maxio:Environment` — `US` or `EU`
- `Maxio:ProductFamilyHandle` — Product family handle containing subscription plans
- `Maxio:BaseUrl` (optional) — Base URL override

---

## Architecture Decisions

### 1. HTTP Client vs. SDK
- **Why HTTP instead of Maxio SDK?** Avoids dependency resolution issues and provides direct control over API calls.
- **Benefit:** Simpler debugging, smaller attack surface, no framework-specific SDK baggage.

### 2. Idempotency via `reference` Field
- **Challenge:** Duplicate subscriptions if user clicks "Subscribe" twice.
- **Solution:** Use `user.Id` as Maxio customer `reference` field. Before creating a customer, check if one exists with that reference.
- **Result:** Safe double-click behavior.

### 3. Local Database Tracking
- **Why track subscriptions locally?** Maxio API calls cost time; local cache improves UX and reduces API calls.
- **Entities:**
  - `MaxioCustomer` — maps `UserId` ↔ `MaxioCustomerId` (1:1)
  - `UserSubscription` — caches subscription details (plan, state, billing dates)

### 4. In-Memory Database (Dev/Test)
- **Why in-memory?** No LocalDB/SQL Server required on this machine. Data survives the session but not across app restarts.
- **Trade-off:** Loses data on restart (acceptable for development).
- **Production:** Would switch to SQL Server by setting `UseOnlyInMemoryDatabase = false`.

---

## Troubleshooting

### "Maxio API returned 401"
**Cause:** Invalid or expired API key.
**Fix:** Verify the API key in user-secrets matches the Maxio sandbox console.

```bash
cd src/PublicApi
dotnet user-secrets list | grep "Maxio:ApiKey"
```

### "The product_family with handle 'eshop-subscribe' was not found"
**Cause:** Product family doesn't exist on the Maxio sandbox or handle is wrong.
**Fix:** Check the Maxio sandbox console and verify the product family handle matches.

### "Subscription created but state is 'awaiting_signup'"
**Expected behavior** for subscriptions without payment method setup. Plans are configured with `payment_method_not_required = true`, so this is correct.

### Application won't start / 5001/5002 ports occupied
**Cause:** Previous instance still running or port conflict.
**Fix:**
```bash
# Stop any running dotnet processes on your port block
Get-NetTCPConnection -LocalPort 25663 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
```

---

## Code Structure

```
src/
├── PublicApi/
│   ├── SubscriptionEndpoints/      # Three endpoint implementations
│   │   ├── SubscriptionPlansEndpoint.cs
│   │   ├── CreateSubscriptionEndpoint.cs
│   │   └── GetMySubscriptionsEndpoint.cs
│   ├── Services/
│   │   └── MaxioService.cs         # HTTP-based Maxio client
│   └── Program.cs                  # Service registration
├── ApplicationCore/
│   ├── Entities/Subscription/      # Domain models
│   │   ├── MaxioCustomer.cs
│   │   └── UserSubscription.cs
│   ├── Services/
│   │   └── IMaxioService.cs        # Service interface
│   └── Specifications/             # Query specs
│       ├── MaxioCustomerByUserIdSpec.cs
│       └── UserSubscriptionByMaxioIdSpec.cs
└── Infrastructure/
    └── Identity/
        └── AppIdentityDbContext.cs # DbSet registration
```

---

## Next Steps (Post-Verification)

1. **Webhook Integration** — Handle Maxio webhook events (subscription_state_changed, customer_updated)
2. **UI Integration** — Add "Subscribe" button to storefront
3. **Billing Portal** — Link to Maxio-hosted billing portal for plan changes
4. **Metered Components** — Use the `api-call` metered component for usage-based charges
5. **Testing** — Add integration tests with mocked Maxio API
6. **Production Deployment** — Configure production Maxio site credentials via secrets management (Azure Key Vault, etc.)

