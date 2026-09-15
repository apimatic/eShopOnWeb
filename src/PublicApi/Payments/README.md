# PayPal payments in PublicApi

PublicApi implements a two-step PayPal card flow: checkout authorizes the exact catalog-derived order total, fulfillment captures it, cancellation voids an uncaptured authorization, and refunds return all or part of a capture. Card vault tokens are owned by the authenticated shopper; the application stores only the PayPal token, brand, last four digits, and expiry.

## Configuration

The integration binds the `PayPal` section with these keys:

- `PayPal:ClientId`
- `PayPal:ClientSecret`
- `PayPal:Environment`
- `PayPal:Currency`
- `PayPal:BaseUrl` (optional override used for every call, including OAuth)

For local development, load the supplied environment variables without placing their values in the repository:

```powershell
dotnet user-secrets set "PayPal:ClientId" "$env:PAYPAL_CLIENT_ID" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" "$env:PAYPAL_CLIENT_SECRET" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment" "$env:PAYPAL_ENVIRONMENT" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency" "$env:PAYPAL_CURRENCY" --project src/PublicApi/PublicApi.csproj
```

Run one PublicApi process for a complete in-memory test:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile PublicApi
```

Authenticate at `POST /api/authenticate` as `demouser@microsoft.com` for shopper calls and `admin@microsoft.com` for operator calls. The seeded development password is the existing `AuthorizationConstants.DEFAULT_PASSWORD` value. Put the returned token in `Authorization: Bearer <token>`.

## API sequence

1. `POST /api/orders` with catalog item IDs, quantities, and a shipping address. Save the top-level `orderId`.
2. `POST /api/orders/{orderId}/pay` with exactly one of:
   - `card`: `name`, `number`, `expiry` (`yyyy-MM`), `securityCode`, and `billingAddress`.
   - `paymentMethodId`: a top-level ID returned by the vault endpoint.
3. As an administrator, call `POST /api/orders/{orderId}/fulfil` to capture, or `POST /api/orders/{orderId}/cancel` to release the hold.
4. After capture, the owning shopper calls `POST /api/orders/{orderId}/refunds` with `idempotencyKey` and optional `amount`. Omitting `amount` refunds the remaining balance.
5. Inspect shopper state at `GET /api/my-orders`.

To save and reuse a card, call `POST /api/payment-methods` with `{ "card": { ... } }`, use the returned top-level `paymentMethodId` in step 2 for another order, list it with `GET /api/payment-methods`, and remove it with `DELETE /api/payment-methods/{paymentMethodId}`.

Administrators can reconcile any ISO-8601 range with `GET /api/reconciliation?from=...&to=...`. The client splits ranges into PayPal's 31-day windows and exhausts every page. `Matched`, `PayPalOnly`, and `localOnly` results make discrepancies explicit. Recently-created sandbox transactions may not appear until PayPal's reporting delay has elapsed.
