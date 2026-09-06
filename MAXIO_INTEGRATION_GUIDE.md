# Maxio Subscription Billing Integration Guide

This document describes how to set up, configure, and verify the Maxio Advanced Billing integration for eShopOnWeb.

## Overview

The integration adds recurring-subscription billing capabilities to eShopOnWeb using Maxio Advanced Billing as the billing system of record. This is an additive capability that runs parallel to the existing cart/checkout flow.

### New Capabilities

- **GET `/api/subscription-plans`** - List available subscription plans
- **POST `/api/subscriptions`** - Subscribe a logged-in user to a plan
- **GET `/api/my-subscriptions`** - Retrieve user's active subscriptions

## Prerequisites

### Maxio Sandbox Account

1. Access the Maxio sandbox site at: `https://cp-exp-4.chargify.com` (or your assigned sandbox subdomain)
2. The following entities are pre-seeded on the sandbox site:
   - **Product Family**: `eshop-subscribe` (ID: 3023074)
   - **Pro Plan**: `eshop-pro` ($299/mo, ID: 7126957)
   - **Basic Plan**: `basic-plan` ($29/mo, ID: 7126958)
   - **Metered component**: `api-call` ($0.01/unit, ID: 3057195)

### Required Credentials

Obtain from Maxio:
- **API Key** - From dashboard: Config > Integrations > API Keys
- **Site Subdomain** - Your sandbox subdomain (e.g., `cp-exp-4`)
- **Product Family Handle** - The handle of the product family containing your plans (e.g., `eshop-subscribe`)

## Setup

### 1. Configure Maxio Credentials (Development)

Store the Maxio credentials securely using .NET user-secrets:

```powershell
cd src/PublicApi

# Set your actual credentials
dotnet user-secrets set "Maxio:ApiKey" "<YOUR_API_KEY>"
dotnet user-secrets set "Maxio:Subdomain" "<YOUR_SANDBOX_SUBDOMAIN>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<YOUR_PRODUCT_FAMILY_HANDLE>"

# Optional: Override the API base URL if needed
dotnet user-secrets set "Maxio:BaseUrl" ""  # Leave empty for default: https://{subdomain}.chargify.com
```

### 2. Database Setup

The application uses in-memory database by default. To run with SQL Server:

```bash
# Use in-memory (default for development)
dotnet run --configuration Development

# Or use SQL Server LocalDB
dotnet run --configuration Development -- --UseOnlyInMemoryDatabase=false
```

### 3. Handle SDK/Runtime Mismatch

If you encounter SDK version issues:

```powershell
# Allow .NET runtime to roll forward to the latest major version
$env:DOTNET_ROLL_FORWARD="Major"
dotnet run --configuration Development
```

### 4. HTTPS Development Certificate

Ensure your development certificate is trusted:

```bash
dotnet dev-certs https --check
# If not trusted, install it:
dotnet dev-certs https --trust
```

## Running the Application

### Start PublicApi Service

```powershell
cd src/PublicApi
dotnet run --launch-profile PublicApi
```

The PublicApi will start on: `https://localhost:25763`

### Start Web Application (Optional)

In a separate terminal:

```powershell
cd src/Web
dotnet run --launch-profile Web
```

The Web application will start on: `https://localhost:5001`

## Verification Steps

### Step 1: Generate JWT Token

Get an authentication token to access protected endpoints:

```powershell
$tokenResponse = Invoke-WebRequest -Uri "https://localhost:25763/api/authenticate" `
  -Method POST `
  -ContentType "application/json" `
  -Body '{"username":"demouser@microsoft.com","password":"Pass@word123"}' `
  -SkipCertificateCheck

$tokenData = $tokenResponse.Content | ConvertFrom-Json
$token = $tokenData.token

Write-Host "Token: $token"
```

### Step 2: List Available Subscription Plans

```powershell
$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
}

$plansResponse = Invoke-WebRequest -Uri "https://localhost:25763/api/subscription-plans" `
  -Headers $headers `
  -SkipCertificateCheck

$plansResponse.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10 | Write-Host
```

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 1,
      "maxioPlanId": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription...",
      "priceInCents": 29900,
      "currency": "USD"
    },
    {
      "id": 2,
      "maxioPlanId": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "Basic subscription...",
      "priceInCents": 2900,
      "currency": "USD"
    }
  ],
  "correlationId": "uuid-here"
}
```

### Step 3: Subscribe to a Plan

```powershell
$subscriptionPayload = @{
    "planHandle" = "eshop-pro"
} | ConvertTo-Json

$subscriptionResponse = Invoke-WebRequest -Uri "https://localhost:25763/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -ContentType "application/json" `
  -Body $subscriptionPayload `
  -SkipCertificateCheck

$subscriptionData = $subscriptionResponse.Content | ConvertFrom-Json
Write-Host $subscriptionData | ConvertTo-Json -Depth 10
```

**Expected Response (201 Created):**
```json
{
  "subscription": {
    "id": 1,
    "maxioSubscriptionId": 12345,
    "planHandle": "eshop-pro",
    "state": "active",
    "nextBillingAt": "2026-10-06T00:00:00Z",
    "priceInCents": 29900
  },
  "correlationId": "uuid-here"
}
```

### Step 4: Retrieve User's Subscriptions

```powershell
$mySubsResponse = Invoke-WebRequest -Uri "https://localhost:25763/api/my-subscriptions" `
  -Headers $headers `
  -SkipCertificateCheck

$mySubsResponse.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10 | Write-Host
```

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 12345,
      "planHandle": "eshop-pro",
      "state": "active",
      "nextBillingAt": "2026-10-06T00:00:00Z",
      "priceInCents": 29900
    }
  ],
  "correlationId": "uuid-here"
}
```

### Step 5: Verify in Maxio Dashboard

1. Log into `https://cp-exp-4.chargify.com` (your sandbox)
2. Navigate to **Customers**
3. Search for the user (email: `demouser@microsoft.com`)
4. Verify the customer and subscription were created:
   - Customer should exist with the user's email
   - Subscription should show "active" status
   - Next billing date should be set correctly
   - Plan should match the requested plan handle

## Troubleshooting

### Issue: "Maxio:ApiKey is not configured"

**Solution**: Verify user-secrets are set:
```powershell
cd src/PublicApi
dotnet user-secrets list
```

### Issue: "Failed to create subscription: 422"

**Possible causes**:
- Invalid plan handle - verify it exists on Maxio
- Customer reference already exists - the service handles this with idempotency
- Check the response content for detailed error message

### Issue: HTTPS Certificate Error

**Solution**:
```powershell
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

### Issue: Port Already in Use

**Solution**: Stop any existing processes on ports 25763/25764:
```powershell
netstat -ano | findstr :25763
taskkill /PID <PID> /F
```

## API Endpoint Reference

### GET /api/subscription-plans
- **Authentication**: Optional (public)
- **Description**: List all available subscription plans from Maxio
- **Response**: Array of SubscriptionPlanDto objects
- **Status Codes**: 200 OK

### POST /api/subscriptions
- **Authentication**: Required (JWT Bearer Token)
- **Description**: Create a subscription for the authenticated user
- **Request Body**:
  ```json
  {
    "planHandle": "eshop-pro"
  }
  ```
- **Response**: SubscriptionDto object
- **Status Codes**: 
  - 201 Created (success)
  - 400 Bad Request (invalid plan or subscription error)
  - 401 Unauthorized (missing/invalid token)

### GET /api/my-subscriptions
- **Authentication**: Required (JWT Bearer Token)
- **Description**: Get all subscriptions for the authenticated user
- **Response**: Array of SubscriptionDto objects
- **Status Codes**: 
  - 200 OK
  - 401 Unauthorized (missing/invalid token)

## Architecture Notes

### Project Structure

- **ApplicationCore**: Domain entities (Subscription, SubscriptionPlan)
- **Infrastructure**: Maxio service integration (MaxioService)
- **PublicApi**: REST endpoints for subscription management

### Entities

- **Subscription**: Represents a user's subscription with Maxio, stored locally for quick access
- **SubscriptionPlan**: Local cache of available plans from Maxio

### Database

- Uses in-memory database by default (for development)
- Can be configured to use SQL Server
- Migrations: `src/Infrastructure/Data/Migrations/`

## Production Deployment

For production deployment:

1. **Store Secrets in Azure Key Vault** or similar secrets management system
2. **Use SQL Server** or persistent database
3. **Enable HTTPS** with production certificates
4. **Configure CORS** appropriately
5. **Monitor Maxio API** rate limits and errors
6. **Implement logging and alerting** for subscription events

## API Documentation

Swagger/OpenAPI documentation is available at: `https://localhost:25763/swagger`

## Support

For issues with:
- **Maxio API**: See [Maxio Developer Docs](https://developers.maxio.com/)
- **eShopOnWeb**: See [GitHub Repository](https://github.com/dotnet-architecture/eShopOnWeb)

---

**Created**: September 6, 2026
**Integration**: Maxio Advanced Billing
**Version**: 1.0
