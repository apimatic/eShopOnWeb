# Testing the Subscription Integration

This document provides step-by-step instructions to manually test the Maxio subscription integration.

## Prerequisites

1. **Environment Setup**
   - Maxio credentials are set as environment variables (MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, etc.)
   - User-secrets are configured with Maxio settings
   - .NET 8 or higher SDK is installed

2. **Start the PublicApi**
   ```bash
   cd src/PublicApi
   dotnet run
   # Server will be available at https://localhost:28523
   ```

## Test Sequence

### Test 1: List Subscription Plans

**Endpoint**: `GET https://localhost:28523/api/subscription-plans`

**Command**:
```bash
curl -k https://localhost:28523/api/subscription-plans
```

**Expected Response** (200 OK):
```json
{
  "plans": [
    {
      "id": 7130995,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": null,
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month",
      "taxable": false
    },
    {
      "id": 7130996,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": null,
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month",
      "taxable": false
    }
  ]
}
```

**Verification**:
- ✅ Response code is 200
- ✅ At least 2 plans are returned
- ✅ Each plan has name, handle, priceInCents
- ✅ Prices are positive integers (in cents)

### Test 2: Authenticate User

**Endpoint**: `POST https://localhost:28523/api/authenticate`

**Command**:
```bash
curl -k -X POST https://localhost:28523/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }'
```

**Expected Response** (200 OK):
```json
{
  "result": true,
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "username": "demouser@microsoft.com",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

**Extract Token**:
```bash
TOKEN=$(curl -s -k -X POST https://localhost:28523/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' \
  | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
echo "Token: $TOKEN"
```

**Verification**:
- ✅ Response code is 200
- ✅ result is true
- ✅ token field is present and not empty
- ✅ Token is a valid JWT (three parts separated by dots)

### Test 3: Create Subscription (Authentication Required)

**Endpoint**: `POST https://localhost:28523/api/subscriptions`

**Prerequisites**:
- Must have a valid JWT token from Test 2

**Command**:
```bash
TOKEN="<token-from-test-2>"
curl -k -X POST https://localhost:28523/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "productHandle": "eshop-pro"
  }'
```

**Expected Response** (201 Created):
```json
{
  "subscriptionId": 123456789,
  "customerId": 98765,
  "state": "active",
  "productHandle": "eshop-pro",
  "productName": "Pro Plan",
  "priceInCents": 29900,
  "intervalUnit": "month",
  "interval": 1,
  "activatedAt": "2026-09-07T12:34:56Z",
  "nextAssessmentAt": "2026-10-07T12:34:56Z",
  "currentPeriodEndsAt": "2026-10-07T12:34:56Z"
}
```

**Verification**:
- ✅ Response code is 201
- ✅ subscriptionId is present and is a number
- ✅ customerId is present and is a number
- ✅ state is "active"
- ✅ productHandle matches the request
- ✅ nextAssessmentAt is set (next billing date)
- ✅ Local database has a Subscription record for this user

**Notes on Idempotency**:
- If you call this endpoint twice with the same productHandle and user:
  - A NEW subscription is created each time (different subscriptionId)
  - The SAME Maxio customer is reused (found by reference lookup)
  - This is idempotent at the customer level (never creates duplicate customers)

### Test 4: Retrieve User's Subscriptions (Authentication Required)

**Endpoint**: `GET https://localhost:28523/api/my-subscriptions`

**Prerequisites**:
- Must have a valid JWT token
- Must have created at least one subscription

**Command**:
```bash
TOKEN="<token-from-test-2>"
curl -k https://localhost:28523/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN"
```

**Expected Response** (200 OK):
```json
{
  "subscriptions": [
    {
      "subscriptionId": 123456789,
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "productId": 7130995,
      "state": "active",
      "priceInCents": 29900,
      "intervalUnit": "month",
      "interval": 1,
      "activatedAt": "2026-09-07T12:34:56Z",
      "nextAssessmentAt": "2026-10-07T12:34:56Z"
    }
  ]
}
```

**Verification**:
- ✅ Response code is 200
- ✅ subscriptions array contains at least one entry
- ✅ subscription IDs match those from Test 3
- ✅ state is "active" (or another valid state)
- ✅ Product details are populated from Maxio

### Test 5: Test Authentication Required

**Endpoint**: `POST https://localhost:28523/api/subscriptions` (no token)

**Command**:
```bash
curl -k -X POST https://localhost:28523/api/subscriptions \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}'
```

**Expected Response** (401 Unauthorized)

**Verification**:
- ✅ Response code is 401
- ✅ No subscription is created

### Test 6: Test Multiple Subscriptions for Same User

**Endpoint**: `POST https://localhost:28523/api/subscriptions`

**Prerequisites**:
- Must have created at least one subscription (Test 3)

**Command** (subscribe to different plan):
```bash
TOKEN="<token-from-test-2>"
curl -k -X POST https://localhost:28523/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "productHandle": "basic-plan"
  }'
```

**Expected Response** (201 Created)

**Then verify with Test 4**:
```bash
TOKEN="<token-from-test-2>"
curl -k https://localhost:28523/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN"
```

**Verification**:
- ✅ User now has 2 subscriptions
- ✅ Different subscriptionIds
- ✅ Different productHandles ("eshop-pro" and "basic-plan")
- ✅ Different prices (29900 vs 2900)

### Test 7: Test Different User

**Prerequisites**:
- Admin user should also exist (admin@microsoft.com)

**Command** (get token for admin):
```bash
curl -s -k -X POST https://localhost:28523/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' \
  | grep -o '"token":"[^"]*"' | cut -d'"' -f4
```

**Create subscription as admin**:
```bash
ADMIN_TOKEN="<admin-token>"
curl -k -X POST https://localhost:28523/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -d '{"productHandle": "eshop-pro"}'
```

**Verify admin's subscriptions**:
```bash
ADMIN_TOKEN="<admin-token>"
curl -k https://localhost:28523/api/my-subscriptions \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

**Verification**:
- ✅ Admin can create subscriptions
- ✅ Admin's subscriptions are separate from demouser's
- ✅ Each user only sees their own subscriptions

## Debugging

### Check Logs
When running `dotnet run`, watch the console for:
- `"Failed to fetch Maxio..."` - API communication issues
- `"Failed to create subscription"` - Subscription creation failed
- `"Successfully created Maxio customer"` - Customer created in Maxio

### Verify User-Secrets
```bash
cd src/PublicApi
dotnet user-secrets list
```

Should show:
```
Maxio:ApiKey = <value>
Maxio:ProductFamilyHandle = eshop-subscribe
Maxio:Subdomain = cp-exp-2
```

### Check Database Records
- With in-memory database: no persistence between runs
- With SQL Server: query the `Subscriptions` table
  ```sql
  SELECT UserId, MaxioCustomerId, MaxioSubscriptionId, State FROM Subscriptions
  ```

## Common Issues

### 401 Unauthorized on Create/List Subscriptions
- **Cause**: Invalid or missing JWT token
- **Fix**: Get a fresh token from Test 2, ensure "Bearer " prefix in header

### No plans returned from GET /api/subscription-plans
- **Cause**: Maxio API call failing
- **Check**: Logs for "Failed to list Maxio products"
- **Fix**: Verify MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN environment variables

### 400 Bad Request when creating subscription
- **Cause**: Maxio subscription creation failed
- **Check**: Logs for error details
- **Common reasons**:
  - Invalid productHandle
  - Maxio API rate limit
  - Invalid customer data

### Database errors (SQL Server)
- **Cause**: Migration not applied
- **Fix**: Run `dotnet ef database update -p src/Infrastructure/Infrastructure.csproj -s src/PublicApi/PublicApi.csproj --context CatalogContext`

## Performance Notes

- First request to `/api/subscription-plans` will hit Maxio API (~200-500ms)
- Customer creation on first subscription (~300-600ms)
- Subsequent requests are fast (local DB queries)

## Security Notes

- All endpoints except `/api/subscription-plans` require JWT authentication
- `/api/my-subscriptions` only returns the authenticated user's subscriptions
- API keys are never logged or exposed in responses
- HTTPS is required in production (enabled by default)
