# Walmart Marketplace inventory integration

eShopOnWeb now keeps **Walmart Marketplace** informed of how much of its catalog it has to sell,
so Walmart can act as a second sales channel. This is additive — the existing
catalog/basket/order flow is untouched.

Each catalog item is sold on Walmart under seller SKU `ESHOP-{catalogItemId}` (catalog item 7 →
`ESHOP-7`).

## What was added

| Capability | Endpoint (PublicApi, routed under `/api/`) | Who |
|---|---|---|
| Set one SKU's stock on Walmart | `PUT /api/walmart/stock/{sku}` (body `{ "quantity": N }`) | operator (admin) |
| Read one SKU's stock from Walmart (live) | `GET /api/walmart/stock/{sku}` | operator (admin) |
| Push the whole catalog's stock to Walmart | `POST /api/walmart/stock/sync` (no body) | operator (admin) |
| Reconciliation report (Walmart vs eShop) | `GET /api/walmart/stock` | operator (admin) |
| Set eShop's own stock for a catalog item | `PUT /api/catalog-items/{catalogItemId}/stock` (body `{ "quantity": N }`) | operator (admin) |
| Place an order (decrements eShop stock) | `POST /api/orders` (body `{ "items": [ { "catalogItemId": N, "quantity": N } ] }`) | any signed-in shopper |

Response identifiers: `orderId`; `sku` + `quantity`; `results[]` each with `sku` + `status`
(`updated`/`failed`); `items[]` each with `sku`, `walmartQuantity`, `eshopQuantity` (`null` on the
side that has no such SKU).

## How it talks to Walmart

The **`api-specs/walmart`** OpenAPI documents are the sole contract. Only two are needed:

- `authorization.json` — OAuth 2.0 **client-credentials** token at `POST /seller/auth/v4/token`.
- `inventory.json` — `PUT`/`GET /seller/v1/inventory/v4/items/{sku}`, `POST .../items/bulk`
  (sync), `GET .../items` (report, paginated).

A hand-written client (`src/Infrastructure/Walmart/`) builds directly to these contracts — no
third-party Walmart SDK. The bearer token is obtained via client-credentials, cached until just
before expiry, attached as `Authorization: Bearer <token>`, and refreshed once on a 401.

### Configuration (`Walmart:` section)

| Key | Source | Meaning |
|---|---|---|
| `Walmart:ClientId` | `WALMART_CLIENT_ID` | OAuth client id |
| `Walmart:ClientSecret` | `WALMART_CLIENT_SECRET` | OAuth client secret (never logged/returned/committed) |
| `Walmart:BaseUrl` | optional | `scheme://host[:port]` override that replaces scheme/host/port of **every** Walmart call (auth + inventory). Each contract's base path after the host (`/seller`, `/seller/v1`) is preserved. |

Credentials are read from the environment into **.NET user-secrets** (never written to any repo
file):

```bash
dotnet user-secrets --project src/PublicApi set "Walmart:ClientId"     "$WALMART_CLIENT_ID"
dotnet user-secrets --project src/PublicApi set "Walmart:ClientSecret" "$WALMART_CLIENT_SECRET"
```

## Verify it yourself

Prerequisites on this machine: `global.json` rolls forward to the .NET 10 SDK; run with
`DOTNET_ROLL_FORWARD=Major`. The DB runs in-memory (`UseOnlyInMemoryDatabase=true`) — stock and
orders persist only within a single run, and PublicApi has its own isolated store (drive every flow
through PublicApi).

### 1. Build + automated tests

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet build eShopOnWeb.sln
dotnet test tests/UnitTests/UnitTests.csproj                       # contract-level client tests (stubbed HTTP)
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj   # HTTP surface, auth roles, orchestration
```

The unit tests prove the client speaks the Walmart contract (URLs, bodies, pagination, base-URL
override, token caching, 401 retry) without a live Walmart. The integration tests drive every
endpoint over HTTP with Walmart replaced by an in-memory fake.

### 2. Run the app and drive it

```bash
export DOTNET_ROLL_FORWARD=Major UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:39183;http://localhost:39184"
# Point at a Walmart-compatible service; omit to use the contract's production server.
export Walmart__BaseUrl="http://localhost:39190"
dotnet run --project src/PublicApi --no-launch-profile
```

Get a bearer token (JWT; the storefront cookie won't work here), then call the endpoints:

```bash
B=https://localhost:39183
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")

# eShop's own stock, then place an order (decrements it; over-stock is refused with 400)
curl -sk -X PUT  $B/api/catalog-items/1/stock -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" -d '{"quantity":25}'
curl -sk -X POST $B/api/orders               -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":1,"quantity":2}]}'

# Push the whole catalog to Walmart, set/read one SKU, and reconcile
curl -sk -X POST $B/api/walmart/stock/sync   -H "Authorization: Bearer $ADMIN"
curl -sk -X PUT  $B/api/walmart/stock/ESHOP-7 -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" -d '{"quantity":50}'
curl -sk        $B/api/walmart/stock/ESHOP-7 -H "Authorization: Bearer $ADMIN"
curl -sk        $B/api/walmart/stock          -H "Authorization: Bearer $ADMIN"
```

A shopper token (`demouser@microsoft.com` / `Pass@word1`) can call `POST /api/orders` but gets
`403` on every `/api/walmart/...` and `/api/catalog-items/{id}/stock` operator endpoint.

If `Walmart:BaseUrl` points at nothing, operator Walmart calls return `502` (the integration tried
the overridden host, never Walmart production). Point `Walmart:BaseUrl` at the Walmart-compatible
service to see the happy path.
