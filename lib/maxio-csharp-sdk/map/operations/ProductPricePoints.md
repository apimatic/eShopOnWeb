<!-- Generated file — do not edit; regenerated with the SDK. -->

# ProductPricePoints — operations

Accessor: `client.ProductPricePoints` · Source: `Api/ProductPricePoints.cs` · 11 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ArchiveProductPricePoint

- **Signature**: `ArchiveProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProductPricePointResponse`
- **Error**: `SdkException<ArchiveProductPricePointError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ProductIdModel` | `Models/AnyOf/ProductIdModel.cs` |
| `PricePointIdModel` | `Models/AnyOf/PricePointIdModel.cs` |
| `ProductPricePointResponse` | `Models/ProductPricePointResponse.cs` |
| `ArchiveProductPricePointError` | `Errors/ArchiveProductPricePointError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### BulkCreateProductPricePoints

- **Signature**: `BulkCreateProductPricePoints(int productId, BulkCreateProductPricePointsRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `BulkCreateProductPricePointsResponse`
- **Error**: `SdkException<BulkCreateProductPricePointsError>` — **Case A (typed)**
- **Error accessors**: `TryGetMapOfJsonElement(out IReadOnlyDictionary<string, JsonElement>)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BulkCreateProductPricePointsRequest` | `Models/BulkCreateProductPricePointsRequest.cs` |
| `BulkCreateProductPricePointsResponse` | `Models/BulkCreateProductPricePointsResponse.cs` |
| `BulkCreateProductPricePointsError` | `Errors/BulkCreateProductPricePointsError.cs` |

### CreateProductCurrencyPrices

- **Signature**: `CreateProductCurrencyPrices(int productPricePointId, CreateProductCurrencyPricesRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CurrencyPricesResponse`
- **Error**: `SdkException<CreateProductCurrencyPricesError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateProductCurrencyPricesRequest` | `Models/CreateProductCurrencyPricesRequest.cs` |
| `CurrencyPricesResponse` | `Models/CurrencyPricesResponse.cs` |
| `CreateProductCurrencyPricesError` | `Errors/CreateProductCurrencyPricesError.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### CreateProductPricePoint

- **Signature**: `CreateProductPricePoint(ProductIdModel productId, CreateProductPricePointRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ProductPricePointResponse`
- **Error**: `SdkException<CreateProductPricePointError>` — **Case A (typed)**
- **Error accessors**: `TryGetProductPricePointErrorResponse1(out ProductPricePointErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ProductIdModel` | `Models/AnyOf/ProductIdModel.cs` |
| `CreateProductPricePointRequest` | `Models/CreateProductPricePointRequest.cs` |
| `ProductPricePointResponse` | `Models/ProductPricePointResponse.cs` |
| `CreateProductPricePointError` | `Errors/CreateProductPricePointError.cs` |
| `ProductPricePointErrorResponse1` | `Models/ProductPricePointErrorResponse1.cs` |

### ListAllProductPricePoints

- **Signature**: `ListAllProductPricePoints(SortingDirection? direction, ListPricePointsFilter? filter, ListProductsPricePointsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `direction` — nullable, no default → **must pass explicitly**
  - `filter` — nullable, no default → **must pass explicitly**
  - `include` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `direction` ← `direction`, `filter` ← `filter`, `include` ← `include`, `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `ListProductPricePointsResponse`
- **Error**: `SdkException<ListAllProductPricePointsError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `ListPricePointsFilter` | `Models/ListPricePointsFilter.cs` |
| `ListProductsPricePointsInclude` | `Models/Enums/ListProductsPricePointsInclude.cs` |
| `ListProductPricePointsResponse` | `Models/ListProductPricePointsResponse.cs` |
| `ListAllProductPricePointsError` | `Errors/ListAllProductPricePointsError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListProductPricePoints

- **Signature**: `ListProductPricePoints(ProductIdModel productId, bool? currencyPrices, IReadOnlyList<PricePointType>? filterType, bool? archived, int? page = 1, int? perPage = 10, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `currencyPrices` — nullable, no default → **must pass explicitly**
  - `filterType` — nullable, no default → **must pass explicitly**
  - `archived` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `10`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `currency_prices` ← `currencyPrices`, `filter[type]` ← `filterType`, `archived` ← `archived`
- **Returns**: `ListProductPricePointsResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ProductIdModel` | `Models/AnyOf/ProductIdModel.cs` |
| `PricePointType` | `Models/Enums/PricePointType.cs` |
| `ListProductPricePointsResponse` | `Models/ListProductPricePointsResponse.cs` |

### PromoteProductPricePointToDefault

- **Signature**: `PromoteProductPricePointToDefault(int productId, int pricePointId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProductResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ProductResponse` | `Models/ProductResponse.cs` |

### ReadProductPricePoint

- **Signature**: `ReadProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, bool? currencyPrices, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `currencyPrices` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `currency_prices` ← `currencyPrices`
- **Returns**: `ProductPricePointResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ProductIdModel` | `Models/AnyOf/ProductIdModel.cs` |
| `PricePointIdModel` | `Models/AnyOf/PricePointIdModel.cs` |
| `ProductPricePointResponse` | `Models/ProductPricePointResponse.cs` |

### UnarchiveProductPricePoint

- **Signature**: `UnarchiveProductPricePoint(int productId, int pricePointId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProductPricePointResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ProductPricePointResponse` | `Models/ProductPricePointResponse.cs` |

### UpdateProductCurrencyPrices

- **Signature**: `UpdateProductCurrencyPrices(int productPricePointId, UpdateCurrencyPricesRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `CurrencyPricesResponse`
- **Error**: `SdkException<UpdateProductCurrencyPricesError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorArrayMapResponse1(out ErrorArrayMapResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateCurrencyPricesRequest` | `Models/UpdateCurrencyPricesRequest.cs` |
| `CurrencyPricesResponse` | `Models/CurrencyPricesResponse.cs` |
| `UpdateProductCurrencyPricesError` | `Errors/UpdateProductCurrencyPricesError.cs` |
| `ErrorArrayMapResponse1` | `Models/ErrorArrayMapResponse1.cs` |

### UpdateProductPricePoint

- **Signature**: `UpdateProductPricePoint(ProductIdModel productId, PricePointIdModel pricePointId, UpdateProductPricePointRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ProductPricePointResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ProductIdModel` | `Models/AnyOf/ProductIdModel.cs` |
| `PricePointIdModel` | `Models/AnyOf/PricePointIdModel.cs` |
| `UpdateProductPricePointRequest` | `Models/UpdateProductPricePointRequest.cs` |
| `ProductPricePointResponse` | `Models/ProductPricePointResponse.cs` |

