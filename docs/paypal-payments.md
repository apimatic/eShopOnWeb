# PayPal payments in PublicApi

PublicApi supports an authorize-at-checkout, capture-at-fulfilment payment lifecycle and
PayPal-vaulted cards. Card numbers and security codes are forwarded to PayPal in memory and
are never persisted by eShopOnWeb. Saved methods contain only a PayPal vault token and safe
display metadata.

## Configuration

`src/PublicApi` binds the `PayPal` section with these keys:

| Key | Purpose |
| --- | --- |
| `PayPal:ClientId` | REST app client ID |
| `PayPal:ClientSecret` | REST app secret |
| `PayPal:Environment` | `Sandbox` or `Live` |
| `PayPal:Currency` | Three-letter order currency, such as `USD` |
| `PayPal:BaseUrl` | Optional API-base override used for every call, including OAuth |

For local development, copy the supplied environment variables into the PublicApi user-secret
store without putting their values in the repository:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi
dotnet user-secrets set "UseOnlyInMemoryDatabase" "true" --project src/PublicApi
```

`PayPal:BaseUrl`, when present, is used verbatim as the base for OAuth, Orders, Payments,
Vault, and Transaction Search. When absent, the base is derived from `PayPal:Environment`.

## API sequence

1. Obtain shopper and administrator JWTs with `POST /api/authenticate`.
2. As the shopper, create an order with `POST /api/orders` and catalog item IDs, quantities,
   and a shipping address.
3. Authorize the exact total with `POST /api/orders/{orderId}/pay`, supplying either `card` or
   `paymentMethodId`.
4. As an administrator, capture and fulfil with `POST /api/orders/{orderId}/fulfil`, or release
   an uncaptured hold with `POST /api/orders/{orderId}/cancel`.
5. As the owning shopper, refund a fulfilled order with
   `POST /api/orders/{orderId}/refunds`. Supply a stable `idempotencyKey`; omit `amount` for the
   full remaining amount.
6. Save, list, and delete cards with `POST`, `GET`, and `DELETE /api/payment-methods`.
7. As an administrator, reconcile a complete ISO-8601 range with
   `GET /api/reconciliation?from=...&to=...`.

The reconciliation implementation splits ranges into PayPal-supported windows, reads every
page in each window, de-duplicates boundary records, and reports both PayPal-only and
eShop-only records. Recent sandbox activity can remain eShop-only until PayPal reporting
catches up.

## Operational behavior

- Stable PayPal request IDs prevent retries from creating a second authorization, capture,
  void, or refund. Refund request IDs are deterministically derived from the caller key.
- Each order has a globally unique payment reference, so an in-memory database restart cannot
  reuse an invoice or idempotency key from an earlier PayPal transaction.
- Fulfilment refreshes the authorization from PayPal. After the initial honor period it
  reauthorizes before capture. Once PayPal can no longer reauthorize it, the response tells the
  operator to ask the shopper to call `/pay` again.
- Pending authorizations and captures are retained and refreshed instead of being submitted a
  second time.
- PayPal errors expose only issue codes and the PayPal debug ID; request bodies and card data
  are not logged.
