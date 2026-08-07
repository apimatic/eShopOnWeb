# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of eShopOnWeb's existing Catalog → Basket → Order flow: a
logged-in shopper can place an order, pay for it with PayPal, refund it, and save cards for reuse.
Everything is exposed as JWT-authenticated HTTP endpoints on `src/PublicApi` under `/api/`. The
caller's identity always comes from the token — never from the request body — so a shopper can only
ever act on their own orders and cards.

## Endpoints

| Method & route | Purpose |
| --- | --- |
| `POST /api/orders` | Place an order from catalog item ids + quantities. Prices come from the catalog. Returns `orderId`; order starts `AwaitingPayment`. |
| `POST /api/orders/{orderId}/pay` | Pay with PayPal using either one-off `card` details **or** a saved `paymentMethodId`. |
| `POST /api/orders/{orderId}/refunds` | Full refund of the order's payment → `Refunded`. |
| `GET /api/my-orders` | The caller's orders with payment state. |
| `POST /api/payment-methods` | Save (vault) a card. Returns `paymentMethodId` + safe descriptors (brand/last4/expiry). |
| `GET /api/payment-methods` | The caller's saved cards (safe descriptors only). |
| `DELETE /api/payment-methods/{paymentMethodId}` | Remove a saved card (and delete it from PayPal's vault). |

Amounts are taken from catalog prices, currency USD.

## Design

- **Clean layering.** `IPaymentGateway` (in `ApplicationCore`) abstracts the processor; the PayPal
  HTTP implementation (`PayPalPaymentGateway`) lives in `Infrastructure/Payments`. Orchestration is in
  `ApplicationCore/Services` (`PaymentService`, `PaymentMethodService`, `BuyerService`); endpoints are thin.
- **PayPal APIs used.** Orders v2 (`/v2/checkout/orders` + `/capture`) for card charges, Payments v2
  (`/v2/payments/captures/{id}/refund`) for refunds, and Payment Method Tokens v3
  (`/v3/vault/setup-tokens` → `/v3/vault/payment-tokens`, `DELETE …`) to vault and reuse cards.
- **No card data at rest.** Full card details are a pass-through to PayPal only — never stored in the
  app database and never logged. Saved cards are referenced by a PayPal vault token; the app keeps
  only brand/last4/expiry for display.
- **Idempotent in effect.** Two layers: (1) a domain state guard — an already-paid order is never
  charged again and an already-refunded order is never refunded again; (2) a stable, per-order
  `PayPal-Request-Id` (derived from a Guid minted with the order) so concurrent duplicates de-duplicate
  at PayPal. A double-click never produces a double charge or double refund.
- **Ownership.** Orders and saved cards are scoped to the authenticated buyer; a request for another
  shopper's resource returns the same `404` as a non-existent one.

## Configuration

Bound from the `PayPal` configuration section (values via user-secrets / environment, never committed):

| Key | Env var | Notes |
| --- | --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` |
| `PayPal:BaseUrl` | — | Optional; used verbatim as the API base when set, else derived from `Environment`. |

Load the credentials into .NET user-secrets for the `PublicApi` project:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
```

## Running & verifying

See the repository README / task notes for environment specifics. In short (this dev box):

```bash
export DOTNET_ROLL_FORWARD=Major          # global.json rolls forward to the installed .NET 10 SDK
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development \       # loads user-secrets
UseOnlyInMemoryDatabase=true \             # no LocalDB on this box
ASPNETCORE_URLS="https://localhost:7943;http://localhost:7944" \
dotnet run --no-launch-profile
```

Get a bearer token from `POST /api/authenticate` (`demouser@microsoft.com` / `Pass@word1`), then drive
the endpoints. The PayPal sandbox test card is Visa `4111 1111 1111 1111`, any future expiry, any CVC.

> The in-memory provider loses all data on restart and ignores migrations, so persisted order/payment
> state only survives within a single run.
