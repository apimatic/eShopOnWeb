# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The JWT-authenticated payment API is rooted at `/api`:

- Shopper: `POST /orders`, `POST /orders/{id}/pay`, `POST /orders/{id}/refunds`,
  `GET /my-orders`, and create/list/delete `/payment-methods`.
- Administrator: `POST /orders/{id}/fulfil`, `POST /orders/{id}/cancel`, and
  `GET /reconciliation?from=...&to=...`.

Payment configuration binds from the `PayPal` section. Load the supplied environment variables
into this project's user-secrets without placing values in the repository:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi
```

`PayPal:BaseUrl` is an optional override. When omitted, `PayPal:Environment` selects PayPal's
sandbox or live REST base URL. The override is applied to OAuth and every other PayPal call.

Direct card requests require PCI SAQ D controls. The application does not persist PAN or CVC;
saved methods retain only the PayPal vault token and masked card metadata.
