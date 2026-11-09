# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

Payments call the Stax API over plain HTTP. Settings come from the `Stax:` configuration section (never commit values):

| Key | Purpose |
| --- | --- |
| `Stax:ApiKey` | merchant API key (sandbox credentials) |
| `Stax:PlatformApiKey` | partner key, only needed for `POST /api/admin/sellers` |
| `Stax:BaseUrl` | optional override of the API base address (used verbatim) |

Load them into user-secrets, e.g. `dotnet user-secrets set "Stax:ApiKey" "$STAX_API_KEY" --project src/PublicApi` (and `STAX_PLATFORM_API_KEY` for the platform key).
