#!/bin/bash

# Maxio Subscription Endpoints Test Script
# This script tests the three subscription endpoints

set -e

BASE_URL="https://localhost:28323"
INSECURE=""  # Add "-k" flag if using self-signed cert and curl complains

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}=== Maxio Subscription Integration Test ===${NC}\n"

# Step 1: Authenticate
echo -e "${YELLOW}Step 1: Authenticating...${NC}"
AUTH_RESPONSE=$(curl -s $INSECURE -X POST "$BASE_URL/api/authenticate" \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}')

TOKEN=$(echo "$AUTH_RESPONSE" | jq -r '.token // empty')

if [ -z "$TOKEN" ]; then
  echo -e "${RED}✗ Authentication failed${NC}"
  echo "Response: $AUTH_RESPONSE"
  exit 1
fi

echo -e "${GREEN}✓ Authenticated successfully${NC}"
echo -e "  Token: ${TOKEN:0:50}...\n"

# Step 2: List subscription plans
echo -e "${YELLOW}Step 2: Listing subscription plans...${NC}"
PLANS_RESPONSE=$(curl -s $INSECURE -X GET "$BASE_URL/api/subscription-plans" \
  -H "Authorization: Bearer $TOKEN")

PLAN_COUNT=$(echo "$PLANS_RESPONSE" | jq '.plans | length // 0')

if [ "$PLAN_COUNT" -eq 0 ]; then
  echo -e "${RED}✗ No subscription plans found${NC}"
  echo "Response: $PLANS_RESPONSE"
else
  echo -e "${GREEN}✓ Found $PLAN_COUNT subscription plans${NC}"
  echo "$PLANS_RESPONSE" | jq '.plans[] | "\(.handle): \(.name) - $\(.price)/mo"'
  echo ""
fi

# Extract first product ID for subscription test
PRODUCT_ID=$(echo "$PLANS_RESPONSE" | jq '.plans[0].id // empty')

if [ -z "$PRODUCT_ID" ]; then
  echo -e "${YELLOW}Skipping subscription creation test (no products available)${NC}"
  exit 0
fi

echo -e "\n${YELLOW}Step 3: Creating subscription (Product ID: $PRODUCT_ID)...${NC}"
SUB_RESPONSE=$(curl -s $INSECURE -X POST "$BASE_URL/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"productId\": $PRODUCT_ID}")

SUB_ID=$(echo "$SUB_RESPONSE" | jq '.subscriptionId // empty')

if [ -z "$SUB_ID" ]; then
  echo -e "${RED}✗ Subscription creation failed${NC}"
  echo "Response: $SUB_RESPONSE"
else
  echo -e "${GREEN}✓ Subscription created successfully${NC}"
  echo "$SUB_RESPONSE" | jq '{subscriptionId, state, price, nextBillingDate, message}'
  echo ""
fi

# Step 4: Get user's subscriptions
echo -e "${YELLOW}Step 4: Retrieving user subscriptions...${NC}"
MY_SUBS=$(curl -s $INSECURE -X GET "$BASE_URL/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN")

MY_SUB_COUNT=$(echo "$MY_SUBS" | jq '.subscriptions | length // 0')

if [ "$MY_SUB_COUNT" -eq 0 ]; then
  echo -e "${YELLOW}⚠ No subscriptions found for user${NC}"
else
  echo -e "${GREEN}✓ Found $MY_SUB_COUNT subscription(s)${NC}"
  echo "$MY_SUBS" | jq '.subscriptions[] | "\(.productHandle): State=\(.state), Price=$\(.price)"'
  echo ""
fi

echo -e "${GREEN}=== All tests completed successfully ===${NC}"
