# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment API uses PayPal Orders v2 for authorization, Payments v2 for capture, void, reauthorization and refunds, Payment Method Tokens v3 for saved cards, and Transaction Search v1 for reconciliation. Card numbers and security codes are forwarded to PayPal only for the current request. The application persists only PayPal identifiers and safe card metadata (brand, last four digits and expiry).

PublicApi binds these settings from the `PayPal` configuration section:

- `PayPal:ClientId`
- `PayPal:ClientSecret`
- `PayPal:Environment` (`Sandbox`, `Live`, or `Production`)
- `PayPal:Currency`
- `PayPal:BaseUrl` (optional; when set, every PayPal call including OAuth uses this base URL)

For local development, copy environment-provided credentials to the existing PublicApi user-secret store without putting their values in the repository:

```powershell
dotnet user-secrets set "PayPal:ClientId" "$env:PAYPAL_CLIENT_ID" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" "$env:PAYPAL_CLIENT_SECRET" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment" "$env:PAYPAL_ENVIRONMENT" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency" "$env:PAYPAL_CURRENCY" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "UseOnlyInMemoryDatabase" "true" --project src/PublicApi/PublicApi.csproj
```

All payment routes require a JWT from `POST /api/authenticate`. Shopper routes use the token username and never accept a buyer ID from the request. `fulfil`, `cancel`, and `reconciliation` additionally require the `Administrators` role.

The payment routes are:

- `POST /api/orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/fulfil`
- `POST /api/orders/{orderId}/cancel`
- `POST /api/orders/{orderId}/refunds`
- `GET /api/my-orders`
- `POST`, `GET /api/payment-methods`
- `DELETE /api/payment-methods/{paymentMethodId}`
- `GET /api/reconciliation?from={from}&to={to}`

Refund requests require a caller-supplied `idempotencyKey`. Reconciliation automatically reads every page and divides ranges longer than PayPal's 31-day maximum into non-overlapping windows.
