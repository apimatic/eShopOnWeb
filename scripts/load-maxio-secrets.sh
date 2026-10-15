#!/usr/bin/env bash
# Loads the Maxio sandbox settings from the environment into .NET user-secrets for src/PublicApi.
# Reads MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN and MAXIO_DEFAULT_PRODUCT_FAMILY; stores nothing in the repository.
set -euo pipefail
cd "$(dirname "$0")/../src/PublicApi"
: "${MAXIO_API_KEY:?MAXIO_API_KEY is not set}" "${MAXIO_SITE_SUBDOMAIN:?MAXIO_SITE_SUBDOMAIN is not set}" "${MAXIO_DEFAULT_PRODUCT_FAMILY:?MAXIO_DEFAULT_PRODUCT_FAMILY is not set}"
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" > /dev/null
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" > /dev/null
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" > /dev/null
# Optional override of the API base address:
[ -z "${MAXIO_BASE_URL:-}" ] || dotnet user-secrets set "Maxio:BaseUrl" "$MAXIO_BASE_URL" > /dev/null
echo "Maxio user-secrets loaded."
