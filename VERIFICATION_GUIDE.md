# Maxio Subscription Billing Integration - Verification Guide

This guide walks you through verifying that the Maxio subscription billing integration is working correctly in eShopOnWeb.

## Quick Start Verification (5 minutes)

### 1. Prerequisites Check
```bash
cd /path/to/repo

# Verify .NET SDK
dotnet --version   # Should be 8.0 or higher with DOTNET_ROLL_FORWARD support

# Verify git status
git log --oneline -5
# Should show: "Add Maxio subscription billing integration to eShopOnWeb"
```

### 2. Build Verification
```bash
# Build PublicApi
cd src/PublicApi
dotnet build -c Release
# Expected: "Build succeeded"
```

### 3. Configuration Verification
```bash
# Check that Maxio config was added to appsettings.json
grep -A 5 '"Maxio"' appsettings.json

# Expected output:
# "Maxio": {
#   "ApiKey": "",
#   "Subdomain": "cp-exp-2",
#   "ProductFamilyHandle": "eshop-subscribe",
#   "BaseUrl": ""
# }
```

## Full Integration Verification (30 minutes)

### Step 1: Set Maxio Credentials

Choose one method:

**Method A: Environment Variables (Recommended for CI/CD)**
```bash
export MAXIO_API_KEY="<your-sandbox-api-key>"
export MAXIO_SITE_SUBDOMAIN="cp-exp-2"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
```

**Method B: .NET User Secrets (Recommended for Development)**
```bash
cd src/PublicApi
dotnet user-secrets init  # Only needed if not already initialized

# Set secrets
dotnet user-secrets set "Maxio:ApiKey" "<your-sandbox-api-key>"
dotnet user-secrets set "Maxio:Subdomain" "cp-exp-2"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "eshop-subscribe"

# Verify secrets are set
dotnet user-secrets list
```

### Step 2: Start the PublicApi Service

```bash
cd src/PublicApi

# Option 1: With SDK/runtime compatibility settings
export DOTNET_ROLL_FORWARD=Major
dotnet run

# Option 2: With in-memory database setting
dotnet run -- --UseOnlyInMemoryDatabase=true

# Expected output:
# ...
# info: Microsoft.Hosting.Lifetime[14]
#       Now listening on: https://localhost:25483
# info: Microsoft.Hosting.Lifetime[0]
#       Application started. Press Ctrl+C to exit.
```

### Step 3: Verify Swagger UI

1. Open browser: `https://localhost:25483/swagger`
2. You should see three new endpoints under "SubscriptionEndpoints":
   - GET /api/subscription-plans
   - POST /api/subscriptions
   - GET /api/my-subscriptions
3. All three should have a lock icon (authorization required)

### Step 4: Get JWT Token for Testing

**Option A: Using Swagger UI**
1. Find POST /api/authenticate endpoint
2. Click "Try it out"
3. Enter credentials:
   ```json
   {
     "username": "demouser@microsoft.com",
     "password": "Pass@word123"
   }
   ```
4. Click "Execute"
5. Copy the "token" value from the response

**Option B: Using cURL**
```bash
curl -X POST https://localhost:25483/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word123"
  }' \
  -k  # -k ignores SSL certificate validation for localhost
```

Expected response:
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

### Step 5: Test List Plans Endpoint

**Using Swagger UI:**
1. Click on GET /api/subscription-plans
2. Click "Authorize" button
3. Paste the JWT token from Step 4 (just the token value, not "Bearer ")
4. Click "Authorize"
5. Click "Try it out" and then "Execute"

**Using cURL:**
```bash
TOKEN="<jwt-token-from-step-4>"

curl -X GET https://localhost:25483/api/subscription-plans \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -k

# Expected response:
# {
#   "success": true,
#   "message": "",
#   "plans": [
#     {
#       "id": 7126957,
#       "name": "Pro Plan",
#       "handle": "eshop-pro",
#       "description": "Professional subscription",
#       "priceInCents": 29900,
#       "price": 299.00,
#       "interval": 1,
#       "intervalUnit": "month"
#     },
#     {
#       "id": 7126958,
#       "name": "Basic Plan",
#       "handle": "basic-plan",
#       "description": "Basic subscription",
#       "priceInCents": 2900,
#       "price": 29.00,
#       "interval": 1,
#       "intervalUnit": "month"
#     }
#   ]
# }
```

✓ **Success Criteria:** Returns list of 2 plans with correct pricing

### Step 6: Test Create Subscription Endpoint

**Using cURL:**
```bash
TOKEN="<jwt-token-from-step-4>"

curl -X POST https://localhost:25483/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "eshop-pro"}' \
  -k

# Expected response:
# {
#   "success": true,
#   "message": "Subscription created successfully",
#   "subscriptionId": 12345678,
#   "customerId": 87654321,
#   "state": "active",
#   "productHandle": "eshop-pro",
#   "nextBillingAt": "2026-10-06T00:00:00Z",
#   "currentPeriodEndsAt": "2026-10-06T00:00:00Z"
# }
```

✓ **Success Criteria:** 
- `success` is true
- `state` is "active"
- `nextBillingAt` is a valid future date
- `subscriptionId` is a positive integer

### Step 7: Test List Subscriptions Endpoint

**Using cURL:**
```bash
TOKEN="<jwt-token-from-step-4>"

curl -X GET https://localhost:25483/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -k

# Expected response:
# {
#   "success": true,
#   "subscriptions": [
#     {
#       "subscriptionId": 12345678,
#       "customerId": 87654321,
#       "productHandle": "eshop-pro",
#       "state": "active",
#       "balanceInCents": 0,
#       "balance": 0.00,
#       "nextBillingAt": "2026-10-06T00:00:00Z",
#       "currentPeriodEndsAt": "2026-10-06T00:00:00Z",
#       "createdAt": "2026-09-06T12:00:00Z",
#       "updatedAt": "2026-09-06T12:00:00Z"
#     }
#   ]
# }
```

✓ **Success Criteria:**
- `success` is true
- Returns the subscription created in Step 6
- Subscription state is "active"

### Step 8: Test Idempotent Customer Creation

Create another subscription with the same user to verify no duplicate customer is created:

**Using cURL:**
```bash
TOKEN="<jwt-token-from-step-4>"

curl -X POST https://localhost:25483/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "basic-plan"}' \
  -k

# Then list subscriptions again
curl -X GET https://localhost:25483/api/my-subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -k
```

✓ **Success Criteria:**
- Second subscription created successfully
- List now shows 2 subscriptions
- Same `customerId` for both
- No duplicate customer errors in logs

## Error Scenario Testing

### Test 1: Missing JWT Token
```bash
curl -X GET https://localhost:25483/api/subscription-plans \
  -H "Content-Type: application/json" \
  -k

# Expected: 401 Unauthorized
```

### Test 2: Invalid JWT Token
```bash
curl -X GET https://localhost:25483/api/subscription-plans \
  -H "Authorization: Bearer invalid-token" \
  -H "Content-Type: application/json" \
  -k

# Expected: 401 Unauthorized
```

### Test 3: Missing Maxio Credentials
1. Stop the server
2. Unset environment variables: `unset MAXIO_API_KEY`
3. Restart the server (without setting via user-secrets)
4. Try to list plans

Expected: Error in logs or 500 response with message about missing credentials

### Test 4: Invalid Product Handle
```bash
TOKEN="<jwt-token-from-step-4>"

curl -X POST https://localhost:25483/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle": "nonexistent-plan"}' \
  -k

# Expected: 400 Bad Request with error message
```

## Code Review Checklist

### Security
- [ ] No credentials stored in code
- [ ] JWT authentication required for all endpoints
- [ ] API key passed via HTTP Basic auth to Maxio
- [ ] SSL/TLS used for all communication

### Architecture
- [ ] `MaxioClient` service handles all external API calls
- [ ] Configuration properly injected via dependency injection
- [ ] Endpoints follow existing eShopWeb patterns
- [ ] Error handling with logging

### Implementation Quality
- [ ] Build succeeds without errors
- [ ] All endpoints discoverable in Swagger
- [ ] Proper HTTP status codes used
- [ ] Consistent request/response formats
- [ ] User context extracted from JWT claims

### Testing Coverage
- [ ] Endpoints work with valid credentials
- [ ] Authentication required (401 without token)
- [ ] Idempotent customer creation
- [ ] Error handling for invalid inputs
- [ ] Multiple subscriptions per user supported

## Performance Considerations

### Database Impact
- In-memory database used (data lost on restart)
- No persistence layer required for MVP
- Customer-subscription mappings maintained in memory during runtime

### API Rate Limiting
- No explicit rate limiting implemented
- Maxio API has standard rate limits
- Consider adding caching for product list in production

### Optimization Opportunities (Future)
- Cache subscription plans for duration of app instance
- Implement batch customer lookups
- Add webhook support for subscription events
- Implement subscription cancellation/update endpoints

## Troubleshooting

### Build Fails with "SDK/Runtime Mismatch"
```bash
# Set SDK to roll forward
export DOTNET_ROLL_FORWARD=Major
dotnet build
```

### Maxio API Connection Refused
- Verify `MAXIO_API_KEY` is set to valid sandbox key
- Verify `MAXIO_SITE_SUBDOMAIN` is "cp-exp-2"
- Check internet connectivity
- Verify no firewall blocking outbound HTTPS

### "User not authenticated" Error
- Ensure JWT token is valid and not expired
- Token must come from `/api/authenticate` endpoint
- Check Authorization header format: `Authorization: Bearer <token>`

### "No plans returned"
- Verify `MAXIO_DEFAULT_PRODUCT_FAMILY` is set to "eshop-subscribe"
- Check Maxio sandbox that product family exists
- Verify products are not archived

### HTTPS Certificate Error on localhost
- Use `-k` flag with curl to skip verification (development only)
- Or ensure dev certificate is installed: `dotnet dev-certs https --check`

## Next Steps for Production

1. **Persistence**: Add subscription mapping to database
2. **Webhooks**: Implement Maxio webhooks for subscription events
3. **Cancellation**: Add endpoint to cancel subscriptions
4. **Upgrade/Downgrade**: Add plan change endpoints
5. **Metering**: Track API usage for metered components
6. **Rate Limiting**: Implement API rate limiting
7. **Monitoring**: Add Application Insights or similar
8. **Documentation**: Generate API documentation from Swagger

## Files Summary

| File | Purpose |
|------|---------|
| `MaxioConfiguration.cs` | Configuration binding for Maxio settings |
| `Services/MaxioClient.cs` | HTTP client for Maxio API |
| `SubscriptionEndpoints/SubscriptionPlansEndpoint.cs` | List available plans |
| `SubscriptionEndpoints/CreateSubscriptionEndpoint.cs` | Create subscription |
| `SubscriptionEndpoints/ListSubscriptionsEndpoint.cs` | List user subscriptions |
| `appsettings.json` | Configuration with Maxio section |
| `Program.cs` | Updated with Maxio DI registration |
| `MAXIO_INTEGRATION_SETUP.md` | Setup guide |
| `verify-maxio-integration.sh` | Automated verification script |

## Success Metrics

✓ All builds complete without errors  
✓ Three new endpoints appear in Swagger  
✓ JWT authentication required for all endpoints  
✓ Plans endpoint returns 2 plans with correct pricing  
✓ Create subscription succeeds with valid JWT  
✓ List subscriptions returns created subscription  
✓ Idempotent customer creation (no duplicates)  
✓ Error handling for missing/invalid credentials  
✓ No secrets stored in repository  
✓ Integration works end-to-end with Maxio sandbox  

---

**All verification steps completed successfully? Great!** The Maxio subscription billing integration is ready for use.
