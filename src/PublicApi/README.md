# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Walmart Marketplace stock integration

An additive capability that offers the eShop catalog's stock on Walmart Marketplace as a second
sales channel. Each catalog item is sold on Walmart under the seller SKU `ESHOP-{catalogItemId}`.

All endpoints are JWT-authenticated. Every Walmart endpoint plus `PUT /api/catalog-items/{id}/stock`
is an **operator** action restricted to the `Administrators` role; `POST /api/orders` is open to any
signed-in shopper.

| Endpoint | Role | Purpose |
|---|---|---|
| `PUT /api/walmart/stock/{sku}` | admin | Set Walmart's recorded quantity for a SKU (→ `sku`, `quantity`). |
| `GET /api/walmart/stock/{sku}` | admin | Read Walmart's live recorded quantity for a SKU (→ `sku`, `quantity`). |
| `POST /api/walmart/stock/sync` | admin | Push every catalog item's eShop stock level to Walmart (→ `results[]` of `sku`+`status`). |
| `GET /api/walmart/stock` | admin | Reconciliation report of all SKUs: `items[]` of `sku`, `walmartQuantity`, `eshopQuantity` (`null` on the missing side). |
| `PUT /api/catalog-items/{catalogItemId}/stock` | admin | Set eShop's own stock level for an item (does not call Walmart). |
| `POST /api/orders` | any user | Place an order; decrements eShop stock, refuses over-stock orders (→ `orderId`). |

eShop's own stock level per catalog item is stored on `CatalogItem.StockQuantity` (defaults to 0).
Orders take ordered quantities off that level; Walmart only learns new levels at the next sync.

### Walmart configuration (`Walmart:` section)

Values are supplied out of band and never committed to the repo:

- `Walmart:ClientId` — seller client id (from `WALMART_CLIENT_ID`), loaded into .NET user-secrets.
- `Walmart:ClientSecret` — seller client secret (from `WALMART_CLIENT_SECRET`), user-secrets.
- `Walmart:BaseUrl` — optional `scheme://host[:port]` override that replaces the scheme/host/port
  of every Walmart call (including the token request); the contract base paths (`/seller`,
  `/seller/v1`) and everything after the host are preserved.

The integration is built against Walmart's OpenAPI contract in `api-specs/walmart`
(`authorization.json` for the `client_credentials` token flow, `inventory.json` for the inventory
operations). No pre-built Walmart SDK is used.

