# Supplier catalog sync

An additive capability that pulls a supplier's product listing into eShopOnWeb's own catalog
using [Firecrawl](https://firecrawl.dev) to read the listing page. It does not touch the existing
catalog, basket or order flow — imported products become ordinary `CatalogItem`s and show up in
the existing catalog listing.

## Endpoints (PublicApi, JWT, administrator role only)

| Method & route | Purpose | Key response fields |
| --- | --- | --- |
| `POST /api/catalog/suppliers` | Register a supplier (name + product-listing URL). | `supplierId` |
| `POST /api/catalog/suppliers/{supplierId}/sync` | Start a background sync of that listing. Returns immediately. | `syncId`, `status` |
| `GET  /api/catalog/syncs/{syncId}` | Status and outcome of a sync. | `status`, `itemsFound`, `itemsImported` |

`status` is one of `Pending`, `Running`, `Completed` (whole listing captured),
`PartiallyCompleted` (listing read but some products could not be imported), or `Failed`.

## How it works

1. Starting a sync creates a `CatalogSync` record and enqueues its id on an in-process queue
   (`ISupplierSyncQueue`); the call returns `202 Accepted` right away.
2. `SupplierSyncBackgroundService` (a `BackgroundService`) drains the queue and runs each sync in
   its own DI scope via `SupplierCatalogSyncService`.
3. The sync service calls Firecrawl's **`POST /extract`** with a JSON Schema describing the product
   fields (name, description, brand, SKU, price), then polls **`GET /extract/{id}`** until the job
   reaches a terminal state. The listing URL is expanded to a glob so paginated listings are read
   in full.
4. Each product is upserted into the catalog, matched by the supplier's own product code
   (`CatalogItem.SupplierProductCode` + `SupplierId`), so re-running a sync updates existing items
   instead of duplicating them. Brands and a "Supplier Catalog" type are created on first use.
   Products with no usable price (e.g. "Contact for pricing") are counted in `itemsFound` but not
   imported — yielding a `PartiallyCompleted` status.

## Firecrawl integration

The client (`Infrastructure/Services/Firecrawl/FirecrawlClient.cs`) is hand-written to the OpenAPI
contract in [`firecrawl-spec/openapi.json`](firecrawl-spec/openapi.json) — no third-party SDK. It
uses the spec's `bearerAuth` scheme and the `/extract` endpoints, whose request (`urls`, `prompt`,
`schema`) and response (`status`, `data`) shapes are defined in the spec.

## Configuration

Bound from the `Firecrawl` configuration section:

| Key | Meaning |
| --- | --- |
| `Firecrawl:ApiKey` | API key. Supply out of band (from the `FIRECRAWL_API_KEY` environment variable, loaded into .NET user-secrets). **Never committed to the repo.** |
| `Firecrawl:BaseUrl` | Optional. When set, used verbatim as the API base address; otherwise `https://api.firecrawl.dev/v2`. |

Optional `SupplierSync` section tunes poll interval, timeout, the catalog type/brand names and the
extraction prompt (`ApplicationCore/Services/SupplierSyncOptions.cs`).

## Running locally

```bash
# Load the key into user-secrets (value never written to the repo)
dotnet user-secrets set "Firecrawl:ApiKey" "$FIRECRAWL_API_KEY" --project src/PublicApi/PublicApi.csproj

# Run PublicApi in-memory (no LocalDB required); register, sync and verify within one run
ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true DOTNET_ROLL_FORWARD=Major \
  ASPNETCORE_URLS="https://localhost:11183;http://localhost:11184" \
  dotnet run --project src/PublicApi/PublicApi.csproj
```
