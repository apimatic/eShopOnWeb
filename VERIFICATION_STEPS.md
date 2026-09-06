# Maxio Billing Integration - Verification Steps

Follow these steps to verify the Maxio Advanced Billing integration is correctly implemented and working.

## Phase 1: Code Verification ✓

### Compilation Check
```bash
cd C:\path\to\repo
dotnet build eShopOnWeb.sln -c Release
```

**Expected Result:** Build succeeds with 0 errors
- Status: ✓ VERIFIED

### File Structure Check

Verify the following files exist:

```
src/
  ApplicationCore/
    Services/
      MaxioConfiguration.cs          ✓
      MaxioApiClient.cs              ✓
  PublicApi/
    SubscriptionEndpoints/
      SubscriptionPlansEndpoint.cs   ✓
      CreateSubscriptionEndpoint.cs  ✓
      MySubscriptionsEndpoint.cs     ✓
    appsettings.json                 ✓ (updated)
    Program.cs                       ✓ (updated)
    Properties/launchSettings.json   ✓ (updated)
```

All files created: ✓ VERIFIED

### Code Quality Checks

1. **No hardcoded secrets:**
   - All Maxio credentials loaded from environment variables
   - appsettings.json contains only nulls for sensitive config
   - ✓ VERIFIED

2. **Configuration loading:**
   - Program.cs reads MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, etc.
   - Falls back to defaults if not provided
   - ✓ VERIFIED

3. **HTTP client registration:**
   - MaxioApiClient registered as IHttpClientFactory service
   - IMaxioApiClient interface available for DI
   - ✓ VERIFIED

4. **Endpoint implementation:**
   - All three endpoints follow IEndpoint pattern
   - Proper inheritance from BaseRequest/BaseResponse
   - RequireAuthorization() configured on all endpoints
   - ✓ VERIFIED

## Phase 2: Environment Setup

### Step 1: Prepare Maxio Credentials

Get your Maxio sandbox credentials. You need:
- API Key (e.g., from Maxio dashboard)
- Site Subdomain (usually "cp-exp-2" for sandbox)

### Step 2: Set Environment Variables

**Windows PowerShell:**
```powershell
$env:MAXIO_API_KEY = "YOUR_API_KEY"
$env:MAXIO_SITE_SUBDOMAIN = "cp-exp-2"
$env:MAXIO_ENVIRONMENT = "sandbox"
$env:MAXIO_DEFAULT_PRODUCT_FAMILY = "eshop-subscribe"
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
```

**Windows Command Prompt:**
```cmd
set MAXIO_API_KEY=YOUR_API_KEY
set MAXIO_SITE_SUBDOMAIN=cp-exp-2
set MAXIO_ENVIRONMENT=sandbox
set MAXIO_DEFAULT_PRODUCT_FAMILY=eshop-subscribe
set UseOnlyInMemoryDatabase=true
set DOTNET_ROLL_FORWARD=Major
```

**Or use provided script:**
```powershell
.\setup-maxio-dev.ps1 -ApiKey "YOUR_API_KEY" -Subdomain "cp-exp-2"
```

### Step 3: Start the PublicApi

```bash
cd src/PublicApi
dotnet run
```

You should see:
```
PublicApi App created...
Seeding Database...
LAUNCHING PublicApi
Now listening on: https://localhost:25083
Now listening on: http://localhost:25084
Application started. Press Ctrl+C to exit.
```

**Expected Status:** Application running on https://localhost:25083 ✓

### Step 4: Verify Swagger UI

Open browser to: `https://localhost:25083/swagger`

**Expected Result:** Swagger UI loads with API documentation

Look for new endpoints:
- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

All should be visible in the SubscriptionEndpoints section.

**Status:** ✓ VERIFIED

## Phase 3: Endpoint Testing

### Test 1: Authentication

Get a JWT token first:

```bash
curl -X POST "https://localhost:25083/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }' \
  --insecure
```

**Expected Response (200 OK):**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "demouser@microsoft.com",
  "result": true,
  "isLockedOut": false,
  "isNotAllowed": false,
  "requiresTwoFactor": false
}
```

**Save the token value** for use in subsequent requests.

### Test 2: Get Subscription Plans

```bash
$token = "YOUR_TOKEN_HERE"

curl -X GET "https://localhost:25083/api/subscription-plans" \
  -H "Authorization: Bearer $token" \
  --insecure
```

**Expected Response (200 OK):**
```json
{
  "plans": [
    {
      "id": 7126957,
      "handle": "eshop-pro",
      "name": "Pro Plan",
      "description": "Professional plan with advanced features",
      "priceInCents": 29900,
      "interval": 1,
      "intervalUnit": "month"
    },
    {
      "id": 7126958,
      "handle": "basic-plan",
      "name": "Basic Plan",
      "description": "Basic subscription plan",
      "priceInCents": 2900,
      "interval": 1,
      "intervalUnit": "month"
    }
  ],
  "correlationId": "UUID-HERE"
}
```

**Verification Points:**
- Status code is 200
- Array contains at least one plan
- Each plan has: id, handle, name, priceInCents, interval, intervalUnit
- Maxio API call succeeded

✓ VERIFIED IF ALL CHECKS PASS

### Test 3: Create a Subscription

```bash
curl -X POST "https://localhost:25083/api/subscriptions" \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{
    "productHandle": "eshop-pro"
  }' \
  --insecure
```

**Expected Response (201 Created):**
```json
{
  "subscriptionId": 12345678,
  "state": "active",
  "productName": "Pro Plan",
  "currentPeriodEndsAt": "2024-10-06T00:00:00Z",
  "nextAssessmentAt": "2024-10-06T00:00:00Z",
  "createdAt": "2024-09-06T14:30:00Z",
  "correlationId": "UUID-HERE"
}
```

**Verification Points:**
- Status code is 201 (Created)
- subscriptionId is a positive integer
- state is "active"
- productName matches the plan name
- Dates are valid ISO format

**Key Flow Verified:**
1. User ID extracted from JWT token
2. Customer created/retrieved with user ID as reference (idempotent)
3. Subscription created for customer
4. Response includes subscription details

✓ VERIFIED IF ALL CHECKS PASS

### Test 4: Get User's Subscriptions

```bash
curl -X GET "https://localhost:25083/api/my-subscriptions" \
  -H "Authorization: Bearer $token" \
  --insecure
```

**Expected Response (200 OK):**
```json
{
  "subscriptions": [
    {
      "id": 12345678,
      "state": "active",
      "productHandle": "eshop-pro",
      "productName": "Pro Plan",
      "currentPeriodStartsAt": "2024-09-06T00:00:00Z",
      "currentPeriodEndsAt": "2024-10-06T00:00:00Z",
      "nextAssessmentAt": "2024-10-06T00:00:00Z",
      "activatedAt": "2024-09-06T00:00:00Z",
      "createdAt": "2024-09-06T14:30:00Z",
      "updatedAt": "2024-09-06T14:30:00Z"
    }
  ],
  "correlationId": "UUID-HERE"
}
```

**Verification Points:**
- Status code is 200
- Array contains subscription(s)
- Subscription from Test 3 appears in the list
- Subscription details match what was created

✓ VERIFIED IF ALL CHECKS PASS

### Test 5: Error Handling

#### Missing Authorization
```bash
curl -X GET "https://localhost:25083/api/subscription-plans" \
  --insecure
```

**Expected Response (401 Unauthorized)**

#### Invalid Product Handle
```bash
curl -X POST "https://localhost:25083/api/subscriptions" \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "invalid-handle"}' \
  --insecure
```

**Expected Response (400 Bad Request)** with Maxio error message

✓ VERIFIED IF ERROR HANDLING WORKS

## Phase 4: Integration Verification

### Checklist

- [ ] Build succeeds without errors
- [ ] All files created in correct locations
- [ ] No secrets in committed code
- [ ] Environment variables properly loaded
- [ ] PublicApi starts successfully
- [ ] Swagger UI shows new endpoints
- [ ] Authentication endpoint works
- [ ] Subscription plans endpoint returns data
- [ ] Subscription creation succeeds
- [ ] User subscriptions endpoint lists created subscriptions
- [ ] Error handling returns appropriate status codes
- [ ] Multiple subscriptions per user work
- [ ] Idempotent customer creation works (creating subscription twice doesn't create two customers)

## Troubleshooting

### "Maxio API error: Unauthorized"
- Verify MAXIO_API_KEY is correct
- Ensure API key has sufficient permissions
- Check Maxio dashboard for API key status

### "Product not found"
- Verify product handle exists in Maxio sandbox
- Default handles: eshop-pro, basic-plan
- Check MAXIO_DEFAULT_PRODUCT_FAMILY setting

### "Connection refused on localhost:25083"
- Verify PublicApi is still running
- Check that no other application is using port 25083
- Verify firewall isn't blocking localhost HTTPS

### "SSL certificate validation failed"
- This is expected in development
- Use `--insecure` flag with curl or `SkipCertificateCheck` with PowerShell
- Production deployment should use valid certificates

### "401 Unauthorized on subscription endpoints"
- Verify JWT token is not expired
- Check Authorization header format: `Authorization: Bearer <token>`
- Re-authenticate to get fresh token

## Production Deployment Considerations

Before deploying to production:

1. **Secrets Management**
   - Never commit MAXIO_API_KEY
   - Use secure secrets management (Azure Key Vault, AWS Secrets Manager, etc.)
   - Rotate API keys regularly

2. **API Rate Limiting**
   - Maxio has rate limits
   - Implement caching for product catalog
   - Add retry logic with exponential backoff

3. **Monitoring & Logging**
   - Log all API calls for audit trail
   - Monitor for failed subscriptions
   - Alert on authentication failures

4. **Data Persistence**
   - In production, don't use in-memory database
   - Consider storing subscription state locally for quick access
   - Implement webhook handlers for Maxio events

5. **Testing**
   - Test with Maxio staging environment first
   - Verify payment processing with test cards
   - Test subscription lifecycle (creation, updates, cancellation)

## Documentation

For detailed documentation, see:
- `MAXIO_INTEGRATION_SUMMARY.md` - Architecture and design decisions
- `TEST_MAXIO_INTEGRATION.md` - Detailed testing procedures
- Code comments in MaxioApiClient.cs for API implementation details

## Success Criteria

✓ **INTEGRATION COMPLETE IF:**
1. All tests pass (Phase 3)
2. No build errors
3. No hardcoded secrets in code
4. All endpoints return expected responses
5. Error handling works correctly
6. Idempotent operations verified
