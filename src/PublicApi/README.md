# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.



## Maxio subscription billing

Subscriptions are billed through Maxio Advanced Billing (C# SDK shipped with the `maxio` plugin, referenced as a
`ProjectReference` to the plugin's `sdk/dotnet` source: re-point it with `dotnet remove reference` /
`dotnet add reference` where the plugin is installed elsewhere).

Settings are read from the `Maxio:` configuration section; the app refuses to start without `Maxio:ApiKey` and
`Maxio:Subdomain`:

| Key | Meaning |
| --- | --- |
| `Maxio:ApiKey` | Site API key |
| `Maxio:Subdomain` | Site subdomain |
| `Maxio:Environment` | `US` (default) or `EU` |
| `Maxio:BaseUrl` | Optional; when set it is used verbatim as the API base address |

Never put values in a file of the repository. For local development use .NET user-secrets, for example:

```
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "$MAXIO_ENVIRONMENT" --project src/PublicApi
```

Elsewhere use `Maxio__ApiKey`, `Maxio__Subdomain`, ... environment variables. The integration tests build the host
from the `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN` and `MAXIO_ENVIRONMENT` variables and talk to the Maxio sandbox.

A plan is a Maxio product in the product family of a catalog brand (family handle `catalog-brand-{brandId}`).
