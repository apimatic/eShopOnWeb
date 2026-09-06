# Maxio Subscription Integration - Verification Guide

This guide explains how to verify the Maxio subscription billing integration in eShopOnWeb.

## Setup - Configure Maxio Credentials

Before running the application, configure Maxio credentials using .NET user secrets:

```bash
# Navigate to PublicApi directory
cd src/PublicApi

# Set Maxio credentials (replace with actual values from site cp-exp-1)
dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Optional: Override base URL if needed
# dotnet user-secrets set "Maxio:BaseUrl" "https://your-custom-url.com"
```

## Environment Setup

The application requires:
- **.NET 8.0 runtime** (or SDK with rollForward: latestMajor enabled)
- **In-memory database** - Run with `UseOnlyInMemoryDatabase=true`
- **HTTPS dev certificate** - Ensure it's trusted with `dotnet dev-certs https --check`

### Running the PublicApi

```bash
cd src/PublicApi

# Run with in-memory database and environment variables
$env:UseOnlyInMemoryDatabase="true"
dotnet run
```

The API will listen on `https://localhost:27363`

## Testing the Subscription Endpoints

### 1. Authenticate First

Get a JWT token for testing:

```bash
curl -X POST https://localhost:27363/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username": "demouser@microsoft.com", "password": "Pass@word1"}' \
  -k
```

Response contains a `token` field. Copy this token for subsequent requests.

### 2. List Available Subscription Plans

```bash
curl -X GET https://localhost:27363/api/subscription-plans \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -k
```

Expected response: List of available plans (Pro Plan: $299/month, Basic Plan: $29/month)

### 3. Subscribe to a Plan

```bash
curl -X POST https://localhost:27363/api/subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}' \
  -k
```

Expected response: Subscription created successfully with state (likely "awaiting_signup" since payment method is not required)

**Idempotency check:** Run the same request again - should return the existing subscription without creating a duplicate.

### 4. Get My Subscriptions

```bash
curl -X GET https://localhost:27363/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -k
```

Expected response: List containing the subscription created in step 3

## Implementation Details

### Endpoints Created

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/subscription-plans` | GET | List available subscription plans |
| `/api/subscriptions` | POST | Create a subscription for authenticated user |
| `/api/my-subscriptions` | GET | List user's current subscriptions |

All endpoints require JWT authentication (Bearer token).

### Key Features

1. **Idempotent Customer Creation** - Uses user ID as customer reference to prevent duplicate customers on double-click
2. **No Payment Required** - Plans configured without upfront payment profile
3. **JWT Authentication** - Endpoints are secured with JWT tokens
4. **Automatic Customer Lookup** - Checks for existing customer before creating new one
5. **Subscription State Tracking** - Returns subscription state, next billing date, and pricing

### Database Note

- Uses in-memory database (by design - persists only during single run)
- User-subscription mappings are stored via Maxio customer reference (user ID)
- To persist across restarts, replace in-memory provider with SQL Server

### Maxio Configuration

The integration reads configuration from:
- `Maxio:ApiKey` - API key for authentication
- `Maxio:Subdomain` - Maxio subdomain (e.g., "your-company")
- `Maxio:ProductFamilyHandle` - Product family handle for filtering plans (default: "eshop-subscribe")
- `Maxio:BaseUrl` - Optional override for Maxio base URL

## Troubleshooting

### Port Conflicts
If ports 27363/27361 are in use, update `launchSettings.json` in Web and PublicApi projects.

### Certificate Issues
```bash
# Verify dev cert is trusted
dotnet dev-certs https --check

# If needed, create and trust a new cert
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

### Authentication Issues
- Ensure token is included in Authorization header as: `Bearer <token>`
- Token expires after 7 days
- Get a new token via `/api/authenticate` endpoint

### Maxio Connection Errors
- Verify API key and subdomain are correct
- Confirm account is on `cp-exp-1` sandbox site
- Check `Maxio:BaseUrl` if using custom server

## API Response Examples

### Successful Subscription Creation

```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "subscription": {
    "id": 1234567,
    "state": "awaiting_signup",
    "productName": "Pro Plan",
    "productHandle": "eshop-pro",
    "productPriceInCents": 29900,
    "nextAssessmentAt": "2026-10-07T00:00:00Z",
    "activatedAt": "2026-09-07T12:34:56Z",
    "paymentCollectionMethod": "automatic"
  },
  "success": true,
  "message": "Subscription created successfully. State: awaiting_signup"
}
```

### Subscription List Response

```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "subscriptions": [
    {
      "id": 1234567,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "productPriceInCents": 29900,
      "nextAssessmentAt": "2026-10-07T00:00:00Z",
      "activatedAt": "2026-09-07T12:34:56Z",
      "paymentCollectionMethod": "automatic"
    }
  ]
}
```

## Files Modified/Created

- **Modified:**
  - `src/PublicApi/PublicApi.csproj` - Added Maxio SDK reference
  - `src/PublicApi/Program.cs` - Registered Maxio client with DI
  - `Directory.Packages.props` - Added SDK version

- **Created:**
  - `src/PublicApi/SubscriptionEndpoints/ListPlansEndpoint.cs`
  - `src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs`
  - `src/PublicApi/SubscriptionEndpoints/ListMySubscriptionsEndpoint.cs`
  - `src/PublicApi/SubscriptionEndpoints/*.cs` - Supporting DTOs and response classes
  - `maxio-plan.md` - Maxio contract sheet and planning document
