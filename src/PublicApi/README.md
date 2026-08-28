# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

Payment endpoints are JWT-authenticated and use the PayPal OpenAPI contracts in
`api-specs/paypal`. The integration does not use a PayPal SDK and never persists card numbers
or security codes.

Configure the PublicApi user-secret store from the supplied process environment:

```powershell
dotnet user-secrets set "PayPal:ClientId" "$env:PAYPAL_CLIENT_ID" --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$env:PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" "$env:PAYPAL_ENVIRONMENT" --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" "$env:PAYPAL_CURRENCY" --project src/PublicApi
```

`PayPal:BaseUrl` is an optional override. When present, it is the base for OAuth and every API
request. For this repository's in-memory development mode:

```powershell
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --project src/PublicApi
```

Authenticate at `POST /api/authenticate`, then drive the flow through `POST /api/orders`,
`POST /api/orders/{id}/pay`, operator `POST /api/orders/{id}/fulfil` or `cancel`, and
`POST /api/orders/{id}/refunds`. Saved cards use `POST`, `GET`, and `DELETE`
`/api/payment-methods`; only PayPal's vault token plus safe card display data is stored.
