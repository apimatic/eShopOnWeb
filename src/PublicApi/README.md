# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Subscription endpoints (`/api/plans`, `/api/subscriptions/...`, `/api/admin/plans`, `/api/admin/subscriptions/...`) call the Maxio API over HTTP. Configure the `Maxio:` section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US` or `EU`) and optionally `Maxio:BaseUrl` (used verbatim when set). Keep the values out of the repository, e.g.:

```
dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi
```
