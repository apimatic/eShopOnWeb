#!/bin/bash

# Maxio Subscription Integration Verification Script
# Run this after confirming the SDK package is available
# Prerequisites: .NET SDK with DOTNET_ROLL_FORWARD=Major, curl, jq (optional)

set -e

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_URL="https://localhost:27703"
DEMO_EMAIL="demouser@example.com"
DEMO_PASSWORD="Pass@word1"  # Change if different in seeded DB

echo "╔════════════════════════════════════════════════════════════════╗"
echo "║ Maxio Subscription Integration Verification                    ║"
echo "╚════════════════════════════════════════════════════════════════╝"
echo ""

# Step 1: Build
echo "[1/6] Building PublicApi..."
cd "$REPO_ROOT"
export DOTNET_ROLL_FORWARD=Major
dotnet clean src/PublicApi/PublicApi.csproj > /dev/null 2>&1 || true
dotnet restore src/PublicApi/PublicApi.csproj > /dev/null
if ! dotnet build src/PublicApi/PublicApi.csproj; then
    echo "❌ Build failed. Check SDK package availability."
    exit 1
fi
echo "✅ Build succeeded"
echo ""

# Step 2: Get auth token
echo "[2/6] Obtaining JWT token..."
TOKEN=$(curl -s -X POST "$API_URL/api/authenticate" \
  -H "Content-Type: application/json" \
  -d "{\"username\":\"$DEMO_EMAIL\",\"password\":\"$DEMO_PASSWORD\"}" \
  --insecure | jq -r '.token // empty')

if [ -z "$TOKEN" ]; then
    echo "❌ Failed to obtain authentication token"
    echo "   Ensure PublicApi is running and demo user exists"
    exit 1
fi
echo "✅ Got token: ${TOKEN:0:20}..."
echo ""

# Step 3: List subscription plans
echo "[3/6] Listing subscription plans..."
PLANS=$(curl -s -X GET "$API_URL/api/subscription-plans" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure)

PLAN_COUNT=$(echo "$PLANS" | jq '.plans | length' 2>/dev/null || echo "0")
if [ "$PLAN_COUNT" -gt 0 ]; then
    echo "✅ Found $PLAN_COUNT subscription plans"
    echo "$PLANS" | jq -r '.plans[] | "   - \(.name) (\(.handle)): \(.priceInCents / 100 | @json) / \(.intervalUnit // "month")"' 2>/dev/null || true
else
    echo "⚠️  No plans found (ensure Maxio sandbox is configured correctly)"
fi
echo ""

# Step 4: Subscribe to a plan
echo "[4/6] Creating subscription (eshop-pro plan)..."
PLAN_HANDLE="eshop-pro"
CREATE_RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$API_URL/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"planHandle\":\"$PLAN_HANDLE\"}" \
  --insecure)

HTTP_CODE=$(echo "$CREATE_RESPONSE" | tail -n1)
BODY=$(echo "$CREATE_RESPONSE" | sed '$d')

if [ "$HTTP_CODE" = "201" ] || [ "$HTTP_CODE" = "200" ]; then
    SUB_ID=$(echo "$BODY" | jq '.subscription.id // empty' 2>/dev/null)
    SUB_STATE=$(echo "$BODY" | jq -r '.subscription.state // "unknown"' 2>/dev/null)
    NEXT_BILL=$(echo "$BODY" | jq -r '.subscription.currentPeriodEndsAt // "N/A"' 2>/dev/null)
    echo "✅ Subscription created"
    echo "   ID: $SUB_ID"
    echo "   State: $SUB_STATE"
    echo "   Next billing: $NEXT_BILL"
else
    echo "⚠️  Subscription creation returned HTTP $HTTP_CODE"
    echo "   Response: $BODY" | head -c 200
fi
echo ""

# Step 5: List user's subscriptions
echo "[5/6] Listing user's subscriptions..."
LIST_RESPONSE=$(curl -s -X GET "$API_URL/api/my-subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  --insecure)

SUB_COUNT=$(echo "$LIST_RESPONSE" | jq '.subscriptions | length' 2>/dev/null || echo "0")
if [ "$SUB_COUNT" -gt 0 ]; then
    echo "✅ Found $SUB_COUNT subscription(s)"
    echo "$LIST_RESPONSE" | jq -r '.subscriptions[] | "   - ID \(.id): \(.state) (\(.product.name // "unknown"))"' 2>/dev/null || true
else
    echo "⚠️  No subscriptions found"
fi
echo ""

# Step 6: Idempotency test
echo "[6/6] Testing idempotency (subscribe again with same user)..."
IDEMPOTENT_RESPONSE=$(curl -s -w "\n%{http_code}" -X POST "$API_URL/api/subscriptions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"planHandle\":\"$PLAN_HANDLE\"}" \
  --insecure)

IDEMPOTENT_CODE=$(echo "$IDEMPOTENT_RESPONSE" | tail -n1)
if [ "$IDEMPOTENT_CODE" = "201" ] || [ "$IDEMPOTENT_CODE" = "200" ]; then
    IDEMPOTENT_BODY=$(echo "$IDEMPOTENT_RESPONSE" | sed '$d')
    IDEMPOTENT_ID=$(echo "$IDEMPOTENT_BODY" | jq '.subscription.id // empty' 2>/dev/null)
    if [ "$IDEMPOTENT_ID" = "$SUB_ID" ]; then
        echo "✅ Idempotency works: duplicate request returned same subscription ID"
    else
        echo "⚠️  Subscription IDs differ (may be multiple subscriptions allowed per user)"
    fi
else
    echo "❌ Idempotency test failed with HTTP $IDEMPOTENT_CODE"
fi
echo ""

# Summary
echo "╔════════════════════════════════════════════════════════════════╗"
echo "║ Verification Complete                                          ║"
echo "╚════════════════════════════════════════════════════════════════╝"
echo ""
echo "If all steps passed, the Maxio integration is working correctly."
echo "Next steps:"
echo "  - Add integration tests to PublicApiIntegrationTests project"
echo "  - Configure monitoring/logging for production"
echo "  - Deploy to staging with real Maxio credentials"
