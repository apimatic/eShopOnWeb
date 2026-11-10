# Pay by bank (Plaid)

Endpoints under `/api/` (all need a JWT; support endpoints need the `Administrators` role):

| Route | Purpose |
|---|---|
| `GET /api/banks?query=` | Bank picker (cached full list, name search) |
| `POST /api/bank/link-token` | Plaid Link token (`{ "purpose": "bank" \| "brokerage" }`) |
| `POST /api/bank/connections` | Save a connection from Link's `publicToken` |
| `GET /api/bank/connections` | The caller's connections |
| `DELETE /api/bank/connections/{connectionId}` | Disconnect (removes the Plaid item) |
| `POST /api/checkout/bank-payment` | Start: `connectionId`, `items[{catalogItemId, quantity}]`, `shippingCost` -> `total`, `options` |
| `PUT /api/checkout/bank-payment/{checkoutId}/shipping` | Re-price the shipping |
| `POST /api/checkout/bank-payment/{checkoutId}/confirm` | `{ "speed": "standard" \| "express" }` -> `orderId`, `fee`, `arrivesOn` |
| `POST /api/orders/{orderId}/cancel` | Cancel before shipping; full refund |
| `GET /api/support/shoppers/{email}/bank-activity` | Shop's synced copy of bank activity |
| `GET /api/support/shoppers/{email}/transactions?from=&to=` | Live transactions (YYYY-MM-DD) |
| `GET /api/portfolio` | Brokerage holdings (`contracts` for options, `quantity` otherwise) |

Failures the app can't carry out answer 4xx with `{ "statusCode", "message", "reason" }`.

## Settings

`Plaid:ClientId`, `Plaid:Secret`, `Plaid:Environment` (`sandbox` or `production`) and the optional
`Plaid:BaseUrl` (used verbatim when set). Load the sandbox credentials into user-secrets from the
`PLAID_CLIENT_ID`, `PLAID_SECRET` and `PLAID_ENV` environment variables (never into a file in the repo):

```
cd src/PublicApi
dotnet user-secrets set "Plaid:ClientId" "$PLAID_CLIENT_ID"
dotnet user-secrets set "Plaid:Secret" "$PLAID_SECRET"
dotnet user-secrets set "Plaid:Environment" "$PLAID_ENV"
```

`BankPayments:ExpressFee` (default 5.00) is the flat fee for express (same-day ACH).

## Notes

* Express (same-day ACH) is offered only while `total + fee <= 1,000,000`; standard up to 10,000,000 (Plaid's limits).
* The Plaid authorization is created at confirm time from the current total, so a shipping change is always charged.
* Plaid access tokens are stored protected (ASP.NET Core data protection).
* The SDK serializes `start_date`/`end_date` as date-times, which Plaid rejects; `PlaidDateFormatHandler` trims them.
* Integration tests (`tests/PublicApiIntegrationTests/BankEndpoints`) run against the Plaid sandbox.
