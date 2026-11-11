# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Pay by bank (Plaid)

Endpoints (JWT; `Administrators` role for `support`): `GET /api/banks`, `POST /api/bank/link-token`,
`POST|GET /api/bank/connections`, `DELETE /api/bank/connections/{id}`, `POST /api/checkout/bank-payment`,
`PUT /api/checkout/bank-payment/{checkoutId}/shipping`, `POST /api/checkout/bank-payment/{checkoutId}/confirm`,
`POST /api/orders/{orderId}/cancel`, `GET /api/support/shoppers/{email}/bank-activity`,
`GET /api/support/shoppers/{email}/transactions?from=&to=`, `GET /api/portfolio`.

Plaid is called through the SDK in `sdk/plaid` (needs the .NET 10 SDK to build; `global.json` allows it).

Settings (`Plaid:ClientId`, `Plaid:Secret`, `Plaid:Environment` = `sandbox`|`production`, optional `Plaid:BaseUrl`
used verbatim when set). Keep credentials out of the repository, in user-secrets:

    dotnet user-secrets set Plaid:ClientId "$PLAID_CLIENT_ID"
    dotnet user-secrets set Plaid:Secret "$PLAID_SECRET"
    dotnet user-secrets set Plaid:Environment "$PLAID_ENV"

`BankPayment:ExpressFee` (default 5.00) is the fee the shopper pays for the express (same-day ACH) speed.
Express is only offered while the charge (order + fee) fits the Same Day ACH limit of 1,000,000.
