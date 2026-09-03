<!-- Generated file — do not edit; regenerated with the SDK. -->

# SalesCommissions — operations

Accessor: `client.SalesCommissions` · Source: `Api/SalesCommissions.cs` · 3 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ListSalesCommissionSettings

- **Signature**: `ListSalesCommissionSettings(string sellerId, bool? liveMode, int? page = 1, int? perPage = 100, string? authorization = "Bearer <<apiKey>>", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `liveMode` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `100`, `authorization` = `"Bearer <<apiKey>>"`
- **Query params (wire ← C#)**: `live_mode` ← `liveMode`, `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `IReadOnlyList<SaleRepSettings>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SaleRepSettings` | `Models/SaleRepSettings.cs` |

### ListSalesReps

- **Signature**: `ListSalesReps(string sellerId, bool? liveMode, int? page = 1, int? perPage = 100, string? authorization = "Bearer <<apiKey>>", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `liveMode` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `100`, `authorization` = `"Bearer <<apiKey>>"`
- **Query params (wire ← C#)**: `live_mode` ← `liveMode`, `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `IReadOnlyList<ListSaleRepItem>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ListSaleRepItem` | `Models/ListSaleRepItem.cs` |

### ReadSalesRep

- **Signature**: `ReadSalesRep(string sellerId, string salesRepId, bool? liveMode, int? page = 1, int? perPage = 100, string? authorization = "Bearer <<apiKey>>", RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `liveMode` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `100`, `authorization` = `"Bearer <<apiKey>>"`
- **Query params (wire ← C#)**: `live_mode` ← `liveMode`, `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `SaleRep`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SaleRep` | `Models/SaleRep.cs` |

