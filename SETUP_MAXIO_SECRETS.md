# Maxio Configuration Setup

## Prerequisites

Make sure you have the following environment variables set:
- `MAXIO_API_KEY` - Your Maxio API key
- `MAXIO_SITE_SUBDOMAIN` - Your Maxio sandbox subdomain
- `MAXIO_ENVIRONMENT` - The environment name (usually "sandbox")
- `MAXIO_DEFAULT_PRODUCT_FAMILY` - The product family handle (e.g., "eshop-subscribe")

## Setup Instructions

### PowerShell Setup

Run the following commands in PowerShell from the repository root:

```powershell
cd src/PublicApi

# Set Maxio configuration from environment variables
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY"
# Optionally set BaseUrl if you need to override the default constructed URL
# dotnet user-secrets set "Maxio:BaseUrl" "https://cp-exp-2.chargify.com"

# Verify secrets were set
dotnet user-secrets list
```

### .NET Runtime Configuration

When running the application, also set the database mode:

```powershell
# For in-memory database (recommended for development without LocalDB)
$env:UseOnlyInMemoryDatabase = "true"

# Then run the application
dotnet run --project src/PublicApi/PublicApi.csproj
```

## Verification

After setup, you can verify the Maxio service is working by:

1. Starting the application with `dotnet run`
2. Accessing the Swagger UI at `https://localhost:25323/swagger`
3. Authenticating with test credentials
4. Testing the `/api/subscription-plans` endpoint
