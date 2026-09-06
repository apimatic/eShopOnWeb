#!/bin/bash

# Test script for Maxio subscription endpoints
# Prerequisites:
# 1. PublicApi is running on https://localhost:25443
# 2. Replace <jwt-token> with actual token from /api/authenticate
# 3. Set MAXIO env vars before running the app

BASE_URL="https://localhost:25443"
JWT_TOKEN=""

echo "=== Maxio Subscription Integration Tests ==="
echo ""

# Test 1: Get subscription plans (no auth required)
echo "Test 1: GET /api/subscription-plans"
echo "Expected: List of available plans"
echo ""
curl -s -X GET "$BASE_URL/api/subscription-plans" \
  -H "accept: application/json" \
  -k | jq '.' || echo "Failed to retrieve plans"
echo ""
echo "---"
echo ""

# Test 2: Authenticate to get JWT token
echo "Test 2: POST /api/authenticate"
echo "Expected: JWT token in response"
echo ""
AUTH_RESPONSE=$(curl -s -X POST "$BASE_URL/api/authenticate" \
  -H "accept: application/json" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"P@ssw0rd!"}' \
  -k)

echo "$AUTH_RESPONSE" | jq '.'
JWT_TOKEN=$(echo "$AUTH_RESPONSE" | jq -r '.token')

if [ "$JWT_TOKEN" == "null" ] || [ -z "$JWT_TOKEN" ]; then
  echo "ERROR: Failed to get JWT token"
  exit 1
fi

echo "Token obtained: ${JWT_TOKEN:0:50}..."
echo ""
echo "---"
echo ""

# Test 3: Create subscription
echo "Test 3: POST /api/subscriptions"
echo "Expected: Created subscription with plan 'eshop-pro'"
echo ""
curl -s -X POST "$BASE_URL/api/subscriptions" \
  -H "accept: application/json" \
  -H "Authorization: Bearer $JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}' \
  -k | jq '.' || echo "Failed to create subscription"
echo ""
echo "---"
echo ""

# Test 4: Get user subscriptions
echo "Test 4: GET /api/my-subscriptions"
echo "Expected: List of user subscriptions"
echo ""
curl -s -X GET "$BASE_URL/api/my-subscriptions" \
  -H "accept: application/json" \
  -H "Authorization: Bearer $JWT_TOKEN" \
  -k | jq '.' || echo "Failed to retrieve subscriptions"
echo ""
echo "---"
echo ""

echo "=== Tests Complete ==="
