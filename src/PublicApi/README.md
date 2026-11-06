# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Billing endpoints (`/api/plans`, `/api/subscriptions/...`, `/api/admin/plans`, `/api/admin/subscriptions/...`) call Maxio through the SDK in `sdk/maxio`.
Settings are read from the `Maxio:` configuration section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US`/`EU`) and the optional `Maxio:BaseUrl` (used verbatim as the API base address when set).
Keep them in user-secrets, never in the repository:

    dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi
    dotnet user-secrets set "Maxio:Subdomain" "<subdomain>" --project src/PublicApi
    dotnet user-secrets set "Maxio:Environment" "US" --project src/PublicApi

The integration tests in `tests/PublicApiIntegrationTests/BillingEndpoints` run against the Maxio sandbox using those secrets.
