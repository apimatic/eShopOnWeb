#!/bin/bash

# Test script for Maxio subscription billing integration
# Usage: ./test-subscription-endpoints.sh [username] [password]

set -e

API_BASE="https://localhost:25643"
USERNAME="${1:-demouser@microsoft.com}"
PASSWORD="${2:-Pass@word123}"

echo "=== Maxio Subscription Integration Test ==="
echo "API Base: $API_BASE"
echo "Username: $USERNAME"
echo ""

# Step 1: Authenticate
echo "[1/4] Authenticating user..."
AUTH_RESPONSE=$(curl -s -X POST "$API_BASE/api/authenticate" \
  -H "Content-Type: application/json" \
  -d "{
    \"username\": \"$USERNAME\",
    \"password\": \"$PASSWORD\"
  }")

TOKEN=$(echo $AUTH_RESPONSE | grep -o '"token":"[^"]*' | cut -d'"' -f4)
if [ -z "$TOKEN" ]; then
  echo "ERROR: Failed to authenticate. Response: $AUTH_RESPONSE"
  exit 1
fi
echo "✓ Got JWT token: ${TOKEN:0:20}..."
echo ""

# Step 2: List subscription plans
echo "[2/4] Listing subscription plans..."
PLANS=$(curl -s -X GET "$API_BASE/api/subscription-plans" \
  -H "Accept: application/json")

echo "✓ Available plans:"
echo $PLANS | jq '.plans[] | "\(.handle): \(.name) - \(.price | tostring) USD/month"' -r
echo ""

# Step 3: Create a subscription
echo "[3/4] Creating subscription to eshop-pro plan..."
SUB_RESPONSE=$(curl -s -X POST "$API_BASE/api/subscriptions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "productHandle": "eshop-pro"
  }')

SUBSCRIPTION_ID=$(echo $SUB_RESPONSE | grep -o '"id":[0-9]*' | head -1 | cut -d':' -f2)
if [ -z "$SUBSCRIPTION_ID" ]; then
  echo "ERROR: Failed to create subscription. Response: $SUB_RESPONSE"
  exit 1
fi
echo "✓ Created subscription (ID: $SUBSCRIPTION_ID)"
echo $SUB_RESPONSE | jq '.subscription | "  State: \(.state), Next Billing: \(.nextBillingAt), Price: \(.currentPrice | tostring) USD"' -r
echo ""

# Step 4: List user's subscriptions
echo "[4/4] Listing your subscriptions..."
SUBS=$(curl -s -X GET "$API_BASE/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN")

SUBSCRIPTION_COUNT=$(echo $SUBS | jq '.subscriptions | length')
echo "✓ Found $SUBSCRIPTION_COUNT subscription(s):"
echo $SUBS | jq '.subscriptions[] | "  ID: \(.id), State: \(.state), Price: \(.currentPrice | tostring) USD"' -r
echo ""

echo "=== All tests passed! ==="
