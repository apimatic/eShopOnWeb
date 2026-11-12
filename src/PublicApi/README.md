# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Billing endpoints (`/api/plans`, `/api/subscriptions/...`, `/api/admin/plans/...`, `/api/admin/subscriptions/...`) call Maxio Advanced Billing through the SDK that ships with the maxio plugin. Settings are read from the `Maxio:` configuration section:

| Key | Meaning |
| --- | --- |
| `Maxio:ApiKey` | Maxio API key (secret) |
| `Maxio:Subdomain` | Maxio site subdomain |
| `Maxio:Environment` | `US` or `EU` |
| `Maxio:BaseUrl` | optional; used verbatim as the API base address instead of one derived from the subdomain |

Load them as .NET user-secrets (`dotnet user-secrets set "Maxio:ApiKey" ...` in `src/PublicApi`) and run in the `Development` environment, or supply them as `Maxio__ApiKey` etc. environment variables. Never put the values in a file in the repository. Without them the billing endpoints answer 503 naming the missing keys.

The project references the SDK source inside the plugin install directory, so it builds only where the plugin is installed at that path.
