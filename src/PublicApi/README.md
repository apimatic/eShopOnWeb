# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Maxio subscription billing

`SubscriptionEndpoints/` exposes recurring subscriptions backed by Maxio Advanced Billing (JWT required):

| Route | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans in the configured product family |
| `POST /api/subscriptions` `{ "planHandle": "..." }` | Subscribe the caller (idempotent per user and plan) |
| `GET /api/my-subscriptions` | The caller's subscriptions with live state |

Configuration (`Maxio:` section; the host refuses to start without the first three):
`Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, optional `Maxio:BaseUrl` (used verbatim when set) and `Maxio:Environment` (`US` default, or `EU`).
Load them with .NET user-secrets, e.g. `dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi`, or the `Maxio__ApiKey` style environment variables.
The Maxio .NET SDK is referenced from the maxio-advanced-billing plugin (`MaxioSdkProject` MSBuild property in `Infrastructure.csproj`).
