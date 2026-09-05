# Maxio Subscription Billing Integration for eShopOnWeb

This document describes the complete Maxio subscription billing integration added to eShopOnWeb, including setup instructions and a step-by-step verification guide.

## Overview

The integration adds recurring subscription billing to eShopOnWeb via **Maxio Advanced Billing** as the billing system of record. This is an **additive capability** that runs **parallel to** the existing one-time commerce flow (Catalog → Basket → Order).

### Key Features

- **Hero Flow**: Logged-in shoppers browse available subscription plans and subscribe to one
- **Idempotent Customer Creation**: Ensures no duplicate Maxio customers are created for the same eShopOnWeb user
- **JWT-Authenticated Endpoints**: All subscription endpoints require bearer token authentication
- **Reference-Based Lookups**: Uses eShopOnWeb user IDs as references in Maxio for reliable customer mapping
- **Persisted Mappings**: Tracks eShopOnWeb users ↔ Maxio customers in the local database

## Architecture

### New Endpoints (PublicApi)

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| GET | `/api/subscription-plans` | List available subscription plans | JWT |
| POST | `/api/subscriptions` | Subscribe to a plan | JWT |
| GET | `/api/my-subscriptions` | Get user's active subscriptions | JWT |

### New Services

- **MaxioApiClient** (`src/ApplicationCore/Services/MaxioApiClient.cs`): HTTP client for Maxio API calls
  - Handles Basic auth with Maxio API key
  - Manages customer lookup, creation, and subscription operations
  - Implements idempotent customer operations using reference values

### New Entities

- **MaxioCustomerMapping** (`src/ApplicationCore/Entities/SubscriptionAggregate/MaxioCustomerMapping.cs`): Maps eShopOnWeb user IDs to Maxio customer IDs
  - Stored in local database for persistence across sessions
  - Enables efficient lookup of Maxio customer when subscribing

### Configuration

Maxio settings are loaded from environment variables with fallback to `appsettings.json`:

| Setting | Env Var | appsettings | Purpose |
|---------|---------|-------------|---------|
| API Key | `MAXIO_API_KEY` | `Maxio:ApiKey` | Maxio API authentication |
| Subdomain | `MAXIO_SITE_SUBDOMAIN` | `Maxio:Subdomain` | Maxio tenant identifier |
| Product Family | `MAXIO_DEFAULT_PRODUCT_FAMILY` | `Maxio:ProductFamilyHandle` | Handle of product family for filtering |
| Base URL | (none) | `Maxio:BaseUrl` | Optional override of API base URL |

## Prerequisites

### Development Environment

```
.NET 8.0 SDK (or .NET 10 with DOTNET_ROLL_FORWARD=Major)
ASP.NET Core 8.0 runtime
```

### Maxio Sandbox Account

- **Site subdomain**: e.g., `cp-exp-3` (from sandbox signup)
- **API key**: Generated in Maxio dashboard under API Keys
- **Pre-seeded entities** on `cp-exp-3` site:
  - Product Family: `eshop-subscribe` (ID: 3023074)
  - Pro Plan: `eshop-pro` (ID: 7126957) — $299.00/mo
  - Basic Plan: `basic-plan` (ID: 7126958) — $29.00/mo
  - Metered component: `api-call` (ID: 3057195) — $0.01/unit

## Setup Instructions

### Step 1: Set Environment Variables

```powershell
# PowerShell (development)
$env:MAXIO_API_KEY = "your-api-key-from-maxio"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-3"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
```

Or on Windows (permanent):

```cmd
setx MAXIO_API_KEY "your-api-key"
setx MAXIO_SITE_SUBDOMAIN "cp-exp-3"
setx MAXIO_DEFAULT_PRODUCT_FAMILY "eshop-subscribe"
```

### Step 2: Configure Database

The project uses in-memory database by default (set via environment variable or appsettings):

```bash
# Use in-memory database (default for development)
$env:UseOnlyInMemoryDatabase = "true"

# Or modify appsettings.Development.json:
# "UseOnlyInMemoryDatabase": true
```

### Step 3: Install HTTPS Dev Certificate (if needed)

```bash
dotnet dev-certs https --check  # Check if trusted
dotnet dev-certs https --trust   # Install and trust cert
```

### Step 4: Build and Run

```bash
cd src/PublicApi
dotnet run
```

PublicApi will start on `https://localhost:24463`

## Verification Guide

### Phase 1: Authenticate

**Goal**: Obtain a JWT token for API requests

```bash
curl -X POST https://localhost:24463/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }' \
  --insecure
```

**Expected Response**:
```json
{
  "result": true,
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "username": "demouser@microsoft.com",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "correlationId": "..."
}
```

**Save the token**:
```bash
$token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

### Phase 2: List Subscription Plans

**Goal**: Verify Maxio API connectivity and plan retrieval

```bash
curl -X GET https://localhost:24463/api/subscription-plans \
  -H "Authorization: Bearer $token" \
  --insecure
```

**Expected Response**:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "$299 Pro Plan",
      "handle": "eshop-pro",
      "description": "...",
      "price": 299.00,
      "interval": "1",
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "name": "$29 Basic Plan",
      "handle": "basic-plan",
      "description": "...",
      "price": 29.00,
      "interval": "1",
      "intervalUnit": "month"
    }
  ],
  "correlationId": "..."
}
```

**Verify**:
- ✓ Both plans are listed
- ✓ Plan handles match `eshop-pro` and `basic-plan`
- ✓ Prices are correct ($299 and $29)

### Phase 3: Create a Subscription

**Goal**: Subscribe the authenticated user to a plan and verify customer/subscription creation

```bash
curl -X POST https://localhost:24463/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

**Expected Response** (HTTP 201 Created):
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productName": "$299 Pro Plan",
  "productHandle": "eshop-pro",
  "price": 299.00,
  "currentPeriodEndsAt": "2026-10-06T12:34:56Z",
  "nextAssessmentAt": "2026-10-06T12:34:56Z",
  "activatedAt": "2026-09-06T12:34:56Z",
  "correlationId": "..."
}
```

**Verify**:
- ✓ HTTP status is 201 (Created)
- ✓ Subscription state is `active`
- ✓ Product handle matches the request
- ✓ `nextAssessmentAt` is approximately 30 days in the future

### Phase 4: Retrieve User's Subscriptions

**Goal**: Verify subscription persistence and retrieval

```bash
curl -X GET https://localhost:24463/api/my-subscriptions \
  -H "Authorization: Bearer $token" \
  --insecure
```

**Expected Response**:
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productId": 7126957,
      "productName": "$299 Pro Plan",
      "productHandle": "eshop-pro",
      "productPrice": 299.00,
      "currentPeriodEndsAt": "2026-10-06T12:34:56Z",
      "nextAssessmentAt": "2026-10-06T12:34:56Z",
      "activatedAt": "2026-09-06T12:34:56Z",
      "createdAt": "2026-09-06T12:34:56Z"
    }
  ],
  "correlationId": "..."
}
```

**Verify**:
- ✓ Subscription list is not empty
- ✓ The subscription from Phase 3 appears in the list
- ✓ All timestamp fields are populated

### Phase 5: Idempotency Test

**Goal**: Verify that subscribing twice doesn't create duplicate customers/subscriptions

```bash
# First subscription (from Phase 3 if not done)
curl -X POST https://localhost:24463/api/subscriptions \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{ "productHandle": "basic-plan" }' \
  --insecure
```

**Expected Response**: HTTP 201 with new subscription for `basic-plan`

**Verify**:
- ✓ New subscription created for different product
- ✓ In Maxio dashboard, only ONE Maxio customer exists for this eShopOnWeb user (no duplicates)

### Phase 6: Verify in Maxio Dashboard

**Goal**: Confirm that all operations are reflected in Maxio

1. Log into Maxio sandbox: `https://cp-exp-3.chargify.com`
2. Navigate to **Customers**
3. Look for a customer with reference = eShopOnWeb user ID (e.g., `00000000-0000-0000-0000-000000000001`)
4. Click the customer
5. Verify:
   - ✓ Email matches eShopOnWeb user email
   - ✓ First Name / Last Name are populated (from eShopOnWeb)
   - ✓ **Subscriptions** tab shows the active subscriptions created above
   - ✓ Subscription state is `active`
   - ✓ Next billing date is correct

## Database Schema

### MaxioCustomerMappings Table

Tracks the mapping between eShopOnWeb users and Maxio customers.

| Column | Type | Purpose |
|--------|------|---------|
| Id | int | Primary key |
| EshopUserId | string(128) | eShopOnWeb user ID (unique) |
| MaxioCustomerId | long | Maxio customer ID |
| CreatedAt | datetime | Mapping creation timestamp |
| UpdatedAt | datetime | Last update timestamp |

**Index**: `UX_MaxioCustomerMappings_EshopUserId` (unique on EshopUserId)

## API Contract (from Maxio OpenAPI Spec)

### GET /api/subscription-plans

Returns list of subscription plans available for purchase.

**Request**:
```
Authorization: Bearer {token}
```

**Response** (200 OK):
```json
{
  "plans": [
    {
      "id": number,
      "name": string,
      "handle": string,
      "description": string,
      "price": number,
      "interval": string,
      "intervalUnit": string
    }
  ],
  "correlationId": string
}
```

### POST /api/subscriptions

Creates a new subscription for the authenticated user.

**Request**:
```
Authorization: Bearer {token}
Content-Type: application/json

{
  "productHandle": "eshop-pro"
}
```

**Response** (201 Created):
```json
{
  "subscriptionId": number,
  "state": string,
  "productName": string,
  "productHandle": string,
  "price": number,
  "currentPeriodEndsAt": string (ISO 8601),
  "nextAssessmentAt": string (ISO 8601),
  "activatedAt": string (ISO 8601),
  "correlationId": string
}
```

### GET /api/my-subscriptions

Returns all subscriptions for the authenticated user.

**Request**:
```
Authorization: Bearer {token}
```

**Response** (200 OK):
```json
{
  "subscriptions": [
    {
      "id": number,
      "state": string,
      "productId": number,
      "productName": string,
      "productHandle": string,
      "productPrice": number,
      "currentPeriodEndsAt": string (ISO 8601),
      "nextAssessmentAt": string (ISO 8601),
      "activatedAt": string (ISO 8601),
      "createdAt": string (ISO 8601)
    }
  ],
  "correlationId": string
}
```

## Troubleshooting

### "Unauthorized" on subscription endpoints

- ✓ Verify the `Authorization: Bearer {token}` header is present
- ✓ Verify token is not expired
- ✓ Verify user is authenticated via `/api/authenticate`

### "404 Not Found" on subscription endpoints

- ✓ Verify endpoints are registered (check Program.cs)
- ✓ Verify PublicApi is running on `https://localhost:24463`
- ✓ Verify endpoints URL is exactly `/api/subscription-plans`, `/api/subscriptions`, `/api/my-subscriptions`

### "Failed to create Maxio customer" error

- ✓ Verify `MAXIO_API_KEY` is correct
- ✓ Verify `MAXIO_SITE_SUBDOMAIN` is correct
- ✓ Verify network connectivity to Maxio API
- ✓ Verify Maxio API key has sufficient permissions (Customers → Create)

### Empty plans list

- ✓ Verify product family handle is correct (`eshop-subscribe`)
- ✓ Verify plans exist in Maxio for this product family
- ✓ Verify plans are not archived

### Subscriptions not persisting after app restart (in-memory DB)

This is expected behavior with `UseOnlyInMemoryDatabase=true`. All data is lost when the app restarts. To persist:
- Switch to SQL Server LocalDB (remove `UseOnlyInMemoryDatabase`)
- Or run the app in a single session for testing

## Key Implementation Details

### Idempotent Customer Creation

The system ensures exactly one Maxio customer exists per eShopOnWeb user:

1. **Reference-based lookup**: Uses eShopOnWeb user ID as the Maxio `reference` field
2. **Idempotent creation**: If `GetCustomerByReferenceAsync` succeeds, returns existing customer; if it fails (404), creates new customer
3. **Local tracking**: Stores mapping in `MaxioCustomerMappings` table for efficient future lookups

### Payment Method Not Required

Both seeded plans have `require_credit_card: false`, so subscriptions can be created without payment information. This simplifies the signup flow for testing.

### Correlation IDs

All responses include a `correlationId` GUID for request tracing. This helps correlate frontend/backend logs.

## Production Considerations

1. **Error Handling**: Current implementation returns basic HTTP error responses. Add structured error handling with specific error codes for production.

2. **Logging**: Add logging at key points (customer creation, subscription creation, API errors) for observability.

3. **Rate Limiting**: Consider implementing rate limits on subscription endpoints.

4. **Webhooks**: Maxio can send webhooks when subscriptions change (e.g., renewal, failure, cancellation). Implement webhook handlers for business logic.

5. **Payment Methods**: Update endpoints to accept and handle payment information for production usage.

6. **Validation**: Add business logic validation (e.g., prevent multiple active subscriptions, enforce subscription limits).

7. **Auditing**: Track who created/modified subscriptions in the local database for compliance.

## References

- **Maxio OpenAPI Spec**: `maxio-spec/openapi.yaml` (authoritative contract)
- **Maxio Documentation**: https://docs.maxio.com
- **eShopOnWeb Project**: https://github.com/dotnet-architecture/eShopOnWeb
