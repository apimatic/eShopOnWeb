# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Billing endpoints live under `/api/plans`, `/api/subscriptions` and `/api/admin/...` and call Maxio through the Maxio Advanced Billing C# SDK.
Settings come from the `Maxio:` configuration section: `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:Environment` (`US`/`EU`) and the optional `Maxio:BaseUrl` (used verbatim when set).
Keep the values out of the repository, e.g. with `dotnet user-secrets set "Maxio:ApiKey" "<key>"` in `src/PublicApi`. The host refuses to start when they are missing.
The project references the SDK from the maxio plugin's install directory, so that plugin must be installed at that path to build.
