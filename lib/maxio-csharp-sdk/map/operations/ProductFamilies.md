<!-- Generated file — do not edit; regenerated with the SDK. -->

# ProductFamilies — operations

Accessor: `client.ProductFamilies` · Source: `Api/ProductFamilies.cs` · 4 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreateProductFamily

- **Signature**: `CreateProductFamily(CreateProductFamilyRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ProductFamilyResponse`
- **Error**: `SdkException<CreateProductFamilyError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateProductFamilyRequest` | `Models/CreateProductFamilyRequest.cs` |
| `ProductFamilyResponse` | `Models/ProductFamilyResponse.cs` |
| `CreateProductFamilyError` | `Errors/CreateProductFamilyError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListProductFamilies

- **Signature**: `ListProductFamilies(BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 5 params (`dateField` … `endDatetime`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
- **Query params (wire ← C#)**: `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`
- **Returns**: `IReadOnlyList<ProductFamilyResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `BasicDateField` | `Models/Enums/BasicDateField.cs` |
| `ProductFamilyResponse` | `Models/ProductFamilyResponse.cs` |

### ListProductsForProductFamily

- **Signature**: `ListProductsForProductFamily(string productFamilyId, BasicDateField? dateField, ListProductsFilter? filter, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? includeArchived, ListProductsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params (`dateField` … `include`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `date_field` ← `dateField`, `filter` ← `filter`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `include_archived` ← `includeArchived`, `include` ← `include`
- **Returns**: `IReadOnlyList<ProductResponse>`
- **Error**: `SdkException<ListProductsForProductFamilyError>` — **Case A (typed)**
- **Error accessors**: `TryGetString(out string)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BasicDateField` | `Models/Enums/BasicDateField.cs` |
| `ListProductsFilter` | `Models/ListProductsFilter.cs` |
| `ListProductsInclude` | `Models/Enums/ListProductsInclude.cs` |
| `ProductResponse` | `Models/ProductResponse.cs` |
| `ListProductsForProductFamilyError` | `Errors/ListProductsForProductFamilyError.cs` |

### ReadProductFamily

- **Signature**: `ReadProductFamily(int id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ProductFamilyResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ProductFamilyResponse` | `Models/ProductFamilyResponse.cs` |

