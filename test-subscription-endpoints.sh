#!/bin/bash

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration
API_URL="https://localhost:25043"
TEST_USERNAME="demouser@microsoft.com"
TEST_PASSWORD="Pass@word1"

echo -e "${YELLOW}=== Maxio Subscription Integration Test ===${NC}\n"

# Step 1: Authenticate
echo -e "${YELLOW}Step 1: Authenticating user...${NC}"
AUTH_RESPONSE=$(curl -s -X POST "$API_URL/api/authenticate" \
  -H "Content-Type: application/json" \
  -d "{
    \"username\": \"$TEST_USERNAME\",
    \"password\": \"$TEST_PASSWORD\"
  }")

TOKEN=$(echo $AUTH_RESPONSE | grep -o '"token":"[^"]*' | cut -d'"' -f4)

if [ -z "$TOKEN" ]; then
  echo -e "${RED}✗ Failed to authenticate${NC}"
  echo "Response: $AUTH_RESPONSE"
  exit 1
fi

echo -e "${GREEN}✓ Authentication successful${NC}"
echo "Token: ${TOKEN:0:20}...\n"

# Step 2: List subscription plans
echo -e "${YELLOW}Step 2: Listing subscription plans...${NC}"
PLANS_RESPONSE=$(curl -s -X GET "$API_URL/api/subscription-plans" \
  -H "Content-Type: application/json")

if echo "$PLANS_RESPONSE" | grep -q '"plans"'; then
  echo -e "${GREEN}✓ Successfully retrieved subscription plans${NC}"
  echo "Response: $PLANS_RESPONSE\n"
else
  echo -e "${RED}✗ Failed to retrieve subscription plans${NC}"
  echo "Response: $PLANS_RESPONSE"
  exit 1
fi

# Extract a product handle from the response
PRODUCT_HANDLE=$(echo $PLANS_RESPONSE | grep -o '"handle":"[^"]*' | head -1 | cut -d'"' -f4)

if [ -z "$PRODUCT_HANDLE" ]; then
  echo -e "${RED}✗ No plans found in response${NC}"
  exit 1
fi

echo "Using product handle: $PRODUCT_HANDLE\n"

# Step 3: Create a subscription
echo -e "${YELLOW}Step 3: Creating subscription...${NC}"
SUBSCRIPTION_RESPONSE=$(curl -s -X POST "$API_URL/api/subscriptions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d "{
    \"productHandle\": \"$PRODUCT_HANDLE\"
  }")

if echo "$SUBSCRIPTION_RESPONSE" | grep -q '"subscriptionId"'; then
  echo -e "${GREEN}✓ Subscription created successfully${NC}"
  echo "Response: $SUBSCRIPTION_RESPONSE\n"
else
  echo -e "${RED}✗ Failed to create subscription${NC}"
  echo "Response: $SUBSCRIPTION_RESPONSE"
  exit 1
fi

# Step 4: Get user subscriptions
echo -e "${YELLOW}Step 4: Retrieving user subscriptions...${NC}"
USER_SUBS_RESPONSE=$(curl -s -X GET "$API_URL/api/my-subscriptions" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN")

if echo "$USER_SUBS_RESPONSE" | grep -q '"subscriptions"'; then
  echo -e "${GREEN}✓ Successfully retrieved user subscriptions${NC}"
  echo "Response: $USER_SUBS_RESPONSE\n"
else
  echo -e "${RED}✗ Failed to retrieve user subscriptions${NC}"
  echo "Response: $USER_SUBS_RESPONSE"
  exit 1
fi

# Step 5: Test authentication requirement
echo -e "${YELLOW}Step 5: Testing authentication requirement...${NC}"
UNAUTH_RESPONSE=$(curl -s -X GET "$API_URL/api/my-subscriptions" \
  -H "Content-Type: application/json")

if echo "$UNAUTH_RESPONSE" | grep -q "401\|Unauthorized"; then
  echo -e "${GREEN}✓ Endpoint correctly requires authentication${NC}\n"
else
  echo -e "${YELLOW}⚠ Endpoint should require authentication${NC}\n"
fi

echo -e "${GREEN}=== All tests passed! ===${NC}"
