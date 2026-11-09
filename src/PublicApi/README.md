# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

Payments call the Stax HTTP API directly (no SDK). Settings are read from the `Stax:` section:

| Key | Purpose |
| --- | --- |
| `Stax:ApiKey` | Merchant API key (sandbox credentials while testing) |
| `Stax:PlatformApiKey` | Partner key, only needed for `POST /api/admin/sellers` |
| `Stax:BaseUrl` | Optional override of the API base address, used verbatim |

Load them into user-secrets, never into a file:

```
dotnet user-secrets set "Stax:ApiKey" "$STAX_API_KEY" --project src/PublicApi
dotnet user-secrets set "Stax:PlatformApiKey" "$STAX_PLATFORM_API_KEY" --project src/PublicApi
```

Shopper endpoints need any valid JWT; `/api/admin/*` needs the Administrators role. When a request can't be carried
out the endpoint answers 4xx with `{ statusCode, message }`. Bank payments are marked paid by a background worker
(`PaymentSyncWorker`) once Stax reports them settled, and refunds Stax can't yet carry out (bank payments can't be
returned for several business days) stay `pending` and are completed by the same worker.
