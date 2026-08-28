# Public API

The PublicApi host exposes the catalog endpoints plus a JWT-authenticated PayPal order and
saved-card workflow. Card numbers and security codes are sent directly to PayPal and are never
persisted by eShopOnWeb; only PayPal IDs and masked card metadata are stored locally.

## PayPal configuration

Configuration binds from the `PayPal` section with these keys:

- `PayPal:ClientId`
- `PayPal:ClientSecret`
- `PayPal:Environment` (`Sandbox` or `Live`)
- `PayPal:Currency`
- `PayPal:BaseUrl` (optional; overrides the base address for every PayPal call, including OAuth)

For local development, copy the corresponding `PAYPAL_*` environment variables to user-secrets:

```powershell
dotnet user-secrets set --project src/PublicApi "PayPal:ClientId" "$env:PAYPAL_CLIENT_ID"
dotnet user-secrets set --project src/PublicApi "PayPal:ClientSecret" "$env:PAYPAL_CLIENT_SECRET"
dotnet user-secrets set --project src/PublicApi "PayPal:Environment" "$env:PAYPAL_ENVIRONMENT"
dotnet user-secrets set --project src/PublicApi "PayPal:Currency" "$env:PAYPAL_CURRENCY"
dotnet user-secrets set --project src/PublicApi "UseOnlyInMemoryDatabase" "true"
```

## Payment endpoints

- `POST /api/orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/fulfil` (administrator)
- `POST /api/orders/{orderId}/cancel` (administrator)
- `POST /api/orders/{orderId}/refunds`
- `GET /api/my-orders`
- `POST`, `GET`, `DELETE /api/payment-methods`
- `GET /api/reconciliation?from=...&to=...` (administrator)

Authenticate at `POST /api/authenticate` and use the returned token as a Bearer token. Swagger
documents the complete request and response schemas. Refund requests require a unique
`idempotencyKey`; reuse the same key when retrying the same refund.
