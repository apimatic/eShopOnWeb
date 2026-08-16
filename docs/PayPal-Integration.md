# PayPal payments & saved cards (PublicApi)

Additive capability on top of eShopOnWeb: collect money for an order with **PayPal** as the
processor, and let a shopper **save a card** for reuse. It does not change the existing
catalog/basket/order flow.

## Design

- A running .NET service cannot call the PayPal MCP plugin tools (those are agent-time tools),
  and the MCP tools do not expose direct card payments, authorize-intent, void, or vaulting.
  The PayPal plugin's own `paypal-best-practices` skill directs integrations to "fall back to
  the REST API directly" for full control. So the app talks to the **PayPal REST API v2/v3**
  over `HttpClient`, using the plugin references as the source for request/response shapes.
- Money model: **authorize** at pay time (hold), **capture** at fulfilment (take), **void** on
  cancel (release), **refund** after fulfilment (return). Saved cards use the **Vault v3** API.
- The existing `Order`/`OrderItem` aggregate is reused. Payment state PayPal owns (hold,
  capture, refunds) lives on an owned `OrderPayment` entity so later requests can act on it.

### Key files

| Concern | File |
|---|---|
| PayPal REST client | `src/Infrastructure/PayPal/PayPalGateway.cs` (`IPayPalGateway`) |
| Settings (bound from `PayPal:` section) | `src/ApplicationCore/Configuration/PayPalSettings.cs` |
| Order payment orchestration | `src/ApplicationCore/Services/OrderPaymentService.cs` |
| Order placement (from catalog) | `src/ApplicationCore/Services/OrderPlacementService.cs` |
| Saved cards | `src/ApplicationCore/Services/SavedPaymentMethodService.cs` |
| Reconciliation | `src/ApplicationCore/Services/ReconciliationService.cs` |
| Payment state on the order | `src/ApplicationCore/Entities/OrderAggregate/OrderPayment.cs`, `OrderRefund.cs`, `OrderStatus.cs` |
| Saved-card aggregate | `src/ApplicationCore/Entities/PaymentMethodAggregate/SavedPaymentMethod.cs` |
| HTTP endpoints | `src/PublicApi/PaymentEndpoints/*` |

## Endpoints (all under `/api/`, JWT-authenticated)

Shopper-scoped (act only on the caller's own data):

- `POST /api/orders` — place an order from catalog items. Returns `orderId`.
- `POST /api/orders/{orderId}/pay` — authorize (hold) the order total with card details **or**
  a saved card (`{"paymentMethodId": n}`). Idempotent per order.
- `POST /api/orders/{orderId}/refunds` — refund a fulfilled order, full or partial. Body carries
  `idempotencyKey`. Returns `refundId`.
- `GET  /api/my-orders` — the caller's orders with payment state.
- `POST /api/payment-methods` — save (vault) a card. Returns `paymentMethodId` and a safe
  description (brand, last four, expiry) — never full card details.
- `GET  /api/payment-methods` — the caller's saved cards.
- `DELETE /api/payment-methods/{paymentMethodId}` — remove a saved card.

Operator (administrator role) only:

- `POST /api/orders/{orderId}/fulfil` — capture the held funds; records captured amount,
  PayPal fee and net proceeds. Renews a stale authorization first; if it can no longer be
  renewed, returns an operator-actionable message.
- `POST /api/orders/{orderId}/cancel` — void the authorization before fulfilment (release funds).
- `GET  /api/reconciliation?from={iso}&to={iso}` — PayPal's transaction record for the range
  (chunked to 31-day windows and fully paged) lined up against eShop's captured orders.

## Configuration

Settings bind from the `PayPal:` section (never hard-coded, never committed):

| Key | Source env var | Notes |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` / `live`; used to derive the base URL |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | e.g. `USD` |
| `PayPal:BaseUrl` | (optional) | If set, used verbatim for **every** call incl. the token request |

Load them into user-secrets for the PublicApi project (values come from the environment):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Running (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major            # .NET 10 SDK, roll forward from the pinned 8.0.x
export ASPNETCORE_ENVIRONMENT=Development    # loads user-secrets
export UseOnlyInMemoryDatabase=true          # no LocalDB here
export ASPNETCORE_URLS="https://localhost:9943;http://localhost:9944"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Notes: the in-memory store resets each run and PublicApi has its own isolated store, so drive
the whole flow through this API (that is why `POST /api/orders` exists). Verify with the sandbox
test card Visa `4111 1111 1111 1111`, any future expiry, any CVC. PayPal's transaction reporting
lags, so a reconciliation range covering payments you just made may legitimately be empty.
