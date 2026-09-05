# Maxio Subscription Integration Guide

## Overview

This guide explains how to set up and verify the Maxio Advanced Billing integration for eShopOnWeb. The integration adds recurring subscription capabilities alongside the existing one-time commerce flow.

## Architecture

### New Components

1. **Infrastructure/Maxio/** - Maxio API client and DTOs
   - `MaxioClient.cs` - HTTP client for Maxio API calls
   - `IMaxioClient.cs` - Interface for the Maxio client
   - `MaxioSettings.cs` - Configuration settings
   - `MaxioDto.cs` - Data transfer objects for Maxio responses

2. **PublicApi/SubscriptionEndpoints/** - REST endpoints for subscription management
   - `ListSubscriptionPlansEndpoint.cs` - GET `/api/subscription-plans`
   - `CreateSubscriptionEndpoint.cs` - POST `/api/subscriptions`
   - `ListMySubscriptionsEndpoint.cs` - GET `/api/my-subscriptions`

3. **Database Changes**
   - Migration: `20260906000000_AddMaxioCustomerId.cs`
   - Adds `MaxioCustomerId` field to `AspNetUsers` table to track Maxio customer relationship

## Setup Instructions

### Prerequisites

- .NET 8.0 SDK or .NET 10 SDK with rollForward enabled
- ASP.NET Core 8.0 runtime (or use rollForward)
- Maxio Advanced Billing sandbox account with:
  - API Key
  - Site subdomain
  - Product Family Handle (e.g., "eshop-subscribe")

### Environment Variables

Set these environment variables before running the PublicApi:

```bash
# Required
MAXIO_API_KEY=your_api_key_here
MAXIO_SITE_SUBDOMAIN=your_subdomain_here
MAXIO_DEFAULT_PRODUCT_FAMILY=your_product_family_handle

# Optional: Override base URL (defaults to https://{subdomain}.chargify.com)
MAXIO_BASE_URL=https://custom.maxio.com
```

Or use .NET user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets init  # if not already done
dotnet user-secrets set "Maxio:ApiKey" "your_api_key"
dotnet user-secrets set "Maxio:Subdomain" "your_subdomain"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "your_family_handle"
```

### Database Setup

The application uses migrations for database updates. The MaxioCustomerId migration will be applied automatically on first run (or manually via `dotnet ef database update`).

For in-memory database testing (no LocalDB):

```bash
# On Windows
set UseOnlyInMemoryDatabase=true
```

Or add to launchSettings.json:

```json
"environmentVariables": {
  "UseOnlyInMemoryDatabase": "true"
}
```

### Runtime Environment

If using .NET 10 SDK with .NET 8.0 runtime requirement:

```bash
# On Windows
set DOTNET_ROLL_FORWARD=Major
```

Or in launchSettings.json:

```json
"environmentVariables": {
  "DOTNET_ROLL_FORWARD": "Major"
}
```

## API Endpoints

All endpoints require JWT authentication except for `/api/authenticate`.

### 1. List Subscription Plans

```http
GET /api/subscription-plans
Authorization: Bearer <token>
```

**Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "Professional subscription",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month",
      "requiresCreditCard": false
    }
  ]
}
```

### 2. Create Subscription

```http
POST /api/subscriptions
Authorization: Bearer <token>
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

**Response (201 Created):**
```json
{
  "subscriptionId": 12345,
  "state": "active",
  "productName": "Pro Plan",
  "monthlyPrice": 299.00,
  "nextBillingDate": "2026-10-06T12:00:00Z",
  "message": "Subscription created successfully. Next billing date: 2026-10-06"
}
```

### 3. List My Subscriptions

```http
GET /api/my-subscriptions
Authorization: Bearer <token>
```

**Response:**
```json
{
  "subscriptions": [
    {
      "id": 12345,
      "state": "active",
      "productName": "Pro Plan",
      "monthlyPrice": 299.00,
      "nextBillingDate": "2026-10-06T12:00:00Z",
      "activatedAt": "2026-09-06T12:00:00Z",
      "createdAt": "2026-09-06T12:00:00Z"
    }
  ]
}
```

## Verification Steps

### Step 1: Build the Solution

```bash
dotnet build
```

Verify no compilation errors.

### Step 2: Set Environment Variables

For testing locally, use one of these methods:

**Method A: Environment Variables (Windows Command Prompt)**
```cmd
set MAXIO_API_KEY=your_key
set MAXIO_SITE_SUBDOMAIN=cp-exp-3
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
set UseOnlyInMemoryDatabase=true
```

**Method B: .NET User Secrets**
```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "your_key"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-3"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"
```

**Method C: launchSettings.json**
Edit `src/PublicApi/Properties/launchSettings.json`:
```json
{
  "profiles": {
    "PublicApi": {
      "environmentVariables": {
        "MAXIO_API_KEY": "your_key",
        "MAXIO_SITE_SUBDOMAIN": "cp-exp-3",
        "MAXIO_DEFAULT_PRODUCT_FAMILY": "eshop-subscribe",
        "UseOnlyInMemoryDatabase": "true"
      }
    }
  }
}
```

### Step 3: Run PublicApi

```bash
cd src/PublicApi
dotnet run
```

The API should be available at `https://localhost:24703`.

### Step 4: Get Authentication Token

```bash
curl -X POST https://localhost:24703/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

Save the returned `token` value.

### Step 5: Test List Subscription Plans

```bash
curl -X GET https://localhost:24703/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Accept: application/json"
```

Expected: Returns list of available plans from the Maxio product family.

### Step 6: Test Create Subscription

```bash
curl -X POST https://localhost:24703/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}'
```

Expected: 201 Created with subscription details. User's MaxioCustomerId is now set.

### Step 7: Test List My Subscriptions

```bash
curl -X GET https://localhost:24703/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -H "Accept: application/json"
```

Expected: Returns the subscription created in Step 6.

### Step 8: Verify Idempotency

Run Step 6 again (same product handle, same user). Expected: New subscription is created with a different ID, confirming that each call creates a new subscription (not required to be idempotent - each subscription is a new entity).

To verify customer idempotency: Run Step 6 with a different product handle for the same user. Expected: Same Maxio customer ID is reused, new subscription is created.

## Key Features

### 1. Customer Management
- Customers are automatically created in Maxio on first subscription
- Uses user ID as reference (`eshop-user-{userId}`)
- Subsequent subscriptions for the same user reuse the Maxio customer
- MaxioCustomerId is stored in database to avoid repeated lookups

### 2. Payment Method
- Subscriptions use "remittance" payment collection method
- No credit card required for signup (as per sandbox configuration)
- Production configuration can be adjusted per product requirements

### 3. Error Handling
- API calls include logging
- User-friendly error messages returned to client
- Detailed error logging for debugging

### 4. Authorization
- All subscription endpoints require JWT authentication
- ClaimsPrincipal is used to identify the current user
- Unauthorized requests return 401

## Database Considerations

### In-Memory Database (for testing)
- Data is lost on application restart
- Sufficient for basic feature testing
- MaxioCustomerId mappings are temporary

### SQL Server (production)
- Use LocalDB or SQL Server instance
- Connection strings in `appsettings.json` or user-secrets
- Migrations are applied automatically on startup

## API Contract

All responses use consistent JSON structure:

**Success Response:**
```json
{
  "key": "value"
}
```

**Error Response:**
```json
{
  "error": "Error message",
  "details": "Additional details"
}
```

## OpenAPI Specification

The integration uses the Maxio OpenAPI spec located in `maxio-spec/openapi.yaml` as the authoritative contract. All API interactions conform to:

- Authentication: Basic Auth (API Key : x)
- Base URL: `https://{subdomain}.chargify.com` or custom override
- Response format: JSON with snake_case fields

## Troubleshooting

### "Failed to list products for family {handle}"
- Verify product family handle is correct
- Check Maxio API key has read permission
- Ensure Maxio site is accessible

### "Failed to get or create customer"
- Check Maxio API key has write permission
- Verify customer email is unique in Maxio
- Check for network connectivity issues

### "Failed to create subscription"
- Verify product exists in the specified family
- Check customer has been created
- Verify payment collection method is supported for the product

### Unauthorized (401)
- Verify JWT token is valid and not expired
- Check token is included in Authorization header with "Bearer " prefix

### Not Found (404) - user
- User may not exist in database
- Verify user ID in token matches a real user

## Production Considerations

1. **Secrets Management**: Use Azure Key Vault, AWS Secrets Manager, or similar
2. **Error Logging**: Implement centralized logging (Application Insights, ELK, etc.)
3. **Monitoring**: Set up alerts for API failures and subscription creation failures
4. **Rate Limiting**: Consider implementing rate limiting on subscription endpoints
5. **Webhook Handling**: Implement Maxio webhook handlers for subscription status changes
6. **Audit Trail**: Log all subscription operations for compliance
7. **Retry Logic**: Implement exponential backoff for transient failures
