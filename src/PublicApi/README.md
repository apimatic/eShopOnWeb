# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment API uses PayPal Orders v2, Payments v2, Payment Method Tokens v3, and Transaction Search v1. It creates eShop orders first, authorizes their exact catalog-derived total at `/api/orders/{id}/pay`, captures at `/fulfil`, voids at `/cancel`, and refunds captures at `/refunds`. Card numbers and security codes are request-only values and are never persisted or logged by the application. Only PayPal vault IDs and safe card descriptors are stored.

Configure PublicApi through the `PayPal` section. The app maps the four deployment environment variables into this section automatically; local development can use user-secrets:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "UseOnlyInMemoryDatabase" "true" --project src/PublicApi/PublicApi.csproj
```

The keys are `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, and optional `PayPal:BaseUrl`. When `BaseUrl` is present it is the base address for every PayPal call, including OAuth. Otherwise `Environment` selects PayPal sandbox or live. Development and test runs must use sandbox.

All payment routes except authentication require the PublicApi bearer token. `fulfil`, `cancel`, and `reconciliation` require the `Administrators` role. Orders, payment methods, payment, and refunds are filtered to the token's name claim. Direct server-side card processing and vaulting require the merchant's PayPal account to have Advanced Credit and Debit Card Payments plus Vault enabled, and require the applicable PCI SAQ D controls.

`POST /api/orders` accepts `items` (`catalogItemId`, `quantity`) and `shipToAddress`. `POST /api/orders/{id}/pay` accepts exactly one of `card` or `paymentMethodId`. `POST /api/orders/{id}/refunds` accepts an optional `amount` (omit for the remaining full amount) and a required `idempotencyKey`. `GET /api/reconciliation` accepts ISO-8601 `from` and `to` query values; the implementation partitions ranges into PayPal's 31-day windows and retrieves every page.
