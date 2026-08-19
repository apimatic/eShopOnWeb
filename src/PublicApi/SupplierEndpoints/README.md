# Supplier catalog sync

An additive capability that brings a supplier's product listing into eShopOnWeb's own catalog
without anyone re-typing it. A supplier's listing page is read with **Firecrawl**, and every
product found is matched into the store's catalog. Nothing in the existing catalog/basket/order
flow changes — imported products simply appear in the normal catalog listing.

## Endpoints (administrator-only, JWT)

| Method & route | Purpose | Top-level result fields |
| --- | --- | --- |
| `POST /api/catalog/suppliers` | Register a supplier (name + product-listing URL). | `supplierId` |
| `POST /api/catalog/suppliers/{supplierId}/sync` | Start a sync. Returns immediately; the sync runs in the background. | `syncId` |
| `GET /api/catalog/syncs/{syncId}` | Status and outcome of one sync. | `status`, `itemsFound`, `itemsImported` |

`status` is one of `Pending`, `Running`, `Completed` (the whole listing was imported),
`PartiallyCompleted` (only some products could be imported), or `Failed`. Together with
`itemsFound` vs `itemsImported`, a caller can tell the outcome without guessing. Imported items
show up in the existing catalog listing (`GET /api/catalog-items`).

## How it works

1. **Register** persists a `Supplier` (name + listing URL).
2. **Start sync** records a `CatalogSync` (`Pending`) and enqueues its id on an in-process queue
   (`IBackgroundSyncQueue`); the request returns `202 Accepted` right away.
3. A hosted worker (`SupplierSyncHostedService`) dequeues the sync and runs
   `SupplierCatalogSyncProcessor` in its own DI scope:
   - `FirecrawlProductListingReader` reads the listing and returns the products found.
   - Each product is matched into the catalog. Matching is keyed by the supplier's own identifier
     (its SKU, or its product URL) via a `SupplierProductMap`, so **re-running a sync updates the
     same catalog item instead of creating a duplicate**.
   - A product with no usable name or no positive price (e.g. "Contact for pricing") is *found but
     not imported* — that is what makes a sync `PartiallyCompleted`.
   - Brands are resolved/created from each product's brand; imported items get a dedicated
     `Imported` catalog type.
4. `itemsFound` / `itemsImported` and the final status are written back to the `CatalogSync`.

## Firecrawl integration

The Firecrawl OpenAPI spec in `firecrawl-spec/openapi.json` is the authoritative contract. The
client (`src/Infrastructure/Firecrawl`) is hand-written against that spec — no third-party SDK.

- **Endpoint used:** `POST /extract` (async structured extraction) + `GET /extract/{id}` (poll).
  Both the request and the response (`ExtractStatusResponse.data`) are fully defined in the spec,
  which makes them the spec-faithful way to pull structured, multi-product data from a listing
  page. (The live API emits a deprecation notice preferring `/scrape` with a `json` format, but the
  spec's `ScrapeResponse` does **not** define a JSON-output field, so building against that would
  mean relying on an undocumented shape. The spec is authoritative, so `/extract` is used.)
- **Auth:** HTTP bearer (`Authorization: Bearer <key>`), per the spec's `bearerAuth` scheme.
- **Base URL:** the spec's server (`https://api.firecrawl.dev/v2`) unless `Firecrawl:BaseUrl`
  overrides it, in which case the override is used verbatim.

## Configuration

Bound from the `Firecrawl` configuration section:

- `Firecrawl:ApiKey` — the API key. Supplied via user-secrets / environment (from the
  `FIRECRAWL_API_KEY` environment variable); it is never stored in the repository.
- `Firecrawl:BaseUrl` — optional base-address override.
- `Firecrawl:RequestTimeoutSeconds`, `Firecrawl:PollIntervalSeconds`, `Firecrawl:PollTimeoutSeconds`
  — optional tuning for the HTTP call timeout and extract-job polling.

Load the key into user-secrets once:

```bash
dotnet user-secrets set "Firecrawl:ApiKey" "$FIRECRAWL_API_KEY" --project src/PublicApi
```
