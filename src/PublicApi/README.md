# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

Payments use the Stax REST API over plain HTTP (no SDK). Settings come from the `Stax:` configuration section:
`Stax:ApiKey`, `Stax:PlatformApiKey` (only needed for `POST /api/admin/sellers`) and `Stax:BaseUrl` (optional, used verbatim).
Load them into user-secrets, never into files:

```
dotnet user-secrets set "Stax:ApiKey" "<value of STAX_API_KEY>" --project src/PublicApi
dotnet user-secrets set "Stax:PlatformApiKey" "<value of STAX_PLATFORM_API_KEY>" --project src/PublicApi
```

A background service (`PaymentReconciler`) follows bank payments until the bank settles them (the order then shows `paid`)
and retries refunds Stax does not allow yet (bank refunds open some business days after the payment).
`StaxPaymentsTests` run end to end against the Stax sandbox.
