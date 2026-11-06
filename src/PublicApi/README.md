# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Billing endpoints (`/api/plans`, `/api/subscriptions/...`, `/api/admin/plans/...`, `/api/admin/subscriptions/...`) call Maxio through the SDK in `sdk/maxio`.
Settings come from the `Maxio:` configuration section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US` or `EU`) and the optional `Maxio:BaseUrl` (used verbatim as the API base address when set).
Keep the values in .NET user-secrets, never in the repository:

```
dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<site subdomain>" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "US" --project src/PublicApi
```
