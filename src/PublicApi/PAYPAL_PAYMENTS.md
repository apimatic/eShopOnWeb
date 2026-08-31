# PayPal payments

PublicApi provides a headless PayPal card flow backed by the Checkout Orders v2, Payments v2,
Vault Payment Tokens v3, and Transaction Search v1 specifications under `api-specs/paypal`.
No PayPal SDK is used.

## Configuration

The integration binds the `PayPal` section using these keys only:

- `PayPal:ClientId`
- `PayPal:ClientSecret`
- `PayPal:Environment`
- `PayPal:Currency`
- `PayPal:BaseUrl` (optional)

Load the environment-provided credentials into PublicApi user-secrets without adding values to
the repository:

```powershell
dotnet user-secrets set 'PayPal:ClientId' $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:ClientSecret' $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Environment' $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set 'PayPal:Currency' $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
```

When `PayPal:BaseUrl` is present, every request—including `/v1/oauth2/token`—uses that base URL.
Otherwise `sandbox` resolves to `https://api-m.sandbox.paypal.com` and `live` resolves to
`https://api-m.paypal.com`.

## Run locally

```powershell
$env:DOTNET_ROLL_FORWARD = 'Major'
$env:UseOnlyInMemoryDatabase = 'true'
dotnet run --project src/PublicApi/PublicApi.csproj
```

Use the PublicApi HTTPS URL printed by the host. Authenticate at `POST /api/authenticate` with
the seeded shopper (`demouser@microsoft.com`) or operator (`admin@microsoft.com`), then send the
returned token as `Authorization: Bearer <token>`.

## Sandbox verification sequence

Keep one PublicApi process running for the complete sequence because the in-memory store is
per-process.

1. `POST /api/payment-methods` as the shopper with `card` containing sandbox Visa
   `4111111111111111`, a future `YYYY-MM` expiry, any 3-digit security code, name, and billing
   address. Retain the top-level `paymentMethodId`; only brand, last four, and expiry are returned.
2. `POST /api/orders` as the shopper with `items` (`catalogItemId`, `quantity`) and
   `shippingAddress`. Retain the top-level `orderId`.
3. `POST /api/orders/{orderId}/pay` as the shopper with the same `card` object. Verify status
   `Authorized`, authorization status `CREATED`, and an authorization amount equal to the order
   total. Repeating this call returns the same authorization.
4. `POST /api/orders/{orderId}/fulfil` as the operator. Verify status `Fulfilled`, capture status
   `COMPLETED`, and populated `capturedAmount`, `payPalFee`, and `netAmount`. Repeating it returns
   the same capture.
5. `POST /api/orders/{orderId}/refunds` as the shopper with, for example,
   `{"idempotencyKey":"return-1","amount":5.00}`. Repeating the key returns the same top-level
   `refundId`; another key can refund another portion, up to the captured total.
6. Create another order and call its `/pay` route with `{"paymentMethodId":<saved id>}`. This
   authorizes without card details. Call `/cancel` as the operator and verify the authorization
   status becomes `VOIDED`.
7. `DELETE /api/payment-methods/{paymentMethodId}` as the shopper. It disappears from
   `GET /api/payment-methods`, and trying to pay with it returns `PAYMENT_METHOD_NOT_FOUND`.
8. `GET /api/my-orders` as the shopper to inspect all lifecycle and PayPal state. Another shopper
   sees none of these orders or saved cards.
9. `GET /api/reconciliation?from=<RFC3339>&to=<RFC3339>` as the operator. PublicApi splits ranges
   into PayPal-supported windows and reads every page. Recent sandbox activity may not yet appear
   because Transaction Search can lag; local payment records remain visible in the report.

If PayPal returns `PAYER_ACTION_REQUIRED`, the API returns a conflict explaining that the issuer
requires a browser challenge. This integration intentionally does not implement an approval
redirect round-trip.
