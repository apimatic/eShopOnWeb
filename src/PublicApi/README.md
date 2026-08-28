# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

PublicApi supports a two-step PayPal card flow: authorize at checkout, capture on fulfilment,
void on cancellation, and refund a completed capture. It also supports PayPal-vaulted cards.
Card number and security code are forwarded to PayPal only for the active request and are never
stored by eShopOnWeb.

Configuration is bound from the `PayPal` section. For local development, copy the supplied
environment variables into this project's user-secrets store:

```powershell
dotnet user-secrets set "PayPal:ClientId" "$env:PAYPAL_CLIENT_ID" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" "$env:PAYPAL_CLIENT_SECRET" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment" "$env:PAYPAL_ENVIRONMENT" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency" "$env:PAYPAL_CURRENCY" --project src/PublicApi/PublicApi.csproj
```

`PayPal:BaseUrl` is optional. When present, all calls—including OAuth token requests—use that
base URL. Otherwise `PayPal:Environment` selects PayPal's Sandbox or Live API base URL.

Run PublicApi with `UseOnlyInMemoryDatabase=true`, authenticate at `POST /api/authenticate`, and
send the returned bearer token to the payment endpoints. The end-to-end order is:

1. `POST /api/orders`
2. `POST /api/orders/{orderId}/pay`
3. `POST /api/orders/{orderId}/fulfil` as an administrator
4. `POST /api/orders/{orderId}/refunds` as the shopper

Use `POST`, `GET`, and `DELETE /api/payment-methods` to vault, list, and remove cards. Use
`GET /api/reconciliation?from=...&to=...` as an administrator. Reconciliation automatically
pages all PayPal results and splits ranges longer than PayPal's 31-day transaction-search limit.
