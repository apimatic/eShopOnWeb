# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Subscriptions are billed through Maxio Advanced Billing, called with the C# SDK shipped in the **maxio** plugin
(`PublicApi.csproj` has a `ProjectReference` to it; re-point it with `dotnet remove/add reference` if the plugin lives
elsewhere on your machine). Settings come from the `Maxio:` configuration section — never from files in the repo:

| Key | Meaning |
| --- | --- |
| `Maxio:ApiKey` | Maxio API key |
| `Maxio:Subdomain` | Maxio site subdomain |
| `Maxio:Environment` | `US` or `EU` |
| `Maxio:BaseUrl` | optional; used verbatim as the API base address instead of one derived from the subdomain |

Load them with `dotnet user-secrets set "Maxio:ApiKey" "<value>" --project src/PublicApi` (or the `Maxio__ApiKey`
environment variable). The host refuses to start when a required key is missing.

Endpoints: `GET /api/plans`; `POST /api/subscriptions`; `GET /api/subscriptions/{id}`;
`POST /api/subscriptions/{id}/{pause|resume|cancel|restart|switch}`; `GET /api/subscriptions/{id}/usage?date=YYYY-MM-DD`;
staff (`Administrators`): `POST /api/admin/plans`, `POST /api/admin/plans/{planId}/currency-prices`,
`PUT /api/admin/subscriptions/{id}/next-billing-date`. See `maxio-advanced-billing-plan.md` for the design decisions.
