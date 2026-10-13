# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Walmart Marketplace stock integration

Additive capability that keeps Walmart Marketplace told how much of the eShop catalog is for
sale. Each catalog item is offered on Walmart under the seller SKU `ESHOP-{catalogItemId}`.

All endpoints are JWT-authenticated. Every endpoint except `POST /api/orders` is an **operator**
action, restricted to the `ADMINISTRATORS` role; `POST /api/orders` is open to any signed-in shopper.

| Method & route | Role | Purpose |
|---|---|---|
| `PUT /api/catalog-items/{catalogItemId}/stock` | admin | Set eShop's own stock level for an item (does not call Walmart). |
| `POST /api/orders` | shopper | Place an order from catalog item ids/quantities; deducts eShop stock. Over-ordering is refused. |
| `PUT /api/walmart/stock/{sku}` | admin | Set the SKU's quantity on Walmart. |
| `GET /api/walmart/stock/{sku}` | admin | Read the SKU's quantity as Walmart currently records it. |
| `POST /api/walmart/stock/sync` | admin | Push every catalog item's eShop level to Walmart (unset = 0). |
| `GET /api/walmart/stock` | admin | Reconciliation report: every SKU's Walmart vs eShop quantity. |

### Walmart contract & configuration

Every Walmart interaction is built to the OpenAPI documents under `/api-specs/walmart`
(`authorization.json` for the OAuth `client_credentials` token, `inventory.json` for the inventory
operations). No pre-built Walmart SDK is used.

Settings bind from the `Walmart:` configuration section:

- `Walmart:ClientId` — OAuth client id (supply via user-secrets or `WALMART_CLIENT_ID`).
- `Walmart:ClientSecret` — OAuth client secret (supply via user-secrets or `WALMART_CLIENT_SECRET`). Never written to any repo file.
- `Walmart:BaseUrl` — optional `scheme://host[:port]` override. When set it replaces the scheme/host/port of **every** Walmart call (including the token call); the contract's base path and everything after the host are preserved.

