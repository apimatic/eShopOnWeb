# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments and saved cards

PublicApi takes real payments through PayPal (Orders v2, Payments v2, Vault v3, Transaction Search) using the
PayPal Server SDK that ships in the `paypal` plugin (see `src/Infrastructure.PayPal`).

| Endpoint | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from `{ items: [{ catalogItemId, quantity }], shipToAddress }`; returns `orderId`. Starts `AwaitingPayment`. |
| `POST /api/orders/{orderId}/pay` | owner | Authorize (hold) the order total: body `{ card: { number, expiry: "YYYY-MM", securityCode, name, billingAddress } }` **or** `{ paymentMethodId }`. |
| `POST /api/orders/{orderId}/fulfil` | Administrators | Capture the hold; the payment then shows captured amount, PayPal fee and net. Renews a stale hold first. |
| `POST /api/orders/{orderId}/cancel` | Administrators | Before fulfilment: void the hold (no money moves). |
| `POST /api/orders/{orderId}/refunds` | owner | After fulfilment: `{ amount?, idempotencyKey }` (or `Idempotency-Key` header); omit `amount` to refund the rest. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `POST /api/payment-methods` | shopper | Save a card (vaulted at PayPal; only the token, brand, last 4 and expiry are kept). Returns `paymentMethodId`. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{id}` | owner | Remove a saved card; it can no longer be listed or used. |
| `GET /api/reconciliation?from=…&to=…` | Administrators | PayPal's transaction record for the range (every page, 31-day windows) lined up against eShop captures/refunds. |

Errors are JSON `{ statusCode, code, message, payPalDebugId?, issues? }`: 400 invalid input, 404 not found (another
shopper's order/card is reported as not found), 409 state conflict or operation in progress, 422 PayPal refused
(e.g. `INSTRUMENT_DECLINED`), 502 PayPal unavailable, 504 PayPal did not respond (every request spends at most
25 s waiting on PayPal; repeating the same request settles the outcome and never charges twice).

### Configuration

Settings bind from the `PayPal:` section — `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`
(`sandbox`), `PayPal:Currency` (ISO-4217) and the optional `PayPal:BaseUrl` (used verbatim for every PayPal call,
the OAuth token request included). The host refuses to start when a required value is missing. Never put the
values in a file in this repository; for development load them into user-secrets from the environment:

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project src/PublicApi
```

Elsewhere use `PayPal__ClientId`-style environment variables or a secret store; `PAYPAL_CLIENT_ID`,
`PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT` and `PAYPAL_CURRENCY` also fill any key no other source set.
The SDK client is built once, so a rotated secret takes effect after a restart.

The SDK is referenced as a project from the plugin install (`../marketplace/plugins/paypal/sdk/dotnet` relative
to the repository); on another machine pass `-p:PayPalServerSdkProject=<path to PayPalServerSdk.csproj>`.

### Running locally

```bash
export DOTNET_ROLL_FORWARD=Major UseOnlyInMemoryDatabase=true
dotnet run --project src/PublicApi --launch-profile PublicApi     # https://localhost:39823
```

New tables (`Payments`, `PaymentRefunds`, `PaymentMethods`, `PaymentClaims`) and `Orders.Status` come with the
`AddPaymentsAndSavedCards` migration for SQL Server deployments.

