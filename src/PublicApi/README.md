# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Billing endpoints (`/api/plans`, `/api/subscriptions/...`, `/api/admin/plans/...`, `/api/admin/subscriptions/...`) call Maxio through the Maxio Advanced Billing C# SDK. Settings are read from the `Maxio:` section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US` or `EU`) and the optional `Maxio:BaseUrl` (used verbatim instead of the subdomain-derived address). The host refuses to start when a required one is missing. Keep the values out of the repository, for example with user-secrets:

```
dotnet user-secrets set "Maxio:ApiKey" "<value of MAXIO_API_KEY>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<value of MAXIO_SITE_SUBDOMAIN>" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "<value of MAXIO_ENVIRONMENT>" --project src/PublicApi
```

`PublicApi.csproj` references the SDK source shipped with the maxio plugin by a path on this machine; re-point it with `dotnet remove reference` / `dotnet add reference` where the plugin is installed elsewhere. `tests/PublicApiIntegrationTests/BillingEndpoints` runs against the Maxio sandbox.
