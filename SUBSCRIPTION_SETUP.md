# eShopOnWeb Maxio Subscription Integration — Setup & Verification

## What was built

A recurring subscription capability using Maxio Advanced Billing with three HTTP endpoints:

- `GET /api/subscription-plans` — List available plans
- `POST /api/subscriptions` — Subscribe user to a plan
- `GET /api/my-subscriptions` — List user's subscriptions

All endpoints require JWT authentication (bearer token).

## Setup Instructions

### 1. Configure Maxio Credentials (User Secrets)

You **must** provide Maxio API credentials via environment variables or .NET User Secrets. Credentials are never stored in source code.

#### Option A: Environment Variables (for CI/deployment)

Set these environment variables before running:

```bash
# PowerShell:
$env:MAXIO_API_KEY = "your-api-key-here"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-1"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"

# Or add to .env file (not committed) and load before running
```

#### Option B: .NET User Secrets (recommended for dev)

From the `src/PublicApi` directory:

```bash
cd src/PublicApi

# Initialize user secrets for the project (one-time)
dotnet user-secrets init

# Set the credentials
dotnet user-secrets set "Maxio:ApiKey" "your-api-key-here"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-1"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Verify they're set
dotnet user-secrets list
```

### 2. Build & Run

```bash
cd <repo-root>

# Build (with .NET 10 SDK, or let it roll forward from 8.0)
$env:DOTNET_ROLL_FORWARD='Major'
dotnet build eShopOnWeb.sln -c Release

# Run PublicApi (default: https://localhost:24503)
dotnet run --project src/PublicApi/PublicApi.csproj --configuration Release
```

The PublicApi will start on `https://localhost:24503`.

### 3. In-Memory Database

By default, the app uses an in-memory database. User/subscription mappings are lost on restart. To persist, configure a real database in `appsettings.json` or set `UseOnlyInMemoryDatabase=false`.

---

## Verification Workflow

### Step 1: Get a JWT Token

Authenticate as a test user:

```bash
curl -X POST https://localhost:24503/api/authenticate \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }'

# Response:
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "username": "demouser@microsoft.com",
  ...
}
```

Save the `token` value for the next requests.

### Step 2: List Available Plans

```bash
TOKEN="<paste-token-from-step-1>"

curl -X GET https://localhost:24503/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  --insecure

# Response (example):
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional subscription",
      "price": 299.00,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription",
      "price": 29.00,
      "interval": 1,
      "intervalUnit": "month"
    }
  ]
}
```

### Step 3: Subscribe to a Plan

```bash
TOKEN="<paste-token>"

curl -X POST https://localhost:24503/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  --insecure \
  -d '{
    "planHandle": "eshop-pro"
  }'

# Response (example):
{
  "subscriptionId": 12345678,
  "state": "active",
  "planHandle": "eshop-pro",
  "price": 299.00,
  "nextBillingDate": "2026-10-06T00:00:00Z",
  "activatedAt": "2026-09-06T12:34:56Z"
}
```

**Note:** Subscribing the same user twice with the same plan is idempotent — it updates the existing subscription, not creates a duplicate (via the `Reference` field).

### Step 4: List Your Subscriptions

```bash
TOKEN="<paste-token>"

curl -X GET https://localhost:24503/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  --insecure

# Response (example):
{
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "planHandle": "eshop-pro",
      "price": 299.00,
      "nextBillingDate": "2026-10-06T00:00:00Z",
      "activatedAt": "2026-09-06T12:34:56Z"
    }
  ]
}
```

---

## Architecture Notes

**Layering:**

- **Endpoints** (`SubscriptionEndpoints/`) — HTTP handlers, JWT auth, request/response DTOs
- **Service** (`MaxioSubscriptionService.cs`) — Maxio SDK orchestration, idempotency, error handling
- **Configuration** (`MaxioSettings.cs`) — Credentials & options binding from `appsettings.json` + environment

**Idempotency:**

- **Customers:** Created once per user (identified by user ID as `Reference`). Subsequent calls return the existing customer.
- **Subscriptions:** Each subscription has a user-scoped `Reference`. Re-subscribing with the same plan is safe.

**Error Handling:**

- SDK errors (non-2xx from Maxio) are caught and logged.
- 404 on `ReadCustomerByReference` is expected and triggers customer creation.
- 422 errors (validation) on subscription create surface as `BadRequest`.

**No New Infrastructure:**

- No Docker, no broker, no database requirements (in-memory default).
- Uses the project's existing dependency-injection, logging, and auth.

---

## Troubleshooting

**"API key not found" error:**
- Ensure `MAXIO_API_KEY` is set in environment variables or user secrets.
- Check `appsettings.json` — `Maxio:ApiKey` should be empty (values loaded from env/secrets).

**Subscription endpoint returns 422:**
- Check Maxio sandbox status (site `cp-exp-1` must be active).
- Confirm plan handles exist: `eshop-pro`, `basic-plan`.
- Verify the plan is not archived or removed.

**No subscriptions returned:**
- Ensure you subscribed with the same user ID (from JWT claim).
- In-memory database loses data on app restart — re-subscribe after restarting.

**HTTPS/certificate errors:**
- Run `dotnet dev-certs https --check` to verify the dev cert is installed & trusted.
- Add `--insecure` to curl commands (as shown above) to skip cert validation in dev.

---

## Next Steps

- **Persist data:** Configure a real SQL database in `appsettings.json`.
- **Webhooks:** Integrate Maxio webhook events (subscription state changes, billing updates).
- **UI:** Add subscription UI to the Blazor web frontend (`src/Web`, `src/BlazorAdmin`).
- **Billing portal:** Embed Maxio's hosted billing portal for plan upgrades/downgrades.
