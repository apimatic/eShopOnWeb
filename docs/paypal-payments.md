# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of eShopOnWeb: collect money for orders through **PayPal**
(direct card processing) and let a shopper **save a card** for reuse. It does not replace the
existing catalog/basket/order flow — it adds the money movement and the operator flows that
follow a real payment (hold → capture → refund/void).

Everything is exposed as JWT-authenticated HTTP endpoints on **`src/PublicApi`**.

## Money flow

| Stage | Endpoint | PayPal action |
|-------|----------|---------------|
| Place order (awaiting payment) | `POST /api/orders` | — (creates an `Order` + `OrderPayment`) |
| Pay (hold) | `POST /api/orders/{id}/pay` | Create order `intent=AUTHORIZE` with `payment_source.card` (raw card **or** `vault_id`) → authorize |
| Fulfil (take money) | `POST /api/orders/{id}/fulfil` | Capture authorization; renews a stale hold via reauthorize first |
| Cancel (release hold) | `POST /api/orders/{id}/cancel` | Void authorization |
| Refund | `POST /api/orders/{id}/refunds` | Refund capture (full or partial) |
| My orders | `GET /api/my-orders` | — |
| Reconciliation | `GET /api/reconciliation?from=&to=` | Transaction Search, paged over the whole range |

Saved cards: `POST /api/payment-methods` (vault), `GET /api/payment-methods`,
`DELETE /api/payment-methods/{id}`.

## Authorization

- **Operator (Administrators role only):** fulfil, cancel, reconciliation.
- **Shopper-scoped (caller's own data only):** place order, pay, refund, my-orders, and all
  payment-method endpoints. One shopper can never see, use, or delete another's orders or cards
  (mismatches return `404`, never leaking existence).

## Design highlights

- **`IPaymentGateway`** (Infrastructure `PayPalGateway`) is the single place the app calls PayPal
  (REST v2 Orders/Payments, v3 Vault, v1 Reporting). OAuth tokens are cached
  (`PayPalTokenProvider`, singleton) and refreshed proactively.
- **`OrderPayment`** is a separate aggregate holding all PayPal-owned state (order/auth/capture
  ids and statuses, captured gross / PayPal fee / net proceeds, and the refunds), so a later
  request can act on the payment without replaying the one that started it. The existing `Order`
  aggregate is unchanged.
- **Idempotent in effect:** a repeated authorize never places a second hold; a repeated capture
  never captures twice; a refund carries a caller idempotency key, and repeating it under the
  same key returns the original refund (two distinct partial refunds remain legitimate). A
  partly-refunded order can never be refunded beyond what was captured.
  `PayPal-Request-Id` values are derived from globally-unique ids (the reconciliation reference,
  the PayPal authorization/capture id) so they never collide with other activity on the account.
- **Amounts** come from catalog prices; the **currency** comes from configuration. The PayPal
  hold equals the order total to the cent (`amount` + item breakdown).
- **Reconciliation** tags every PayPal order with `invoice_id`/`custom_id` =
  `ESHOP-{orderId}-{guid}`, then lines PayPal's transaction records up against eShop payments,
  surfacing `Matched`, `MissingInEShop`, and `MissingInPayPal`. It pages through the entire
  range (chunked into ≤31-day windows). PayPal reporting lags live activity, so a range covering
  just-created payments can legitimately come back empty — that is expected, not a gap.
- **3-D Secure / browser challenge:** if PayPal answers a card payment with
  `PAYER_ACTION_REQUIRED`/a `payer-action` link, the pay call returns `422` with a clear message
  instead of building an approval round-trip.

## Card-data handling

Full card details are **never** stored in the app database and **never** logged. Only a safe
descriptor (brand, last four, expiry, cardholder name) is stored for saved cards; the card itself
lives only in PayPal's vault, referenced by a vault token id.

## Configuration

Settings bind from the `PayPal:` section (no values are committed to the repo):

| Key | From env var |
|-----|--------------|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox`/`production`) |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | optional — when set, used verbatim as the API base for **every** call |

Load them into .NET user-secrets for the PublicApi project:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
# dotnet user-secrets set "PayPal:BaseUrl"    "$PAYPAL_BASE_URL"   # only if overriding
```

When `PayPal:BaseUrl` is unset the base URL is derived from `PayPal:Environment`
(sandbox → `https://api-m.sandbox.paypal.com`).

## Running (this machine)

Runs entirely in-memory (no SQL Server LocalDB). `appsettings.Development.json` sets
`UseOnlyInMemoryDatabase: true`. With the in-memory provider each host keeps its own store and
loses it on restart, so create, pay, fulfil and refund orders within a single run.

```bash
cd src/PublicApi
# ASP.NET Core 8.0 runtime present -> runs directly; otherwise add DOTNET_ROLL_FORWARD=Major
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:9963;http://localhost:9964" \
dotnet run --no-launch-profile
```

Swagger: `https://localhost:9963/swagger`.

## Persistence note

New entities (`OrderPayment`, `PaymentRefund`, `SavedPaymentMethod`) are registered on
`CatalogContext` with `IEntityTypeConfiguration`s in `src/Infrastructure/Data/Config`. The
in-memory provider needs no migration. For a SQL Server deployment add one:
`dotnet ef migrations add AddPayments -p src/Infrastructure -s src/PublicApi`.
