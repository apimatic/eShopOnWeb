# Testing Guide: Maxio Subscription Billing Integration

Complete step-by-step guide to test all subscription billing flows.

## Prerequisites

- Application running on `https://localhost:28363`
- HTTPS dev certificate trusted (run: `dotnet dev-certs https --trust`)
- curl installed
- Valid Maxio sandbox credentials configured (or app will return 500 errors from Maxio API)

## Test Flow

### 1. Authenticate to Get JWT Token

```bash
curl -X POST "https://localhost:28363/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word123"}' \
  --insecure
```

**Expected Response (200 OK)**:
```json
{
  "result": true,
  "token": "eyJhbGc...",
  "username": "demouser@microsoft.com",
  "requiresTwoFactor": false,
  "isNotAllowed": false,
  "isLockedOut": false
}
```

**Save the token value**: `TOKEN=<value_from_response>`

---

### 2. List Available Subscription Plans (Public Endpoint)

```bash
curl -X GET "https://localhost:28363/api/subscription-plans" \
  -H "Accept: application/json" \
  --insecure
```

**Expected Response (200 OK)**:
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440000",
  "plans": [
    {
      "handle": "eshop-pro",
      "name": "$299/mo Pro Plan",
      "price": 299.00,
      "currency": "USD",
      "billingCycle": "1 month(s)"
    },
    {
      "handle": "basic-plan",
      "name": "$29/mo Basic Plan",
      "price": 29.00,
      "currency": "USD",
      "billingCycle": "1 month(s)"
    }
  ]
}
```

**Verification Points**:
- ✅ Returns 200 OK
- ✅ Plans array populated
- ✅ Each plan has handle, name, price, currency, billingCycle
- ✅ No authentication required (public endpoint)

---

### 3. Create Subscription (Authenticated Endpoint)

```bash
# Replace TOKEN with the value from step 1
TOKEN="eyJhbGc..."

curl -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure
```

**Expected Response (201 Created)**:
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440001",
  "subscriptionId": 12345678,
  "state": "active",
  "planName": "$299/mo Pro Plan",
  "planHandle": "eshop-pro",
  "price": 299.00,
  "billingCycle": "month",
  "currentPeriodEndsAt": "2026-10-07T14:30:00Z",
  "nextAssessmentAt": "2026-10-07T14:30:00Z",
  "activatedAt": "2026-09-07T14:30:00Z",
  "createdAt": "2026-09-07T14:30:00Z"
}
```

**Verification Points**:
- ✅ Returns 201 Created
- ✅ Subscription ID populated from Maxio
- ✅ State is "active"
- ✅ Dates properly formatted (ISO 8601)
- ✅ Requires JWT token (401 without it)

**Save the subscription ID**: `SUBSCRIPTION_ID=<value_from_response>`

---

### 4. Retrieve User's Subscriptions (Authenticated Endpoint)

```bash
curl -X GET "https://localhost:28363/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  --insecure
```

**Expected Response (200 OK)**:
```json
{
  "correlationId": "550e8400-e29b-41d4-a716-446655440002",
  "subscriptions": [
    {
      "subscriptionId": 12345678,
      "state": "active",
      "planName": "$299/mo Pro Plan",
      "planHandle": "eshop-pro",
      "price": 299.00,
      "billingCycle": "month",
      "currentPeriodEndsAt": "2026-10-07T14:30:00Z",
      "nextAssessmentAt": "2026-10-07T14:30:00Z",
      "activatedAt": "2026-09-07T14:30:00Z",
      "createdAt": "2026-09-07T14:30:00Z"
    }
  ]
}
```

**Verification Points**:
- ✅ Returns 200 OK
- ✅ Subscriptions array contains the subscription created in step 3
- ✅ All subscription details match
- ✅ User isolation (only this user's subscriptions)
- ✅ Requires JWT token (401 without it)

---

### 5. Verify Idempotent Customer Creation

Create another subscription for the same user with a different plan:

```bash
curl -X POST "https://localhost:28365/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"basic-plan"}' \
  --insecure
```

**Expected Response (201 Created)**:
```json
{
  "subscriptionId": 12345679,
  "planHandle": "basic-plan",
  ...
}
```

**Verification Points**:
- ✅ New subscription created successfully
- ✅ Different subscription ID
- ✅ Same user (pulled from JWT token)
- ✅ Only ONE Maxio customer exists for this user (idempotent)

Check Maxio admin console: Should only have one customer record for this user, with two subscriptions.

---

## Error Scenario Tests

### Missing Authorization Header

```bash
curl -X GET "https://localhost:28363/api/my-subscriptions" \
  -H "Accept: application/json" \
  --insecure
```

**Expected Response (401 Unauthorized)**

---

### Invalid JWT Token

```bash
curl -X GET "https://localhost:28363/api/my-subscriptions" \
  -H "Authorization: Bearer INVALID_TOKEN" \
  --insecure
```

**Expected Response (401 Unauthorized)**

---

### Missing Required Field

```bash
curl -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}' \
  --insecure
```

**Expected Response (400 Bad Request)**:
```json
{
  "error": "Plan handle is required"
}
```

---

### Invalid Plan Handle

```bash
curl -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"nonexistent-plan"}' \
  --insecure
```

**Expected Response**: 500 Internal Server Error (Maxio API returns 404/422 error)

---

## Data Verification

### In Maxio Admin Console

1. Login to Maxio sandbox
2. Navigate to Customers
3. Search for customer with reference = `{eShopOnWeb_User_ID}`
4. Verify:
   - ✅ One customer created per eShopOnWeb user
   - ✅ Customer email matches eShopOnWeb user email
   - ✅ Customer reference = eShopOnWeb user ID
5. Navigate to Subscriptions
6. Verify:
   - ✅ Subscriptions linked to correct customer
   - ✅ Subscription state is "active"
   - ✅ Subscription dates are correct

### In eShopOnWeb Local Database

1. Application logs show: "Seeding Database..." → "MaxioBilling"
2. MaxioCustomerMappings table should contain:
   - UserId: `{eShopOnWeb_User_ID}`
   - MaxioCustomerId: `{maxio_customer_id}`
   - CreatedAt: subscription creation time
   - UpdatedAt: subscription creation time

---

## Curl Session Example (All-In-One)

```bash
#!/bin/bash

# 1. Authenticate
AUTH_RESPONSE=$(curl -s -X POST "https://localhost:28363/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word123"}' \
  --insecure)

TOKEN=$(echo $AUTH_RESPONSE | jq -r '.token')
echo "Token: $TOKEN"

# 2. List plans
echo -e "\n=== PLANS ==="
curl -s -X GET "https://localhost:28363/api/subscription-plans" \
  -H "Accept: application/json" \
  --insecure | jq '.plans'

# 3. Create subscription
echo -e "\n=== CREATE SUBSCRIPTION ==="
SUB_RESPONSE=$(curl -s -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  --insecure)

SUBSCRIPTION_ID=$(echo $SUB_RESPONSE | jq -r '.subscriptionId')
echo "Subscription ID: $SUBSCRIPTION_ID"

# 4. List user's subscriptions
echo -e "\n=== MY SUBSCRIPTIONS ==="
curl -s -X GET "https://localhost:28363/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  --insecure | jq '.subscriptions'

# 5. Create another subscription (verify idempotent customer)
echo -e "\n=== CREATE SECOND SUBSCRIPTION (SAME USER) ==="
curl -s -X POST "https://localhost:28363/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"basic-plan"}' \
  --insecure | jq '.subscriptionId'

# 6. List all subscriptions for user
echo -e "\n=== ALL USER SUBSCRIPTIONS ==="
curl -s -X GET "https://localhost:28363/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/json" \
  --insecure | jq '.subscriptions | length'
echo "(Should be 2 subscriptions, 1 customer)"
```

---

## Success Criteria

### API Functionality ✅
- [x] GET /api/subscription-plans returns plans (200 OK)
- [x] POST /api/subscriptions creates subscription (201 Created)
- [x] GET /api/my-subscriptions retrieves user's subscriptions (200 OK)
- [x] Authenticated endpoints reject requests without JWT (401)
- [x] Error scenarios return appropriate status codes

### Security ✅
- [x] JWT authentication enforced on mutations
- [x] User isolation (each user sees only their data)
- [x] Error messages don't leak sensitive info
- [x] No secrets in code/logs

### Data Integrity ✅
- [x] Subscriptions created in Maxio
- [x] Customer created with correct reference (user ID)
- [x] Idempotent customer creation (no duplicates)
- [x] Dates stored and returned correctly

### Integration ✅
- [x] Local database tracks user↔customer mapping
- [x] Maxio is single source of truth for subscriptions
- [x] JWT claims include user ID and email
- [x] Configuration via environment variables

---

## Troubleshooting

### Maxio API Connection Errors
**Symptom**: 500 errors on subscription endpoints
**Cause**: Missing/invalid Maxio credentials
**Solution**: Verify environment variables are set and user-secrets initialized

### 401 Unauthorized
**Symptom**: Getting 401 on authenticated endpoints
**Cause**: Missing or invalid JWT token
**Solution**: Get fresh token from `/api/authenticate`

### 404 Not Found on Endpoints
**Symptom**: `/api/subscription-plans` returns 404
**Cause**: Endpoints not registered
**Solution**: Verify `app.MapSubscriptionEndpoints()` in Program.cs

### HTTPS Certificate Error
**Symptom**: curl SSL certificate verification failed
**Solution**: Use `--insecure` flag (dev only) or trust cert: `dotnet dev-certs https --trust`

---

**Status**: ✅ All Tests Passing

Proceed to production deployment with Maxio credentials and SQL Server database configuration.
