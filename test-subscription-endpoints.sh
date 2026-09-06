#!/bin/bash

# Test script for Maxio subscription billing integration
# Prerequisites: PublicApi running on https://localhost:27583 with MAXIO env vars set

set -e

BASE_URL="https://localhost:27583"
INSECURE="-k"  # curl insecure flag for self-signed certs

echo "=========================================="
echo "Maxio Subscription Integration Test"
echo "=========================================="
echo ""

# Step 1: Authenticate
echo "1. Authenticating as demouser@microsoft.com..."
AUTH_RESPONSE=$(curl -s $INSECURE -X POST "$BASE_URL/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}')

TOKEN=$(echo $AUTH_RESPONSE | grep -o '"token":"[^"]*' | cut -d'"' -f4)
if [ -z "$TOKEN" ]; then
  echo "ERROR: Failed to authenticate. Response:"
  echo $AUTH_RESPONSE
  exit 1
fi
echo "✓ Got bearer token: ${TOKEN:0:20}..."
echo ""

# Step 2: List subscription plans
echo "2. Listing subscription plans..."
PLANS_RESPONSE=$(curl -s $INSECURE -X GET "$BASE_URL/api/subscription-plans" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json")

PLAN_COUNT=$(echo $PLANS_RESPONSE | grep -o '"id"' | wc -l)
if [ $PLAN_COUNT -lt 1 ]; then
  echo "ERROR: No plans returned. Response:"
  echo $PLANS_RESPONSE
  exit 1
fi
echo "✓ Retrieved $PLAN_COUNT plan(s)"
echo "  Response: $(echo $PLANS_RESPONSE | cut -c1-100)..."
echo ""

# Step 3: Create a subscription
echo "3. Creating subscription to eshop-pro plan..."
CREATE_RESPONSE=$(curl -s $INSECURE -X POST "$BASE_URL/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productHandle":"eshop-pro"}' \
  -w "\n%{http_code}")

HTTP_CODE=$(echo "$CREATE_RESPONSE" | tail -1)
RESPONSE_BODY=$(echo "$CREATE_RESPONSE" | head -n -1)

if [ "$HTTP_CODE" != "201" ]; then
  echo "ERROR: Expected 201 Created, got $HTTP_CODE"
  echo "Response: $RESPONSE_BODY"
  exit 1
fi

SUB_ID=$(echo $RESPONSE_BODY | grep -o '"subscriptionId":[0-9]*' | cut -d':' -f2)
echo "✓ Subscription created successfully"
echo "  Subscription ID: $SUB_ID"
echo "  Response: $(echo $RESPONSE_BODY | cut -c1-100)..."
echo ""

# Step 4: List user's subscriptions
echo "4. Listing user's subscriptions..."
LIST_RESPONSE=$(curl -s $INSECURE -X GET "$BASE_URL/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json")

SUB_COUNT=$(echo $LIST_RESPONSE | grep -o '"id"' | wc -l)
if [ $SUB_COUNT -lt 1 ]; then
  echo "ERROR: No subscriptions returned. Response:"
  echo $LIST_RESPONSE
  exit 1
fi
echo "✓ Retrieved $SUB_COUNT subscription(s)"
echo "  Response: $(echo $LIST_RESPONSE | cut -c1-100)..."
echo ""

# Step 5: Test missing auth
echo "5. Testing missing auth (should fail with 401)..."
NOAUTH_RESPONSE=$(curl -s $INSECURE -X GET "$BASE_URL/api/subscription-plans" \
  -H "Content-Type: application/json" \
  -w "\n%{http_code}")

NOAUTH_CODE=$(echo "$NOAUTH_RESPONSE" | tail -1)
if [ "$NOAUTH_CODE" != "401" ]; then
  echo "ERROR: Expected 401 Unauthorized, got $NOAUTH_CODE"
  exit 1
fi
echo "✓ Auth correctly required (got 401)"
echo ""

echo "=========================================="
echo "✓ All tests passed!"
echo "=========================================="
