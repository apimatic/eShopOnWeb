# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment client is hand-written against the repository's PayPal OpenAPI contracts:

- `api-specs/paypal/checkout_orders_v2` for card authorization
- `api-specs/paypal/payments_payment_v2` for capture, reauthorization, void, and refund
- `api-specs/paypal/vault_payment_tokens_v3` for saved cards
- `api-specs/paypal/transaction_search_v1` for reconciliation

No PayPal SDK is used. Configure the client through `PayPal:ClientId`,
`PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, and optional
`PayPal:BaseUrl`. PublicApi maps `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`,
`PAYPAL_ENVIRONMENT`, `PAYPAL_CURRENCY`, and optional `PAYPAL_BASE_URL` to those
keys. Values can instead be copied from environment variables into user-secrets:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi
```

For this repository's in-memory development mode, start PublicApi with
`UseOnlyInMemoryDatabase=true`. All steps for an order must run against that same
host process because its data is intentionally ephemeral.
