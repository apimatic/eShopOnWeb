# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Supplier catalog sync (Firecrawl)

An additive capability that imports a supplier's product listing into the store's own catalog by
reading the supplier's listing page with [Firecrawl](https://firecrawl.dev). Imported products
become ordinary `CatalogItem`s and therefore show up in the existing catalog listing
(`GET /api/catalog-items`) and the storefront — no parallel listing is built.

### Endpoints (administrator only, JWT)

| Method & route | Purpose | Key response fields |
| --- | --- | --- |
| `POST /api/catalog/suppliers` | Register a supplier (`name`, `listingUrl`). | `supplierId` |
| `POST /api/catalog/suppliers/{supplierId}/sync` | Start a sync of that supplier's listing. Returns immediately (202); the sync runs in the background. | `syncId`, `status` |
| `GET /api/catalog/syncs/{syncId}` | Status/outcome of a sync. | `status`, `itemsFound`, `itemsImported` |

`status` values: `Pending`/`Running` (still running), `Completed` (whole listing captured —
`itemsImported == itemsFound`), `PartiallyCompleted` (only part captured — some products could not
be imported, e.g. no usable price), or `Failed`.

Each imported product is matched to the catalog by `(supplierId, supplierItemKey)` — the supplier's
own SKU/id (falling back to the product URL, then its name). Re-running a sync therefore updates the
existing catalog item instead of creating a duplicate.

### How it uses Firecrawl

The Firecrawl integration is built directly against the OpenAPI contract in `firecrawl-spec/`
(no third-party SDK). It uses the async `POST /extract` + `GET /extract/{id}` endpoints: a sync
starts an extraction job for the listing URL (with a JSON schema describing the product fields),
polls the job to completion, then upserts the returned products.

### Configuration

Bound from the `Firecrawl:` configuration section:

- `Firecrawl:ApiKey` — Firecrawl bearer token. Supplied via the `FIRECRAWL_API_KEY` environment
  variable and loaded into .NET user-secrets; **never committed to the repo**. Load it once with:

  ```bash
  dotnet user-secrets set "Firecrawl:ApiKey" "$FIRECRAWL_API_KEY" --project src/PublicApi
  ```

- `Firecrawl:BaseUrl` — optional. When set it is used verbatim as the API base address; when empty
  the client falls back to the base URL declared by the Firecrawl OpenAPI spec
  (`https://api.firecrawl.dev/v2`).
