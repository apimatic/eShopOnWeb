# Maxio Subscription Billing Integration - Verification Guide

This guide provides step-by-step instructions to verify that the Maxio Advanced Billing integration is working correctly in eShopOnWeb.

## Prerequisites

1. **Environment Variables Set:**
   - `MAXIO_API_KEY` - Your Maxio sandbox API key
   - `MAXIO_SITE_SUBDOMAIN` - The Maxio sandbox subdomain (e.g., `cp-exp-1`)
   - `MAXIO_ENVIRONMENT` - Set to `US` for sandbox
   - `MAXIO_DEFAULT_PRODUCT_FAMILY` - The product family handle (e.g., `eshop-subscribe`)

2. **Database Setup:**
   - Run with `UseOnlyInMemoryDatabase=true` if SQL Server is not available
   - User-Secrets configured with Maxio API key: `dotnet user-secrets set "Maxio:ApiKey" "<your-key>"`

3. **Test Credentials:**
   - Username: `demouser@microsoft.com`
   - Password: `Pass@word1` (or use the configured DEFAULT_PASSWORD)

## Endpoints to Verify

### 1. List Subscription Plans
**GET** `/api/subscription-plans`

**No authentication required**

**Expected Response:**
```json
{
  "plans": [
    {
      "id": 7126957,
      "name": "Professional Plan",
      "handle": "eshop-pro",
      "pricePerMonth": 299.00,
      "description": "Professional subscription plan"
    },
    {
      "id": 7126958,
      "name": "Basic Plan",
      "handle": "basic-plan",
      "pricePerMonth": 29.00,
      "description": "Basic subscription plan"
    }
  ],
  "correlationId": "..."
}
```

**Testing:**
```bash
curl -X GET "https://localhost:28743/api/subscription-plans"
```

### 2. Authenticate User (Get JWT Token)
**POST** `/api/authenticate`

**Request Body:**
```json
{
  "username": "demouser@microsoft.com",
  "password": "Pass@word1"
}
```

**Expected Response:**
```json
{
  "result": true,
  "token": "eyJhbGc...",
  "username": "demouser@microsoft.com",
  ...
}
```

**Testing:**
```bash
curl -X POST "https://localhost:28743/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
```

### 3. Create Subscription
**POST** `/api/subscriptions`

**Authentication:** JWT Bearer token (from step 2)

**Request Body:**
```json
{
  "productId": 7126957
}
```

**Expected Response:**
```json
{
  "subscription": {
    "id": 123456,
    "customerId": 789,
    "productId": 7126957,
    "productName": "Plan",
    "state": "active",
    "createdAt": "2026-09-07T...",
    "currentPeriodEndsAt": "2026-10-07T..."
  },
  "correlationId": "..."
}
```

**Testing:**
```bash
# First get token
TOKEN=$(curl -s -X POST "https://localhost:28743/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r '.token')

# Then create subscription
curl -X POST "https://localhost:28743/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productId": 7126957}'
```

### 4. Get My Subscriptions
**GET** `/api/my-subscriptions`

**Authentication:** JWT Bearer token

**Expected Response:**
```json
{
  "subscriptions": [
    {
      "id": 123456,
      "customerId": 789,
      "productId": 7126957,
      "productName": "Plan",
      "state": "active",
      "createdAt": "2026-09-07T...",
      "currentPeriodEndsAt": "2026-10-07T..."
    }
  ],
  "correlationId": "..."
}
```

**Testing:**
```bash
curl -X GET "https://localhost:28743/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN"
```

## Running the Verification Tests

### Complete Test Sequence

```bash
#!/bin/bash

# Start the PublicApi service
dotnet run --project src/PublicApi/PublicApi.csproj &
sleep 5

API_URL="https://localhost:28743"

# Step 1: List Plans (no auth)
echo "=== Step 1: List Subscription Plans ==="
curl -s -X GET "$API_URL/api/subscription-plans" | jq '.'

# Step 2: Authenticate
echo -e "\n=== Step 2: Authenticate ==="
AUTH_RESPONSE=$(curl -s -X POST "$API_URL/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}')
echo $AUTH_RESPONSE | jq '.'
TOKEN=$(echo $AUTH_RESPONSE | jq -r '.token')

# Step 3: Create Subscription
echo -e "\n=== Step 3: Create Subscription ==="
curl -s -X POST "$API_URL/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productId": 7126957}' | jq '.'

# Step 4: List My Subscriptions
echo -e "\n=== Step 4: List My Subscriptions ==="
curl -s -X GET "$API_URL/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" | jq '.'
```

## Expected Behavior

1. **Plans Load Successfully**
   - Both Pro ($299/mo) and Basic ($29/mo) plans are returned
   - Plans have correct handles and prices

2. **User Authentication Works**
   - JWT token is issued for valid credentials
   - Token can be used in Bearer header

3. **Subscription Creation Succeeds**
   - Customer is created automatically if not exists
   - Idempotency: Creating subscription twice with same user doesn't create duplicate customer
   - Subscription state is "active"
   - Next billing date is set correctly

4. **Subscription Retrieval Works**
   - User can see their own subscriptions
   - Subscription details match what was created
   - Other users cannot see this subscription

## Troubleshooting

### "API key not found" Error
- Verify `Maxio:ApiKey` is set in user-secrets
- Check environment variable: `echo $MAXIO_API_KEY`

### 401 Unauthorized on Protected Endpoints
- Ensure JWT token is passed in `Authorization: Bearer <token>` header
- Verify token is not expired
- Check token is from successful authentication

### "Customer already exists" Error
- This is expected if creating subscription twice for same user
- The system should handle idempotency by checking existing customer first

### "Product not found" Error
- Verify the product ID matches a seeded plan on the Maxio sandbox
- Check Maxio sandbox has the `eshop-subscribe` family with plans

### HTTPS Certificate Issues
- Run: `dotnet dev-certs https --check`
- If not trusted, run: `dotnet dev-certs https --clean && dotnet dev-certs https --trust`

## Success Criteria

The integration is working correctly when:
- ✅ Plans are listed correctly  
- ✅ Users can authenticate and receive JWT token
- ✅ Subscriptions can be created for authenticated users
- ✅ Multiple subscriptions can be retrieved for a user
- ✅ Idempotency works (duplicate requests don't cause errors)
- ✅ Billing dates are set correctly
- ✅ Subscription state shows as "active"

## Next Steps (If Integration Incomplete)

If the implementation stub methods need to be filled in:
1. Check `/maxio-plan.md` for exact SDK method signatures
2. Review `dotnet-calling-endpoints` skill for parameter binding patterns
3. Review `dotnet-models` skill for request/response shapes
4. Verify error handling follows `dotnet-error-handling` patterns
