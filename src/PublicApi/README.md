# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

The billing endpoints (`/api/plans`, `/api/subscriptions/...`, `/api/admin/plans/...`, `/api/admin/subscriptions/...`) call Maxio through the Maxio Advanced Billing C# SDK.
Settings are read from the `Maxio:` configuration section — `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US` or `EU`) and the optional `Maxio:BaseUrl` (used verbatim when set). The host refuses to start without the first three. Keep them in user-secrets or your secret store, never in `appsettings*.json`:

```
dotnet user-secrets set "Maxio:ApiKey" "<value of MAXIO_API_KEY>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<value of MAXIO_SITE_SUBDOMAIN>" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "<value of MAXIO_ENVIRONMENT>" --project src/PublicApi
```

Outside production the app refuses a Maxio site that is not in test mode. Billing writes use a `MaxioClaims` table (migration `AddMaxioClaims`).
