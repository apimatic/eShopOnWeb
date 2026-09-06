#!/bin/bash

# Maxio Integration Verification Script
# This script verifies that the Maxio billing integration is properly set up and working

set -e

echo "=========================================="
echo "Maxio Integration Verification"
echo "=========================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check 1: Environment Variables
echo -e "${YELLOW}[1/4] Checking environment variables...${NC}"
if [ -z "$MAXIO_API_KEY" ]; then
    echo -e "${RED}✗ MAXIO_API_KEY not set${NC}"
    echo "   Set it with: export MAXIO_API_KEY='your-api-key'"
    exit 1
else
    echo -e "${GREEN}✓ MAXIO_API_KEY is set${NC}"
fi

if [ -z "$MAXIO_SITE_SUBDOMAIN" ]; then
    echo -e "${RED}✗ MAXIO_SITE_SUBDOMAIN not set (should be 'cp-exp-2')${NC}"
    exit 1
else
    echo -e "${GREEN}✓ MAXIO_SITE_SUBDOMAIN is set to: $MAXIO_SITE_SUBDOMAIN${NC}"
fi

# Check 2: Build
echo ""
echo -e "${YELLOW}[2/4] Building PublicApi project...${NC}"
cd "$(dirname "$0")"
if dotnet build src/PublicApi/PublicApi.csproj -c Release > /dev/null 2>&1; then
    echo -e "${GREEN}✓ Build successful${NC}"
else
    echo -e "${RED}✗ Build failed${NC}"
    exit 1
fi

# Check 3: Files exist
echo ""
echo -e "${YELLOW}[3/4] Verifying integration files...${NC}"
files_to_check=(
    "src/PublicApi/MaxioConfiguration.cs"
    "src/PublicApi/Services/MaxioClient.cs"
    "src/PublicApi/SubscriptionEndpoints/SubscriptionPlansEndpoint.cs"
    "src/PublicApi/SubscriptionEndpoints/CreateSubscriptionEndpoint.cs"
    "src/PublicApi/SubscriptionEndpoints/ListSubscriptionsEndpoint.cs"
)

all_files_exist=true
for file in "${files_to_check[@]}"; do
    if [ -f "$file" ]; then
        echo -e "${GREEN}✓ $file${NC}"
    else
        echo -e "${RED}✗ $file (missing)${NC}"
        all_files_exist=false
    fi
done

if [ "$all_files_exist" = false ]; then
    exit 1
fi

# Check 4: Configuration in appsettings.json
echo ""
echo -e "${YELLOW}[4/4] Verifying appsettings.json configuration...${NC}"
if grep -q '"Maxio"' src/PublicApi/appsettings.json; then
    echo -e "${GREEN}✓ Maxio configuration section found${NC}"
else
    echo -e "${RED}✗ Maxio configuration section not found${NC}"
    exit 1
fi

# Success
echo ""
echo -e "${GREEN}=========================================="
echo "All checks passed! ✓"
echo "==========================================${NC}"
echo ""
echo "Next steps:"
echo "1. Set user-secrets (optional, alternative to env vars):"
echo "   cd src/PublicApi"
echo "   dotnet user-secrets set \"Maxio:ApiKey\" \"\$MAXIO_API_KEY\""
echo ""
echo "2. Run the PublicApi service:"
echo "   cd src/PublicApi"
echo "   dotnet run"
echo ""
echo "3. See MAXIO_INTEGRATION_SETUP.md for detailed testing instructions"
echo ""
