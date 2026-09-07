#!/bin/bash

# Maxio Integration Test Script
# Tests the subscription billing endpoints

API_URL="${1:-https://localhost:28243}"
USERNAME="${2:-demouser}"
PASSWORD="${3:-Pass@word1}"
PLAN_HANDLE="${4:-eshop-pro}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
GRAY='\033[0;90m'
NC='\033[0m' # No Color

function info {
    echo -e "${CYAN}ℹ️  $1${NC}"
}

function success {
    echo -e "${GREEN}✓ $1${NC}"
}

function error {
    echo -e "${RED}✗ $1${NC}"
}

echo ""
echo -e "${CYAN}================================${NC}"
echo -e "${CYAN}Maxio Integration Verification${NC}"
echo -e "${CYAN}================================${NC}"
echo ""

# Test 1: Get Available Plans
info "Test 1: Getting available subscription plans..."
PLANS_RESPONSE=$(curl -s -k -X GET "$API_URL/api/subscription-plans" \
    -H "Accept: application/json")

PLAN_COUNT=$(echo "$PLANS_RESPONSE" | grep -o '"id"' | wc -l)
if [ "$PLAN_COUNT" -gt 0 ]; then
    success "Found subscription plans"
    echo "$PLANS_RESPONSE" | grep -o '"name":"[^"]*"' | head -3 | sed 's/"name":"\(.*\)"/  - \1/' | sed "s/^/  ${GRAY}/" | sed "s/$/${NC}/"
else
    error "No plans found. Check Maxio configuration."
    exit 1
fi

echo ""

# Test 2: Authenticate
info "Test 2: Authenticating user '$USERNAME'..."
AUTH_RESPONSE=$(curl -s -k -X POST "$API_URL/api/authenticate" \
    -H "Content-Type: application/json" \
    -d "{\"username\":\"$USERNAME\",\"password\":\"$PASSWORD\"}")

TOKEN=$(echo "$AUTH_RESPONSE" | grep -o '"token":"[^"]*"' | cut -d'"' -f4)
if [ -n "$TOKEN" ]; then
    success "Authentication successful"
else
    error "Authentication failed"
    echo "$AUTH_RESPONSE"
    exit 1
fi

echo ""

# Test 3: Create Subscription
info "Test 3: Creating subscription to plan '$PLAN_HANDLE'..."
SUB_RESPONSE=$(curl -s -k -X POST "$API_URL/api/subscriptions" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"planHandle\":\"$PLAN_HANDLE\"}")

SUB_ID=$(echo "$SUB_RESPONSE" | grep -o '"id":[0-9]*' | head -1 | cut -d':' -f2)
if [ -n "$SUB_ID" ] && [ "$SUB_ID" -gt 0 ]; then
    success "Subscription created successfully"
    echo -e "${GRAY}  - ID: $SUB_ID${NC}"
    PRODUCT_NAME=$(echo "$SUB_RESPONSE" | grep -o '"productName":"[^"]*"' | cut -d'"' -f4)
    echo -e "${GRAY}  - Plan: $PRODUCT_NAME${NC}"
    STATE=$(echo "$SUB_RESPONSE" | grep -o '"state":"[^"]*"' | cut -d'"' -f4)
    echo -e "${GRAY}  - State: $STATE${NC}"
else
    error "Subscription creation failed"
    echo "$SUB_RESPONSE"
    exit 1
fi

echo ""

# Test 4: Get User Subscriptions
info "Test 4: Retrieving user subscriptions..."
MYSUBS_RESPONSE=$(curl -s -k -X GET "$API_URL/api/my-subscriptions" \
    -H "Authorization: Bearer $TOKEN")

SUB_COUNT=$(echo "$MYSUBS_RESPONSE" | grep -o '"id"' | wc -l)
if [ "$SUB_COUNT" -gt 0 ]; then
    success "Found $SUB_COUNT active subscription(s)"
else
    error "No subscriptions found for user"
    exit 1
fi

echo ""

# Test 5: Verify Subscription Details
info "Test 5: Verifying subscription was created correctly..."
if echo "$MYSUBS_RESPONSE" | grep -q "\"id\":$SUB_ID"; then
    success "Subscription verified in user's subscription list"
else
    error "Subscription not found in user's list"
    exit 1
fi

echo ""
echo -e "${CYAN}================================${NC}"
echo -e "${GREEN}✓ All tests passed!${NC}"
echo -e "${CYAN}================================${NC}"
echo ""
echo -e "${GREEN}Integration Status: WORKING${NC}"
