<!-- Generated file — do not edit; regenerated with the SDK. -->

# ComponentPricePoints — operations

Accessor: `client.ComponentPricePoints` · Source: `Api/ComponentPricePoints.cs` · 12 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ArchiveComponentPricePoint

- **Signature**: `ArchiveComponentPricePoint(ComponentIdModel componentId, PricePointIdModel pricePointId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ComponentPricePointResponse`
- **Error**: `SdkException<ArchiveComponentPricePointError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ComponentIdModel` | `Models/AnyOf/ComponentIdModel.cs` |
| `PricePointIdModel` | `Models/AnyOf/PricePointIdModel.cs` |
| `ComponentPricePointResponse` | `Models/ComponentPricePointResponse.cs` |
| `ArchiveComponentPricePointError` | `Errors/ArchiveComponentPricePointError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### BulkCreateComponentPricePoints

- **Signature**: `BulkCreateComponentPricePoints(string componentId, CreateComponentPricePointsRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentPricePointsResponse`
- **Error**: `SdkException<BulkCreateComponentPricePointsError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateComponentPricePointsRequest` | `Models/CreateComponentPricePointsRequest.cs` |
| `ComponentPricePointsResponse` | `Models/ComponentPricePointsResponse.cs` |
| `BulkCreateComponentPricePointsError` | `Errors/BulkCreateComponentPricePointsError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CloneComponentPricePoint

- **Signature**: `CloneComponentPricePoint(ComponentIdModel componentId, PricePointIdModel pricePointId, CloneComponentPricePointRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentPricePointCurrencyOverageResponse`
- **Error**: `SdkException<CloneComponentPricePointError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ComponentIdModel` | `Models/AnyOf/ComponentIdModel.cs` |
| `PricePointIdModel` | `Models/AnyOf/PricePointIdModel.cs` |
| `CloneComponentPricePointRequest` | `Models/CloneComponentPricePointRequest.cs` |
| `ComponentPricePointCurrencyOverageResponse` | `Models/ComponentPricePointCurrencyOverageResponse.cs` |
| `CloneComponentPricePointError` | `Errors/CloneComponentPricePointError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateComponentPricePoint

- **Signature**: `CreateComponentPricePoint(int componentId, CreateComponentPricePointRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentPricePointResponse`
- **Error**: `SdkException<CreateComponentPricePointError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateComponentPricePointRequest` | `Models/CreateComponentPricePointRequest.cs` |
| `ComponentPricePointResponse` | `Models/ComponentPricePointResponse.cs` |
| `CreateComponentPricePointError` | `Errors/CreateComponentPricePointError.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### CreateCurrencyPrices

- **Signature**: `CreateCurrencyPrices(int pricePointId, CreateCurrencyPricesRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentCurrencyPricesResponse`
- **Error**: `SdkException<CreateCurrencyPricesError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateCurrencyPricesRequest` | `Models/CreateCurrencyPricesRequest.cs` |
| `ComponentCurrencyPricesResponse` | `Models/ComponentCurrencyPricesResponse.cs` |
| `CreateCurrencyPricesError` | `Errors/CreateCurrencyPricesError.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### ListAllComponentPricePoints

- **Signature**: `ListAllComponentPricePoints(ListComponentsPricePointsInclude? include, SortingDirection? direction, ListPricePointsFilter? filter, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `include` — nullable, no default → **must pass explicitly**
  - `direction` — nullable, no default → **must pass explicitly**
  - `filter` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `include` ← `include`, `page` ← `page`, `per_page` ← `perPage`, `direction` ← `direction`, `filter` ← `filter`
- **Returns**: `ListComponentsPricePointsResponse`
- **Error**: `SdkException<ListAllComponentPricePointsError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ListComponentsPricePointsInclude` | `Models/Enums/ListComponentsPricePointsInclude.cs` |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `ListPricePointsFilter` | `Models/ListPricePointsFilter.cs` |
| `ListComponentsPricePointsResponse` | `Models/ListComponentsPricePointsResponse.cs` |
| `ListAllComponentPricePointsError` | `Errors/ListAllComponentPricePointsError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListComponentPricePoints

- **Signature**: `ListComponentPricePoints(int componentId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `currencyPrices` — nullable, no default → **must pass explicitly**
  - `filterType` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `currency_prices` ← `currencyPrices`, `page` ← `page`, `per_page` ← `perPage`, `filter[type]` ← `filterType`
- **Returns**: `ComponentPricePointsResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `PricePointType` | `Models/Enums/PricePointType.cs` |
| `ComponentPricePointsResponse` | `Models/ComponentPricePointsResponse.cs` |

### PromoteComponentPricePointToDefault

- **Signature**: `PromoteComponentPricePointToDefault(int componentId, int pricePointId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ComponentResponse` | `Models/ComponentResponse.cs` |

### ReadComponentPricePoint

- **Signature**: `ReadComponentPricePoint(ComponentIdModel componentId, PricePointIdModel pricePointId, bool? currencyPrices, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `currencyPrices` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `currency_prices` ← `currencyPrices`
- **Returns**: `ComponentPricePointCurrencyOverageResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ComponentIdModel` | `Models/AnyOf/ComponentIdModel.cs` |
| `PricePointIdModel` | `Models/AnyOf/PricePointIdModel.cs` |
| `ComponentPricePointCurrencyOverageResponse` | `Models/ComponentPricePointCurrencyOverageResponse.cs` |

### UnarchiveComponentPricePoint

- **Signature**: `UnarchiveComponentPricePoint(int componentId, int pricePointId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ComponentPricePointResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ComponentPricePointResponse` | `Models/ComponentPricePointResponse.cs` |

### UpdateComponentPricePoint

- **Signature**: `UpdateComponentPricePoint(ComponentIdModel componentId, PricePointIdModel pricePointId, UpdateComponentPricePointRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentPricePointResponse`
- **Error**: `SdkException<UpdateComponentPricePointError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ComponentIdModel` | `Models/AnyOf/ComponentIdModel.cs` |
| `PricePointIdModel` | `Models/AnyOf/PricePointIdModel.cs` |
| `UpdateComponentPricePointRequest` | `Models/UpdateComponentPricePointRequest.cs` |
| `ComponentPricePointResponse` | `Models/ComponentPricePointResponse.cs` |
| `UpdateComponentPricePointError` | `Errors/UpdateComponentPricePointError.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### UpdateCurrencyPrices

- **Signature**: `UpdateCurrencyPrices(int pricePointId, UpdateCurrencyPricesRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentCurrencyPricesResponse`
- **Error**: `SdkException<UpdateCurrencyPricesError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateCurrencyPricesRequest` | `Models/UpdateCurrencyPricesRequest.cs` |
| `ComponentCurrencyPricesResponse` | `Models/ComponentCurrencyPricesResponse.cs` |
| `UpdateCurrencyPricesError` | `Errors/UpdateCurrencyPricesError.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

