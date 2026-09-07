# Maxio Subscription Billing Integration - Setup & Verification Guide

## Overview

This document provides instructions for setting up and verifying the Maxio Advanced Billing subscription integration added to eShopOnWeb.

## Architecture

The integration consists of:

1. **MaxioSubscriptionService** (`src/Infrastructure/Services/MaxioSubscriptionService.cs`)
   - HTTP-based client for Maxio API communication
   - Handles customer creation/lookup, product retrieval, and subscription management
   - Uses HTTP Basic Auth with API key

2. **Subscription Entity** (`src/ApplicationCore/Entities/Subscription.cs`)
   - Stores local subscription records for persistence and auditing
   - Tracks Maxio subscription IDs, pricing, and billing dates

3. **Three REST Endpoints** (in `src/PublicApi/SubscriptionEndpoints/`)
   - `GET /api/subscription-plans` - List available subscription plans
   - `POST /api/subscriptions` - Create a new subscription
   - `GET /api/my-subscriptions` - Retrieve current user's subscriptions

## Setup Prerequisites

### 1. Maxio Sandbox Account & Credentials

You need:
- A Maxio sandbox account (clone an existing site to 'cp-exp-4' or similar)
- API Key (created in Admin > Config > Integrations > API Keys)
- Site Subdomain (e.g., 'cp-exp-4')
- Seeded products/plans (the integration references specific product handles)

### 2. Environment Variables

Before running, set these environment variables:

```bash
# Linux/macOS
export MAXIO_API_KEY="your-api-key-here"
export MAXIO_SITE_SUBDOMAIN="cp-exp-4"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"

# Windows PowerShell
$env:MAXIO_API_KEY="your-api-key-here"
$env:MAXIO_SITE_SUBDOMAIN="cp-exp-4"
$env:MAXIO_ENVIRONMENT="sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

### 3. User Secrets (Alternative to Environment Variables)

Store credentials in .NET user secrets for development:

```bash
dotnet user-secrets set "Maxio:ApiKey" "your-api-key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-4"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

To manage user secrets in the PublicApi project:
```bash
cd src/PublicApi
dotnet user-secrets --help
```

## Running the Application

### Build

```bash
dotnet build src/PublicApi/PublicApi.csproj
```

### Run with In-Memory Database

Since LocalDB isn't available, use the in-memory database:

```bash
cd src/PublicApi

# Linux/macOS
dotnet run -- --UseOnlyInMemoryDatabase=true

# Windows PowerShell
dotnet run -- --UseOnlyInMemoryDatabase=true
```

The API will be available at: `https://localhost:28323/swagger`

### Environment Gotchas

1. **SDK Version**: The project uses .NET 8.0.x SDK. If only .NET 10 SDK is installed:
   ```bash
   DOTNET_ROLL_FORWARD=Major dotnet run
   ```

2. **HTTPS Dev Cert**: Ensure the dev certificate is trusted:
   ```bash
   dotnet dev-certs https --check
   dotnet dev-certs https --trust  # if needed
   ```

## Testing the Integration

### 1. Authenticate

First, get a JWT token by authenticating as a user:

```bash
curl -X POST https://localhost:28323/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

Response (example):
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "result": true
}
```

Copy the `token` value for subsequent requests.

### 2. List Subscription Plans

```bash
curl -X GET https://localhost:28323/api/subscription-plans \
  -H "Authorization: Bearer <YOUR_TOKEN>"
```

Expected response:
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Professional Plan",
      "description": "Professional subscription plan",
      "price": 299.00
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription plan",
      "price": 29.00
    }
  ]
}
```

### 3. Create a Subscription

Subscribe the authenticated user to a plan (using product ID from plans list):

```bash
curl -X POST https://localhost:28323/api/subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"productId": 7126957}'
```

Expected response:
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "price": 299.00,
  "nextBillingDate": "2026-10-07T00:00:00",
  "message": "Subscription created successfully"
}
```

**What happens behind the scenes:**
1. The system looks up or creates a Maxio customer for the logged-in user
2. Creates a subscription on that customer for the specified product
3. Stores the subscription details locally in the database

### 4. Get My Subscriptions

Retrieve all subscriptions for the authenticated user:

```bash
curl -X GET https://localhost:28323/api/my-subscriptions \
  -H "Authorization: Bearer <YOUR_TOKEN>"
```

Expected response:
```json
{
  "subscriptions": [
    {
      "id": 1,
      "maxioSubscriptionId": 12345678,
      "productHandle": "eshop-pro",
      "state": "active",
      "price": 299.00,
      "nextBillingDate": "2026-10-07T00:00:00",
      "createdAt": "2026-09-07T12:34:56"
    }
  ]
}
```

## Verification Checklist

Use this checklist to verify the integration is working correctly:

- [ ] Application builds without errors
- [ ] PublicApi starts successfully with in-memory database
- [ ] Can authenticate and receive a valid JWT token
- [ ] GET /api/subscription-plans returns plan list from Maxio
- [ ] POST /api/subscriptions creates subscription successfully
- [ ] Local subscription record is persisted in database
- [ ] GET /api/my-subscriptions shows newly created subscription
- [ ] Multiple subscription requests create separate subscription records
- [ ] Invalid/missing JWT token returns 401 Unauthorized
- [ ] Subscription state matches Maxio response ('active', 'pending', etc.)

## Troubleshooting

### "Maxio:ApiKey is not configured"
- Verify environment variables or user-secrets are set correctly
- Check that you're running from the src/PublicApi directory when using user-secrets

### "Failed to retrieve subscription plans" (500 error)
- Verify Maxio credentials are correct
- Check that Maxio site is accessible from your network
- Verify the Subdomain value matches your Maxio sandbox site
- API Key must have permissions to read products and subscriptions

### "Subscription created successfully" but product is wrong
- Verify the `productId` in your POST request matches a valid Maxio product ID
- Confirm the product exists in your Maxio sandbox site

### Connection timeout
- Ensure you have internet connectivity to Maxio servers
- Verify firewall/proxy isn't blocking access to `https://cp-exp-4.chargify.com`

## API Endpoints Detail

### GET /api/subscription-plans

**Authentication**: Required (Bearer JWT)

**Response**: 200 OK
```json
{
  "plans": [
    {
      "id": integer,
      "handle": "string",
      "name": "string", 
      "description": "string",
      "price": decimal
    }
  ]
}
```

**Error**: 400 Bad Request if Maxio API call fails

---

### POST /api/subscriptions

**Authentication**: Required (Bearer JWT)

**Request Body**:
```json
{
  "productId": integer
}
```

**Response**: 201 Created
```json
{
  "subscriptionId": integer,
  "state": "string",
  "price": decimal,
  "nextBillingDate": "datetime or null",
  "message": "Subscription created successfully"
}
```

**Error**: 400 Bad Request if subscription creation fails

---

### GET /api/my-subscriptions

**Authentication**: Required (Bearer JWT)

**Response**: 200 OK
```json
{
  "subscriptions": [
    {
      "id": integer,
      "maxioSubscriptionId": integer,
      "productHandle": "string",
      "state": "string",
      "price": decimal,
      "nextBillingDate": "datetime or null",
      "createdAt": "datetime"
    }
  ]
}
```

**Error**: 400 Bad Request if retrieval fails

## Configuration Files Modified

- `global.json` - Updated SDK rollForward to `latestMajor`
- `Directory.Packages.props` - Added Microsoft.Extensions.Http NuGet version
- `src/Infrastructure/Infrastructure.csproj` - Added Microsoft.Extensions.Http package
- `src/PublicApi/appsettings.json` - Added Maxio configuration section
- `src/PublicApi/Program.cs` - Registered MaxioSubscriptionService and endpoints

## Database Schema

The Subscription table stores:
- `Id` (int, PK) - Local subscription record ID
- `UserId` (string) - eShopWeb user ID
- `MaxioSubscriptionId` (int) - Maxio subscription ID (unique per user)
- `MaxioProductId` (int) - Maxio product ID
- `ProductHandle` (string) - Product identifier from Maxio
- `State` (string) - Subscription state ('active', 'pending', 'canceled', etc.)
- `CurrentPrice` (decimal) - Monthly recurring revenue
- `NextBillingDate` (datetime, nullable) - Next billing date
- `CreatedAt` (datetime) - When subscription was created locally
- `UpdatedAt` (datetime) - When subscription was last synced

Indexes:
- Unique index on (UserId, MaxioSubscriptionId) to prevent duplicates
- Index on UserId for fast user subscription lookup

## Notes for Production

1. **Secrets Management**: Use Azure Key Vault or similar for production credentials
2. **Database**: Change from in-memory to SQL Server or similar persistent store
3. **Error Handling**: Add comprehensive logging and monitoring
4. **Rate Limiting**: Implement Maxio API rate limit handling
5. **Sync Job**: Consider adding periodic sync of subscription state from Maxio
6. **Webhook Support**: Add Maxio webhook receivers for real-time subscription events
7. **Subscription Management UI**: Implement cancel/pause/update subscription endpoints
8. **Testing**: Add integration tests with Maxio API mocking
