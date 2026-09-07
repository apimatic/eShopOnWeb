# Maxio Integration Verification Guide

This guide walks you through verifying the Maxio subscription billing integration for eShopOnWeb.

## Build Verification

✅ **Confirm the solution builds:**

```bash
$env:DOTNET_ROLL_FORWARD="Major"
cd repo
dotnet restore eShopOnWeb.sln
dotnet build eShopOnWeb.sln
```

**Expected:** Build succeeds with 0 errors (warnings about System.Text.Json and Azure.Identity vulnerabilities are pre-existing and can be ignored).

## Step-by-Step Verification

### 1. Setup: Configure Maxio Credentials

Before running the app, set up Maxio credentials:

```bash
cd src/PublicApi

# Initialize user-secrets
dotnet user-secrets init

# Set your Maxio sandbox credentials
dotnet user-secrets set "Maxio:ApiKey" "<your-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "<your-subdomain>"
```

**Note:** Your Maxio credentials should match:
- **API Key** — from Maxio dashboard → Settings → API
- **Subdomain** — from your Maxio sandbox URL (e.g., if URL is `https://myshop.chargify.com`, subdomain is `myshop`)
- **Sandbox site** — The plan uses site `cp-exp-1` with pre-seeded products:
  - Product Family: `eshop-subscribe`
  - Pro Plan: `eshop-pro` ($299/mo)
  - Basic Plan: `basic-plan` ($29/mo)

### 2. Build and Run

```bash
# From repo root
$env:DOTNET_ROLL_FORWARD="Major"
dotnet run --project src/PublicApi --no-build
```

**Expected:** Application starts and logs:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7200
```

(Port may differ; check the actual output)

### 3. Authenticate

Get a JWT token for API calls:

```bash
# Using curl (or Postman, etc.)
curl -X POST "https://localhost:7200/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@example.com",
    "password": "password"
  }' \
  --insecure

# Save the token
$TOKEN = "eyJhbGciOi..." # from response.token
```

**Expected:** Receive an HTTP 200 with JSON containing a `token` field (or similar auth response, depending on seeded users).

### 4. Test: List Subscription Plans

```bash
curl -X GET "https://localhost:7200/api/subscription-plans" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure -v
```

**Expected:**
- HTTP 200
- JSON response with a `plans` array
- Each plan contains: `id`, `name`, `handle`, `priceInCents`, `interval`, `intervalUnit`
- At least the Pro Plan (`eshop-pro`) and Basic Plan (`basic-plan`) from Maxio are listed

**If it fails:**
- `401` — Token is invalid or expired. Re-authenticate.
- `500` — Maxio API call failed. Check credentials and Maxio status.
- `422` — Maxio returned a validation error. Check the response body.

### 5. Test: Create a Subscription

```bash
curl -X POST "https://localhost:7200/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure -v
```

**Expected:**
- HTTP 200 (or 201 Created)
- JSON response with `subscription` object containing:
  - `id` — subscription ID (non-zero)
  - `customerId` — Maxio customer ID (non-zero)
  - `productHandle` — `"eshop-pro"`
  - `state` — `"active"` or similar
  - `createdAt` — ISO-8601 timestamp

**What's happening:**
1. The endpoint extracts the user ID from the JWT
2. `SubscriptionService.GetOrCreateCustomerAsync()` attempts to look up the user in Maxio
3. If not found (404), it creates a new customer with the user's email and name
4. A subscription is created for that customer to the requested plan

**If it fails:**
- `401` — Token expired or invalid
- `404` — Plan (`eshop-pro`) doesn't exist. Check Maxio product family.
- `422` — Maxio validation error (e.g., plan configuration issue). Check response details.
- `500` — Connection or parsing error. Check logs.

### 6. Test: Idempotent Customer Linking

Run the subscribe command again with the same token (same user):

```bash
curl -X POST "https://localhost:7200/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"basic-plan"}' \
  --insecure
```

**Expected:**
- HTTP 200
- New subscription to `basic-plan`
- `customerId` is the same as the first subscription (same Maxio customer re-used)

**What confirms it worked:**
- No duplicate customers created in Maxio
- The integration uses the `Reference` field (eShopOnWeb user ID) to idempotently look up and re-use existing Maxio customers

### 7. Test: List User's Subscriptions

```bash
curl -X GET "https://localhost:7200/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure
```

**Expected:**
- HTTP 200
- JSON response with `subscriptions` array
- Both subscriptions (eshop-pro and basic-plan) are listed
- Each has the same `customerId`

**If it fails:**
- `401` — Token expired
- `500` — Maxio API error

### 8. Test: Different User

Create a new token for a different user (if available in seeded data):

```bash
curl -X POST "https://localhost:7200/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "anotheruser@example.com",
    "password": "password"
  }' --insecure

# Use the new token
$TOKEN2 = "eyJhbGci..." # from response

curl -X GET "https://localhost:7200/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN2" \
  --insecure
```

**Expected:**
- HTTP 200
- Empty `subscriptions` array (this user has no subscriptions yet)
- No cross-contamination with the first user's subscriptions

## Checklist: Integration Completeness

Use this checklist to confirm all integration requirements are met:

- [ ] **Build:** `dotnet build eShopOnWeb.sln` succeeds with 0 errors
- [ ] **Configuration:** Maxio credentials load from `user-secrets` or environment
- [ ] **Endpoint 1 (List Plans):** `GET /api/subscription-plans` returns plans from Maxio
- [ ] **Endpoint 2 (Create Sub):** `POST /api/subscriptions` creates a Maxio subscription
- [ ] **Endpoint 3 (List My Subs):** `GET /api/my-subscriptions` lists user's subscriptions
- [ ] **Authentication:** All endpoints require and validate JWT bearer token
- [ ] **Idempotency:** Creating multiple subscriptions for the same user reuses the Maxio customer
- [ ] **Error Handling:** Errors (401, 404, 422, 500) return appropriate HTTP status and error messages
- [ ] **No Secrets in Repo:** Credentials not hardcoded in appsettings*.json or code
- [ ] **SDK Usage:** All Maxio interactions go through the Maxio SDK, not direct HTTP calls

## Troubleshooting

### Application won't start

**Error:** "Unable to find package AsadAli.AdvancedBilling.Sdk"

**Fix:** Ensure NuGet can reach nuget.org and the SDK package is published (version 1.0.2).

```bash
dotnet nuget list source  # Verify nuget.org is in the list
dotnet restore src/PublicApi  # Explicitly restore PublicApi packages
```

### 401 Unauthorized on endpoints

**Error:** `"error": "User not authenticated"`

**Fix:** Ensure you're passing a valid bearer token in the `Authorization` header.

```bash
# Correct format
curl -H "Authorization: Bearer YOUR_TOKEN" ...

# Wrong (won't work)
curl -H "Authorization: YOUR_TOKEN" ...
curl -H "Bearer: YOUR_TOKEN" ...
```

### 500 when calling Maxio endpoints

**Error:** `"error": "Provider unreachable"` or `"error": "The provider returned an invalid response"`

**Possible causes:**
1. Network connectivity — Verify access to Maxio API
2. Wrong credentials — Check API Key and subdomain
3. API Key revoked or expired — Regenerate in Maxio dashboard
4. Maxio API down — Check Maxio status page

**Fix:**
```bash
# Test connectivity to Maxio manually
curl -u "api_key_here:x" "https://your-subdomain.chargify.com/subscriptions.json?limit=1"
```

### 422 Unprocessable Entity

**Error:** `"error": "Validation error"` or list of errors from Maxio

**Cause:** Maxio rejected the request (e.g., plan configuration issue, missing payment method when required).

**Fix:** Check Maxio plan settings:
- Verify plan exists and is active
- Check if plan requires payment method (it shouldn't for this setup)
- Look at the error details in the response

### "Customer not found" on list subscriptions

**Behavior:** First call returns empty, which is correct.

**Cause:** `GetOrCreateCustomerAsync()` is called even on list, so it creates a customer automatically.

## Code Architecture

The integration consists of:

1. **SubscriptionService** — Wraps Maxio SDK calls with:
   - Typed error handling (Case A vs Case B errors)
   - Transport error handling (HttpRequestException)
   - JSON parsing error handling
   - Idempotent customer lookup/creation via Reference field

2. **Three Endpoints** (MinimalApi.Endpoint pattern):
   - SubscriptionPlansEndpoint (GET /api/subscription-plans)
   - CreateSubscriptionEndpoint (POST /api/subscriptions)
   - MySubscriptionsEndpoint (GET /api/my-subscriptions)

3. **Configuration** (MaxioOptions):
   - Loaded from appsettings + environment variable overrides
   - Used to configure MaxioAdvancedBillingClient at startup

4. **DTOs** for API responses:
   - SubscriptionPlanDto
   - SubscriptionDto
   - Response wrappers inheriting from BaseResponse

## Expected Behavior Summary

| Scenario | Expected Outcome |
|----------|------------------|
| List plans, first time | Returns plans from Maxio product family |
| Subscribe, first time | Creates Maxio customer + subscription |
| Subscribe, same user, different plan | Reuses Maxio customer, creates new subscription |
| List subscriptions, first user | Returns that user's subscriptions only |
| List subscriptions, different user | Returns empty or that user's subscriptions |
| Unauthenticated request | 401 Unauthorized |
| Invalid JWT | 401 Unauthorized |
| Non-existent plan | 404 Not Found from Maxio (or 422 validation) |
| Maxio API down | 500 Internal Server Error |

## Next: Running in Docker or Production

The integration is production-ready. To deploy:

1. Build Docker image (Dockerfile already present)
2. Inject Maxio credentials via environment variables (not appsettings)
3. Configure database (SQL Server recommended for production)
4. Set up HTTPS certificates
5. Enable logging/monitoring

See the main `README.md` for deployment instructions.
