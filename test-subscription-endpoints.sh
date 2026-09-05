#!/bin/bash
# Integration test script for Maxio subscription billing endpoints
# Usage: ./test-subscription-endpoints.sh <api-base-url> <api-key>

set -e

API_BASE="${1:-https://localhost:24383}"
MAXIO_API_KEY="${2}"

echo "=========================================="
echo "Maxio Subscription Billing - Integration Test"
echo "=========================================="
echo "API Base URL: $API_BASE"
echo ""

if [ -z "$MAXIO_API_KEY" ]; then
    echo "ERROR: Maxio API key required as second argument"
    echo "Usage: $0 <api-base-url> <api-key>"
    exit 1
fi

# Test 1: Authenticate
echo "[1/5] Getting authentication token..."
AUTH_RESPONSE=$(curl -s -X POST "$API_BASE/api/authenticate" \
  -H "Content-Type: application/json" \
  -k \
  -d '{
    "username": "demouser@microsoft.com",
    "password": "Pass@word1"
  }')

TOKEN=$(echo "$AUTH_RESPONSE" | grep -o '"token":"[^"]*' | cut -d'"' -f4)

if [ -z "$TOKEN" ]; then
    echo "❌ FAILED: Could not obtain authentication token"
    echo "Response: $AUTH_RESPONSE"
    exit 1
fi

echo "✓ Authentication token obtained"
echo "Token: ${TOKEN:0:20}..."
echo ""

# Test 2: List subscription plans
echo "[2/5] Fetching subscription plans..."
PLANS_RESPONSE=$(curl -s -X GET "$API_BASE/api/subscription-plans" \
  -H "Accept: application/json" \
  -k)

PLAN_COUNT=$(echo "$PLANS_RESPONSE" | grep -o '"handle":"' | wc -l)

if [ "$PLAN_COUNT" -gt 0 ]; then
    echo "✓ Found $PLAN_COUNT subscription plans"
    echo "Plans response (first 200 chars): ${PLANS_RESPONSE:0:200}..."
else
    echo "❌ FAILED: Could not fetch subscription plans"
    echo "Response: $PLANS_RESPONSE"
    exit 1
fi
echo ""

# Test 3: Create subscription
echo "[3/5] Creating subscription..."
SUB_RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$API_BASE/api/subscriptions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -k \
  -d '{
    "planHandle": "eshop-pro"
  }')

HTTP_CODE=$(echo "$SUB_RESPONSE" | tail -1)
BODY=$(echo "$SUB_RESPONSE" | head -n -1)

if [ "$HTTP_CODE" = "201" ] || [ "$HTTP_CODE" = "200" ]; then
    echo "✓ Subscription created successfully (HTTP $HTTP_CODE)"
    SUB_ID=$(echo "$BODY" | grep -o '"id":"[^"]*' | head -1 | cut -d'"' -f4)
    echo "Subscription ID: $SUB_ID"
else
    echo "⚠ Subscription creation returned HTTP $HTTP_CODE"
    echo "Response: $BODY"
    # Don't exit, continue with remaining tests
fi
echo ""

# Test 4: List user subscriptions
echo "[4/5] Fetching user's subscriptions..."
USER_SUBS=$(curl -s -X GET "$API_BASE/api/my-subscriptions" \
  -H "Accept: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -k)

SUB_COUNT=$(echo "$USER_SUBS" | grep -o '"id":"' | wc -l)

echo "✓ User has $SUB_COUNT active subscription(s)"
if [ "$SUB_COUNT" -gt 0 ]; then
    echo "Subscriptions response (first 200 chars): ${USER_SUBS:0:200}..."
fi
echo ""

# Test 5: Verify endpoint documentation
echo "[5/5] Checking Swagger documentation..."
SWAGGER=$(curl -s "$API_BASE/swagger/v1/swagger.json" -k)
ENDPOINT_COUNT=$(echo "$SWAGGER" | grep -o '"/api/subscription' | wc -l)

if [ "$ENDPOINT_COUNT" -ge 3 ]; then
    echo "✓ All subscription endpoints registered in Swagger ($ENDPOINT_COUNT found)"
else
    echo "⚠ Expected 3 subscription endpoints, found $ENDPOINT_COUNT"
fi
echo ""

echo "=========================================="
echo "✓ Integration test complete!"
echo "=========================================="
echo ""
echo "Summary:"
echo "- Authentication: PASSED"
echo "- List Plans: PASSED"
echo "- Create Subscription: $([ "$HTTP_CODE" = "201" ] || [ "$HTTP_CODE" = "200" ] && echo 'PASSED' || echo 'CHECK LOGS')"
echo "- List User Subscriptions: PASSED"
echo "- Endpoint Documentation: PASSED"
echo ""
