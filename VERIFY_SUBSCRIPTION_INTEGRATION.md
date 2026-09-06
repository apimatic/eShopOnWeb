# Verification Guide: Maxio Subscription Integration

This guide walks through verifying the subscription billing integration with step-by-step instructions.

## Quick Start Verification (5 minutes)

### 1. Build the Solution

```powershell
cd C:\path\to\repo
dotnet build src/PublicApi/PublicApi.csproj -c Debug
```

✓ **Expected**: Build completes with no errors (warnings are OK)

### 2. Configure Environment & Run

```powershell
# Set environment for in-memory database
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"

# Set Maxio credentials from your sandbox
$env:MAXIO_API_KEY = "your-sandbox-api-key"
$env:MAXIO_SITE_SUBDOMAIN = "your-sandbox-name"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"

# OR use the setup script
.\setup-maxio-secrets.ps1

# Run PublicApi
cd src/PublicApi
dotnet run
```

✓ **Expected**: Application starts, logs show:
- "PublicApi App created..."
- "Seeding Database..."
- "LAUNCHING PublicApi"
- Server listening on port from appsettings.json (e.g., https://localhost:25603)

### 3. Access Swagger UI

Open browser to: `https://localhost:25603/swagger`

✓ **Expected**: Swagger UI displays with three new endpoints under "SubscriptionEndpoints":
- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

## Detailed Verification Steps

### Phase 1: Authentication

**Objective**: Verify JWT token generation includes user ID

#### Step 1a: Authenticate via Swagger

1. In Swagger, find the `POST /api/authenticate` endpoint (under AuthEndpoints)
2. Click "Try it out"
3. Use demo credentials:
   - username: `demouser@example.com`
   - password: `Pass@word1`
4. Click "Execute"

✓ **Expected Response**:
```json
{
  "result": true,
  "username": "demouser@example.com",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false,
  "correlationId": "..."
}
```

#### Step 1b: Verify Token Contains User ID

Decode the JWT token (use https://jwt.io in a separate tab):

✓ **Expected Payload**:
```json
{
  "unique_name": "demouser@example.com",
  "nameid": "<user-id-guid>",
  "aud": "http://localhost",
  "exp": <timestamp>,
  ...
}
```

The `nameid` (NameIdentifier claim) is the user ID extracted from claims in endpoints.

### Phase 2: Subscription Plans

**Objective**: Verify plans are retrieved from Maxio product family

#### Step 2a: List Plans

1. In Swagger, find `GET /api/subscription-plans`
2. Click "Try it out"
3. Click "Execute"
4. No authentication needed for this endpoint

✓ **Expected Response** (200 OK):
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Pro Plan",
      "handle": "eshop-pro",
      "description": "...",
      "price": 299.00,
      "billingIntervalDays": 30,
      "billingInterval": "month",
      "requiresCreditCard": false
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "description": "...",
      "price": 29.00,
      "billingIntervalDays": 30,
      "billingInterval": "month",
      "requiresCreditCard": false
    }
  ]
}
```

✓ **Verification Checklist**:
- [ ] At least 2 plans returned (Pro and Basic)
- [ ] Plans have valid handles (matching Maxio)
- [ ] Prices are in dollars (not cents)
- [ ] Billing interval is "month" with 30 days
- [ ] `requiresCreditCard` is false (as per Maxio sandbox config)

### Phase 3: Create Subscription

**Objective**: Verify subscription creation with idempotent customer handling

#### Step 3a: Create Subscription (First Time)

1. Click the lock icon next to `POST /api/subscriptions`
2. In the "Available authorizations" dialog, paste the JWT token from Step 1
3. Click "Authorize"
4. Find `POST /api/subscriptions` endpoint
5. Click "Try it out"
6. In the Request body, enter:
   ```json
   {
     "productHandle": "eshop-pro"
   }
   ```
7. Click "Execute"

✓ **Expected Response** (201 Created):
```json
{
  "id": 15236915,
  "state": "active",
  "productName": "Pro Plan",
  "productHandle": "eshop-pro",
  "pricePerBillingCycle": 299.00,
  "billingIntervalDays": 30,
  "billingInterval": "month",
  "currentPeriodEndsAt": "2024-10-14T14:48:10-05:00",
  "nextAssessmentAt": "2024-10-14T14:48:10-05:00",
  "activatedAt": "2024-09-14T14:48:12-05:00",
  "createdAt": "2024-09-14T14:48:10-05:00",
  "correlationId": "..."
}
```

✓ **Verification Checklist**:
- [ ] Response status is 201 Created (not 200)
- [ ] Subscription ID is returned (integer > 0)
- [ ] State is "active"
- [ ] Product details match the requested plan
- [ ] Price is 299.00 (Pro plan price)
- [ ] Next billing date is approximately 30 days from now
- [ ] Location header points to `/api/subscriptions/{id}`

#### Step 3b: Idempotency Test (Create Again)

Repeat Step 3a with the same JWT token and same product handle.

✓ **Expected Behavior**:
- Response status is 201 Created again
- New subscription ID is generated
- Different subscription from Step 3a (but for same user/product)
- NO duplicate customers created in Maxio

**Note**: Maxio allows multiple subscriptions per customer. Each subscription is separate even if for the same plan.

#### Step 3c: Create Subscription with Different Plan

1. Repeat Step 3a but use `"productHandle": "basic-plan"`

✓ **Expected Response**:
- New subscription with basic-plan details
- Price is 29.00 (Basic plan price)
- Same customer (no duplicate created)

### Phase 4: List My Subscriptions

**Objective**: Verify user sees all their subscriptions

#### Step 4a: List Subscriptions (With Token)

1. Make sure you're still authorized (token is still valid)
2. Find `GET /api/my-subscriptions`
3. Click "Try it out"
4. Click "Execute"

✓ **Expected Response** (200 OK):
```json
{
  "subscriptions": [
    {
      "id": 15236915,
      "state": "active",
      "productName": "Pro Plan",
      "productHandle": "eshop-pro",
      "pricePerBillingCycle": 299.00,
      "billingIntervalDays": 30,
      "billingInterval": "month",
      "currentPeriodEndsAt": "2024-10-14T14:48:10-05:00",
      "nextAssessmentAt": "2024-10-14T14:48:10-05:00",
      "activatedAt": "2024-09-14T14:48:12-05:00",
      "createdAt": "2024-09-14T14:48:10-05:00"
    },
    {
      "id": 15236916,
      "state": "active",
      "productName": "Basic Plan",
      "productHandle": "basic-plan",
      "pricePerBillingCycle": 29.00,
      ...
    }
  ]
}
```

✓ **Verification Checklist**:
- [ ] Both subscriptions from Phase 3 are listed
- [ ] Subscriptions include all details (state, pricing, dates)
- [ ] Count matches expected subscriptions for this user
- [ ] All dates are formatted consistently (ISO 8601)

#### Step 4b: List Subscriptions (Without Token)

1. Click the lock icon and remove the authorization
2. Click "Execute" on `GET /api/my-subscriptions` again

✓ **Expected Response** (401 Unauthorized):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Unauthorized",
  ...
}
```

### Phase 5: Error Handling

**Objective**: Verify proper error responses

#### Step 5a: Invalid Plan Handle

1. Authorize with token
2. Try to create subscription with invalid handle:
   ```json
   {
     "productHandle": "non-existent-plan"
   }
   ```

✓ **Expected Response** (400 Bad Request):
- Clear error message from Maxio

#### Step 5b: Expired/Invalid Token

1. Remove authorization or use malformed token
2. Try to access `POST /api/subscriptions`

✓ **Expected Response** (401 Unauthorized)

## Advanced Verification

### Using Test Script

```powershell
.\test-subscription-flow.ps1 -BaseUrl "https://localhost:25603" -ProductHandle "eshop-pro"
```

This script automatically:
1. Authenticates
2. Lists plans
3. Creates a subscription
4. Lists user's subscriptions
5. Verifies each step

### Using curl

```bash
# 1. Authenticate
curl -X POST "https://localhost:25603/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@example.com","password":"Pass@word1"}' \
  --insecure

# Save the token from response

# 2. List plans
curl -X GET "https://localhost:25603/api/subscription-plans" \
  --insecure

# 3. Create subscription
curl -X POST "https://localhost:25603/api/subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  --insecure

# 4. List my subscriptions
curl -X GET "https://localhost:25603/api/my-subscriptions" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  --insecure
```

### Database Verification (In-Memory)

Since in-memory database is used during development:
- Data persists only for the duration of the application run
- Each restart clears all data
- This is expected and by design for development

For production, configure SQL Server connection strings.

## Troubleshooting

### Issue: Cannot Connect to Maxio

**Symptom**: HTTP 500 or timeout when listing plans

**Diagnosis**:
1. Verify Maxio credentials are set correctly
2. Check network connectivity to Maxio sandbox
3. Verify API key has appropriate permissions

**Solution**:
```powershell
# Verify secrets are set
cd src/PublicApi
dotnet user-secrets list

# Check appsettings
cat appsettings.json | grep -A 5 "Maxio"
```

### Issue: 401 Unauthorized on Subscription Endpoints

**Symptom**: Getting 401 when passing JWT token

**Diagnosis**:
1. Token might be expired (valid for 7 days)
2. Token might be malformed
3. Wrong Authorization header format

**Solution**:
1. Verify token format: `Authorization: Bearer <token>` (with space)
2. Decode token at jwt.io to verify it's valid
3. Get new token from authenticate endpoint

### Issue: 404 Not Found for Subscription Endpoints

**Symptom**: Endpoint doesn't appear in Swagger or returns 404

**Diagnosis**:
1. Endpoints might not have been discovered
2. Application might not have restarted with new code

**Solution**:
1. Rebuild: `dotnet build src/PublicApi/PublicApi.csproj`
2. Restart: Stop and run `dotnet run` again
3. Verify endpoints appear in Swagger at startup

### Issue: In-Memory Database Empty

**Symptom**: Authenticated user doesn't exist

**Diagnosis**:
- Application was restarted (in-memory DB is cleared)
- Or demo user wasn't seeded properly

**Solution**:
1. Check application logs for "Seeding Database"
2. Verify `UseOnlyInMemoryDatabase` is set to `true`
3. Restart application

## Checklist: Full Integration Verification

- [ ] **Build**: `dotnet build` succeeds
- [ ] **Run**: Application starts without errors
- [ ] **Swagger**: UI loads with 3 new subscription endpoints
- [ ] **Auth**: Can authenticate and get JWT token
- [ ] **Token**: Token contains user ID in claims
- [ ] **Plans**: GET /subscription-plans returns at least 2 plans
- [ ] **Create**: POST /subscriptions creates subscription successfully
- [ ] **Billing**: Subscription has correct price and billing interval
- [ ] **List**: GET /my-subscriptions returns created subscription
- [ ] **Auth Check**: Endpoints return 401 without token
- [ ] **Idempotency**: Multiple creates don't create duplicate customers
- [ ] **Error Handling**: Invalid inputs return appropriate errors
- [ ] **Test Script**: `test-subscription-flow.ps1` completes successfully

## Next Steps

Once verification is complete:

1. **Code Review**: Review implementation in `src/PublicApi/SubscriptionEndpoints/`
2. **Documentation**: Share SUBSCRIPTION_SETUP.md with team
3. **Production**: Migrate from in-memory to SQL Server database
4. **Security**: Implement rate limiting and audit logging
5. **Monitoring**: Set up alerts for subscription failures

## Support

For issues or questions:
1. Check SUBSCRIPTION_SETUP.md for configuration help
2. Review SUBSCRIPTION_INTEGRATION.md for architecture details
3. Check Maxio API spec at `maxio-spec/openapi.yaml`
4. Review endpoint implementations in `src/PublicApi/SubscriptionEndpoints/`
