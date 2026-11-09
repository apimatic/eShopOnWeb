# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

The payment endpoints (`/api/payment-methods`, `/api/orders`, `/api/account/close`, `/api/admin/...`) call Stax through the SDK in `sdk/stax`.
Settings are read from the `Stax:` section: `Stax:ApiKey`, `Stax:PlatformApiKey` (needed only for `POST /api/admin/sellers`) and `Stax:BaseUrl` (optional; used verbatim as the API base address).
Keep the values in user-secrets, never in the repository:

```
dotnet user-secrets set "Stax:ApiKey" "<value of STAX_API_KEY>"
dotnet user-secrets set "Stax:PlatformApiKey" "<value of STAX_PLATFORM_API_KEY>"
```

When a request cannot be carried out the API answers with a 4xx status and `{ "statusCode", "message" }`.
