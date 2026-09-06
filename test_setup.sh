#!/bin/bash
# For testing, we'll use dummy Maxio credentials (the actual values don't matter for build verification)
export MAXIO_API_KEY="test_key_12345"
export MAXIO_SITE_SUBDOMAIN="test-sandbox"
export MAXIO_ENVIRONMENT="sandbox"
export MAXIO_DEFAULT_PRODUCT_FAMILY="eshop-subscribe"
export UseOnlyInMemoryDatabase="true"
export DOTNET_ROLL_FORWARD="Major"

echo "Environment configured. Now testing build..."
dotnet build src/PublicApi/PublicApi.csproj 2>&1 | grep -E "error|BUILD|warning|successfully"
