# Maxio Subscription Billing Integration — Verification Guide

This guide provides step-by-step instructions to verify the Maxio subscription billing integration for eShopOnWeb.

## Prerequisites

- .NET 8.0 SDK (or .NET 10 with `DOTNET_ROLL_FORWARD=Major`)
- Maxio sandbox account with credentials set as environment variables:
  - `MAXIO_API_KEY` — API key
  - `MAXIO_SITE_SUBDOMAIN` — Sandbox subdomain (e.g., `cp-exp-2`)
  - `MAXIO_ENVIRONMENT` — Environment (default: `US`)
  - `MAXIO_DEFAULT_PRODUCT_FAMILY` — Product family handle (e.g., `eshop-subscribe`)
- HTTPS dev certificate installed (`dotnet dev-certs https --check`)

## Step 1: Build the Solution

Verify the solution builds cleanly:

```bash
dotnet build src/PublicApi/PublicApi.csproj -c Release
```

Expected: Build succeeds with no errors (warnings about System.Text.Json are expected).

## Step 2: Verify Database Migrations

The migration `AddMaxioCustomerAndUserFields` adds the following columns to the `AspNetUsers` table:
- `FirstName` (nullable string)
- `LastName` (nullable string)
- `MaxioCustomerId` (nullable int)

These are applied automatically on first app startup (via the in-memory database or on actual database migrations).

## Step 3: Start the PublicApi Service

Run the PublicApi with the Maxio environment variables:

```bash
cd src/PublicApi
$env:DOTNET_ROLL_FORWARD="Major"
dotnet run --configuration Release
```

Expected: App starts, logs "LAUNCHING PublicApi", and Swagger UI is available at `https://localhost:27863/swagger`.

## Step 4: Authenticate via JWT

The subscription endpoints require JWT bearer authentication. First, get a token from the authenticate endpoint:

```bash
# Get a JWT token
$response = Invoke-WebRequest -Uri "https://localhost:27863/api/users/authenticate" `
  -Method POST `
  -Headers @{"Content-Type" = "application/json"} `
  -Body '{"username":"demouser@microsoft.com", "password":"Pass@word1"}' `
  -SkipCertificateCheck

$token = ($response.Content | ConvertFrom-Json).token
$headers = @{Authorization = "Bearer $token"}
```

Or use the Swagger UI "Authorize" button:
- Click the padlock icon in the top right
- Set the token you received from the authenticate endpoint

## Step 5: List Available Subscription Plans

Test the plan-listing endpoint:

```bash
Invoke-WebRequest -Uri "https://localhost:27863/api/subscription-plans" `
  -Headers $headers `
  -SkipCertificateCheck | Select-Object -ExpandProperty Content | ConvertFrom-Json
```

Expected output:
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    ...
  ]
}
```

## Step 6: Create a Test User and Subscribe

Subscribe the authenticated user to a plan:

```bash
$subscribeBody = @{
  productHandle = "eshop-pro"
} | ConvertTo-Json

Invoke-WebRequest -Uri "https://localhost:27863/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -Body $subscribeBody `
  -ContentType "application/json" `
  -SkipCertificateCheck | Select-Object -ExpandProperty Content | ConvertFrom-Json
```

Expected output:
```json
{
  "subscriptionId": 123456,
  "customerId": 789,
  "state": "active",
  "productName": "Pro Plan",
  "productHandle": "eshop-pro",
  "priceInCents": 29900,
  "nextBillingDate": "2025-10-07T00:00:00+00:00"
}
```

The user's `MaxioCustomerId` is now stored in the database, allowing future subscriptions without re-creating the customer.

## Step 7: List User Subscriptions

Fetch the authenticated user's subscriptions:

```bash
Invoke-WebRequest -Uri "https://localhost:27863/api/my-subscriptions" `
  -Headers $headers `
  -SkipCertificateCheck | Select-Object -ExpandProperty Content | ConvertFrom-Json
```

Expected output:
```json
{
  "subscriptions": [
    {
      "subscriptionId": 123456,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "priceInCents": 29900,
      "nextBillingDate": "2025-10-07T00:00:00+00:00",
      "createdAt": "2025-09-07T12:34:56+00:00"
    }
  ]
}
```

## Step 8: Verify Idempotency

Subscribe the same user to the same plan again. The operation should succeed and return the same subscription (or fail with a 422 if Maxio enforces uniqueness on the reference key).

This verifies that customer creation is idempotent — the second call finds the existing customer by reference instead of creating a duplicate.

## Step 9: Verify Error Handling

Try subscribing to a non-existent plan:

```bash
$subscribeBody = @{
  productHandle = "nonexistent-plan"
} | ConvertTo-Json

Invoke-WebRequest -Uri "https://localhost:27863/api/subscriptions" `
  -Method POST `
  -Headers $headers `
  -Body $subscribeBody `
  -ContentType "application/json" `
  -SkipCertificateCheck
```

Expected: HTTP 400 with an error message (or HTTP 422 if Maxio returns a validation error).

## Architecture Overview

### Components

- **PublicApi.csproj** — ASP.NET Core 8.0 minimal API host; exposes `/api/subscription-*` endpoints
- **MaxioSubscriptionService** — Service layer wrapping Maxio SDK calls; handles customer/subscription operations
- **MaxioSettings** — Configuration model for Maxio credentials and options
- **SubscriptionEndpoints/** — Three endpoint implementations:
  - `ListSubscriptionPlansEndpoint` — GET /api/subscription-plans
  - `CreateSubscriptionEndpoint` — POST /api/subscriptions
  - `ListUserSubscriptionsEndpoint` — GET /api/my-subscriptions

### Data Flow

1. **Authenticate** → Get JWT token from `/api/users/authenticate`
2. **List Plans** → Call Maxio `ListProducts` via SDK
3. **Subscribe**:
   - Ensure Maxio customer exists for the user (by reference, keyed on user ID)
   - Store customer ID in user's `MaxioCustomerId` field
   - Call Maxio `CreateSubscription` with customer ID and plan handle
   - Return subscription state to caller
4. **List Subscriptions** → Call Maxio `ListCustomerSubscriptions` for the user's customer ID

### Error Handling

- **401 Unauthorized** — JWT token missing or invalid
- **400 Bad Request** — Missing required fields (e.g., productHandle)
- **422 Unprocessable Entity** — Maxio validation error (e.g., duplicate reference)
- **500 Internal Server Error** — Maxio API unreachable or malformed response

### Database Changes

Migration `AddMaxioCustomerAndUserFields` adds three nullable columns to `AspNetUsers`:
- `FirstName` — User's first name (used when creating Maxio customer)
- `LastName` — User's last name (used when creating Maxio customer)
- `MaxioCustomerId` — Maxio's internal customer ID (stored to avoid re-creating customers)

When using an in-memory database, these columns exist only for the current app session and are lost on restart.

## Troubleshooting

### "Access to the path 'Microsoft.Web.LibraryManager.Build.dll' is denied"
This is a pre-existing issue with the build system. Build the PublicApi project specifically instead of the whole solution:
```bash
dotnet build src/PublicApi/PublicApi.csproj
```

### "Maxio credentials not found"
Ensure environment variables are set:
```bash
$env:MAXIO_API_KEY = "..."
$env:MAXIO_SITE_SUBDOMAIN = "..."
$env:MAXIO_ENVIRONMENT = "US"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "..."
```

### "HTTPS dev certificate error"
Ensure the certificate is installed and trusted:
```bash
dotnet dev-certs https --check
dotnet dev-certs https --trust  # If check fails
```

### "JWT token invalid"
Tokens are signed with a hardcoded key in `AuthorizationConstants.JWT_SECRET_KEY`. Ensure the same key is used for both generating and validating tokens. Tokens are time-independent in this implementation (no expiry).

## Next Steps

1. **Database Persistence** — Replace in-memory database with SQL Server or another persistent store to retain user subscriptions across restarts.
2. **Webhook Integration** — Add a webhook endpoint to receive Maxio events (subscription state changes, billing events).
3. **Subscription Management** — Extend endpoints to support canceling, updating, or resuming subscriptions.
4. **Billing Portal** — Link users to Maxio's billing portal for payment method management and invoice history.
5. **Testing** — Add integration tests using the PublicApiIntegrationTests project.

## Notes

- **No payment method required** — The sandbox plans (`eshop-pro`, `basic-plan`) are configured without requiring payment information, so subscriptions succeed immediately.
- **Idempotency** — Customer creation uses the eShopOnWeb user ID as the Maxio reference. Repeated calls with the same user ID will find the existing customer (or fail if Maxio enforces duplicate-reference rules).
- **Production Readiness** — This integration is production-grade in structure (error handling, logging, DI) but requires:
  - Real database for persistence
  - HTTPS certificate for prod
  - Monitoring/alerting on Maxio API errors
  - Secrets management (not hardcoded credentials)
