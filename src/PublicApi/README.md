# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

The payment endpoints under `/api/` (payment methods, orders, checkout, pay, refunds, invoice schedules, sellers,
account close) call the Stax REST API over HTTP. Settings come from the `Stax:` configuration section:
`Stax:ApiKey`, `Stax:PlatformApiKey` (only needed for `POST /api/admin/sellers`) and the optional `Stax:BaseUrl` override.

Load them into user-secrets from the environment (never commit the values):

    dotnet user-secrets set "Stax:ApiKey" "$STAX_API_KEY" --project src/PublicApi
    dotnet user-secrets set "Stax:PlatformApiKey" "$STAX_PLATFORM_API_KEY" --project src/PublicApi

Notes:
- Bank payments stay `pending` until the bank settles them; a background service (`PaymentSyncService`) and
  `GET /api/orders/{id}` pick up the settlement. The same service retries refunds Stax cannot do yet (ACH refunds
  are refused until 5 business days after the payment) until the shopper has the whole amount back.
- Orders and the shopper-to-Stax-customer link are stored in a separate `PaymentsDbContext`
  (`ConnectionStrings:PaymentsConnection`, created on startup).
