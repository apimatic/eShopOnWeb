# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The API implements an authorize-at-checkout/capture-at-fulfilment flow. `Order` remains the
commerce aggregate and now stores PayPal order, authorization, capture and refund state.
Saved cards store only a PayPal vault token plus brand, last four digits and expiry; PAN and
security code are sent directly to PayPal and are never persisted or logged.

Configuration is bound from the `PayPal` section:

- `PayPal:ClientId`
- `PayPal:ClientSecret`
- `PayPal:Environment` (`Sandbox` or `Live`)
- `PayPal:Currency`
- `PayPal:BaseUrl` (optional; when present, this exact base is used for OAuth and every API call)

For local development, load the supplied environment variables into user-secrets without
placing their values in the repository:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi
dotnet user-secrets set "UseOnlyInMemoryDatabase" true --project src/PublicApi
```

Run PublicApi in `Development`, authenticate at `POST /api/authenticate`, and send its token as
`Authorization: Bearer <token>`. The shopper flow is:

1. `POST /api/payment-methods` with `card`, then retain the top-level `paymentMethodId`.
2. `POST /api/orders` with `items` and `shipToAddress`, then retain `orderId`.
3. `POST /api/orders/{orderId}/pay` with either `card` or `paymentMethodId`.
4. Authenticate as the administrator and `POST /api/orders/{orderId}/fulfil` to capture, or
   `POST /api/orders/{orderId}/cancel` to void the hold.
5. For a fulfilled order, `POST /api/orders/{orderId}/refunds` as its shopper with a unique
   `Idempotency-Key` header and an optional `amount` body field.

`GET /api/my-orders` exposes payment state and PayPal-reported capture amount, fee and net
proceeds. Administrator-only `GET /api/reconciliation?from=<ISO-8601>&to=<ISO-8601>` exhausts
all PayPal pages and splits ranges longer than PayPal's 31-day request limit. PayPal reporting
can lag by three hours, so the newest portion of a requested range can legitimately contain
eShop-only entries until PayPal makes those transactions reportable.
