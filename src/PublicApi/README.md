# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

Payment endpoints are JWT-authenticated and live under `/api`:

- `POST /orders`, `POST /orders/{id}/pay`, `GET /my-orders`
- `POST /orders/{id}/fulfil`, `POST /orders/{id}/cancel`, `POST /orders/{id}/refunds`
- `POST /payment-methods`, `GET /payment-methods`, `DELETE /payment-methods/{id}`
- `GET /reconciliation?from={ISO-8601}&to={ISO-8601}`

Fulfil, cancel, and reconciliation require the `Administrators` role. All other operations are restricted to the shopper identity in the JWT. Card numbers and security codes are passed directly to PayPal and are never persisted.

Configuration is bound from the `PayPal` section. `PayPal:BaseUrl` is optional and, when set, is the base address for every call including OAuth. Otherwise `PayPal:Environment` selects PayPal's sandbox or live base address. Load local settings without putting values in this repository:

```powershell
dotnet user-secrets set 'PayPal:ClientId' $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:ClientSecret' $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Environment' $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Currency' $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'UseOnlyInMemoryDatabase' true --project src/PublicApi/PublicApi.csproj
```

PayPal's transaction report is fetched in every page and, for ranges longer than the API's 31-day maximum, in consecutive windows. Newly executed sandbox transactions can take up to three hours to appear.
