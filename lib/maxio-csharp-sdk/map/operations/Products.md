<!-- Generated file — do not edit; regenerated with the SDK. -->

# Products — operations

Accessor: `client.Products` · Source: `Api/Products.cs` · 6 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ArchiveProduct

- **Signature**: `ArchiveProduct(int productId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProductResponse`
- **Error**: `SdkException<ArchiveProductError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ProductResponse` | `Models/ProductResponse.cs` |
| `ArchiveProductError` | `Errors/ArchiveProductError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateProduct

- **Signature**: `CreateProduct(string productFamilyId, CreateOrUpdateProductRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ProductResponse`
- **Error**: `SdkException<CreateProductError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateOrUpdateProductRequest` | `Models/CreateOrUpdateProductRequest.cs` |
| `ProductResponse` | `Models/ProductResponse.cs` |
| `CreateProductError` | `Errors/CreateProductError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListProducts

- **Signature**: `ListProducts(BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? endDate, DateTimeOffset? endDatetime, DateTimeOffset? startDate, DateTimeOffset? startDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params (`dateField` … `include`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `date_field` ← `dateField`, `filter` ← `filter`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `page` ← `page`, `per_page` ← `perPage`, `include_archived` ← `includeArchived`, `include` ← `include`
- **Returns**: `IReadOnlyList<ProductResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `BasicDateField` | `Models/Enums/BasicDateField.cs` |
| `ListProductsFilter` | `Models/ListProductsFilter.cs` |
| `ListProductsInclude` | `Models/Enums/ListProductsInclude.cs` |
| `ProductResponse` | `Models/ProductResponse.cs` |

### ReadProduct

- **Signature**: `ReadProduct(int productId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProductResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ProductResponse` | `Models/ProductResponse.cs` |

### ReadProductByHandle

- **Signature**: `ReadProductByHandle(string apiHandle, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProductResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ProductResponse` | `Models/ProductResponse.cs` |

### UpdateProduct

- **Signature**: `UpdateProduct(int productId, CreateOrUpdateProductRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ProductResponse`
- **Error**: `SdkException<UpdateProductError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateOrUpdateProductRequest` | `Models/CreateOrUpdateProductRequest.cs` |
| `ProductResponse` | `Models/ProductResponse.cs` |
| `UpdateProductError` | `Errors/UpdateProductError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

