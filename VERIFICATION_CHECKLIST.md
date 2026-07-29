# Maxio Integration Verification Checklist

Use this checklist to verify the integration is working correctly.

## Pre-Requisites

- [ ] You have Maxio sandbox credentials (API key, subdomain, product family handle)
- [ ] You have eShopOnWeb demo user credentials (e.g., demouser@microsoft.com / Pass@word1)
- [ ] You have curl or Postman installed for testing
- [ ] .NET 8.0.x SDK or later is installed

## Setup Steps

### 1. Configure Maxio Credentials

```bash
cd src/PublicApi

# Initialize user-secrets (one time)
dotnet user-secrets init

# Set your credentials
dotnet user-secrets set "Maxio:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "YOUR_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "YOUR_PRODUCT_FAMILY"

# Verify secrets were set
dotnet user-secrets list
```

### 2. Start PublicApi

```bash
cd src/PublicApi

# Set environment for in-memory database (if needed)
set UseOnlyInMemoryDatabase=true

# Run with .NET roll-forward (if needed)
set DOTNET_ROLL_FORWARD=Major

# Start the server
dotnet run
```

The API will start on: `https://localhost:7243`

## Verification Steps

### Step 1: Authenticate ✓
Get a JWT token for testing authenticated endpoints.

```bash
curl -X POST https://localhost:7243/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username": "demouser@microsoft.com", "password": "Pass@word1"}'
```

**Expected Result**: Receive a token in the response
**Save**: Copy the `token` value for use in next steps

### Step 2: List Plans ✓
Verify you can fetch available subscription plans.

```bash
curl -X GET https://localhost:7243/api/subscription-plans \
  -H "Content-Type: application/json"
```

**Expected Result**: 
- Status: 200 OK
- Response contains array of plans with handles matching your Maxio site
- Fields include: id, handle, name, description, priceInCents, priceFormatted, interval, intervalUnit

**Examples to check**:
- Plan handle matches what you expect (e.g., "eshop-pro")
- Prices are formatted correctly ($X.XX)
- At least one plan is returned

### Step 3: Create Subscription ✓
Create a subscription for the authenticated user.

```bash
curl -X POST https://localhost:7243/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -d '{"planHandle": "eshop-pro"}'
```

**Expected Result**:
- Status: 200 OK (or 201 Created)
- Response includes:
  - id (numeric subscription ID)
  - productName
  - productHandle
  - state ("active")
  - createdAt (ISO timestamp)
  - nextBillingAt (ISO timestamp)

**Checks**:
- productHandle matches requested plan
- state is "active"
- nextBillingAt is ~30 days from now

### Step 4: Verify Idempotency ✓
Create same subscription again - should return the existing one.

```bash
curl -X POST https://localhost:7243/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -d '{"planHandle": "eshop-pro"}'
```

**Expected Result**:
- Status: 200 OK
- Response has SAME subscription ID as Step 3
- No new subscription created in Maxio

### Step 5: List User Subscriptions ✓
Retrieve all subscriptions for the authenticated user.

```bash
curl -X GET https://localhost:7243/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Expected Result**:
- Status: 200 OK
- Response includes the subscriptions array
- Array contains at least the subscription from Step 3

**Checks**:
- Subscription ID matches Step 3
- Product handle matches
- State is "active"

### Step 6: Create Second Plan ✓
Create subscription for a different plan.

```bash
curl -X POST https://localhost:7243/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -d '{"planHandle": "basic-plan"}'
```

**Expected Result**:
- Status: 200 OK
- Response has NEW subscription ID (different from Step 3)
- productHandle is "basic-plan"

### Step 7: Verify Multiple Subscriptions ✓
Confirm user now has both subscriptions.

```bash
curl -X GET https://localhost:7243/api/my-subscriptions \
  -H "Authorization: Bearer YOUR_TOKEN_HERE"
```

**Expected Result**:
- Status: 200 OK
- Array contains 2 subscriptions (from Steps 3 and 6)
- Both have state "active"
- Different product handles

### Step 8: Test Authentication ✓
Verify unauthenticated requests are rejected for protected endpoints.

```bash
curl -X GET https://localhost:7243/api/my-subscriptions
```

**Expected Result**:
- Status: 401 Unauthorized

## Error Cases (Optional)

### Invalid Plan Handle
```bash
curl -X POST https://localhost:7243/api/subscriptions \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_TOKEN_HERE" \
  -d '{"planHandle": "nonexistent-plan"}'
```

**Expected**: 400 Bad Request

### Missing Authorization Header
```bash
curl -X POST https://localhost:7243/api/subscriptions \
  -H "Content-Type: application/json" \
  -d '{"planHandle": "eshop-pro"}'
```

**Expected**: 401 Unauthorized

## Success Criteria

You have successfully verified the integration if:

- [x] Step 1: Authentication returns valid JWT token
- [x] Step 2: Plans endpoint returns available plans with correct data
- [x] Step 3: Create subscription returns valid subscription object
- [x] Step 4: Idempotency check returns same subscription (not duplicate)
- [x] Step 5: List subscriptions returns created subscriptions
- [x] Step 6: Can create multiple subscriptions for different plans
- [x] Step 7: User subscriptions list contains all subscriptions
- [x] Step 8: Unauthenticated access is rejected with 401

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Failed to connect to Maxio" | Verify API Key, Subdomain, and ProductFamilyHandle in user-secrets |
| "Plan not found" | Verify planHandle matches Maxio site; run Step 2 to see available plans |
| "401 Unauthorized" on subscription endpoints | Get fresh token from authenticate endpoint |
| "Build failed" | Set `DOTNET_ROLL_FORWARD=Major` environment variable |
| "Connection string error" | Set `UseOnlyInMemoryDatabase=true` environment variable |

## Files to Review

After successful verification, review these files to understand the implementation:

1. **SUBSCRIPTION_BILLING_SETUP.md** - Complete setup and API documentation
2. **MAXIO_INTEGRATION_SUMMARY.md** - Architecture and design details
3. **src/PublicApi/MaxioConfiguration.cs** - Configuration model
4. **src/PublicApi/MaxioSubscriptionService.cs** - Business logic
5. **src/PublicApi/SubscriptionEndpoints/*.cs** - REST endpoints

## Notes

- All subscription data persists in Maxio only (local DB is in-memory)
- Different users will have different subscription lists
- Test with different plans available in your Maxio site
- Maxio makes the first charge after subscription creation (or per plan configuration)

## Next Steps

After verification:

1. **Review code** - Examine implementation in detail
2. **Test edge cases** - Try various plan combinations, user scenarios
3. **Check logs** - Monitor console output for any warnings/errors
4. **Integrate UI** - Add subscription plan selection to web storefront
5. **Production deployment** - Move credentials to secure vault, configure monitoring

For complete documentation, see **SUBSCRIPTION_BILLING_SETUP.md** and **MAXIO_INTEGRATION_SUMMARY.md**.
