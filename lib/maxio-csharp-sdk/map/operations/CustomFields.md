<!-- Generated file — do not edit; regenerated with the SDK. -->

# CustomFields — operations

Accessor: `client.CustomFields` · Source: `Api/CustomFields.cs` · 9 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CreateMetadata

- **Signature**: `CreateMetadata(ResourceType resourceType, int resourceId, CreateMetadataRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `IReadOnlyList<Metadata>`
- **Error**: `SdkException<CreateMetadataError>` — **Case A (typed)**
- **Error accessors**: `TryGetSingleErrorResponse1(out SingleErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ResourceType` | `Models/Enums/ResourceType.cs` |
| `CreateMetadataRequest` | `Models/CreateMetadataRequest.cs` |
| `Metadata` | `Models/Metadata.cs` |
| `CreateMetadataError` | `Errors/CreateMetadataError.cs` |
| `SingleErrorResponse1` | `Models/SingleErrorResponse1.cs` |

### CreateMetafields

- **Signature**: `CreateMetafields(ResourceType resourceType, CreateMetafieldsRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `IReadOnlyList<Metafield>`
- **Error**: `SdkException<CreateMetafieldsError>` — **Case A (typed)**
- **Error accessors**: `TryGetSingleErrorResponse1(out SingleErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ResourceType` | `Models/Enums/ResourceType.cs` |
| `CreateMetafieldsRequest` | `Models/CreateMetafieldsRequest.cs` |
| `Metafield` | `Models/Metafield.cs` |
| `CreateMetafieldsError` | `Errors/CreateMetafieldsError.cs` |
| `SingleErrorResponse1` | `Models/SingleErrorResponse1.cs` |

### DeleteMetadata

- **Signature**: `DeleteMetadata(ResourceType resourceType, int resourceId, string? name, IReadOnlyList<string>? names, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `name` — nullable, no default → **must pass explicitly**
  - `names` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `name` ← `name`, `names` ← `names`
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeleteMetadataError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ResourceType` | `Models/Enums/ResourceType.cs` |
| `DeleteMetadataError` | `Errors/DeleteMetadataError.cs` |

### DeleteMetafield

- **Signature**: `DeleteMetafield(ResourceType resourceType, string? name, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `name` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `name` ← `name`
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeleteMetafieldError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ResourceType` | `Models/Enums/ResourceType.cs` |
| `DeleteMetafieldError` | `Errors/DeleteMetafieldError.cs` |

### ListMetadata

- **Signature**: `ListMetadata(ResourceType resourceType, int resourceId, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `PaginatedMetadata`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ResourceType` | `Models/Enums/ResourceType.cs` |
| `PaginatedMetadata` | `Models/PaginatedMetadata.cs` |

### ListMetadataForResourceType

- **Signature**: `ListMetadataForResourceType(ResourceType resourceType, BasicDateField? dateField, DateTimeOffset? startDate, DateTimeOffset? endDate, DateTimeOffset? startDatetime, DateTimeOffset? endDatetime, bool? withDeleted, IReadOnlyList<int>? resourceIds, SortingDirection? direction, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 8 params (`dateField` … `direction`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `with_deleted` ← `withDeleted`, `resource_ids` ← `resourceIds`, `direction` ← `direction`
- **Returns**: `PaginatedMetadata`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ResourceType` | `Models/Enums/ResourceType.cs` |
| `BasicDateField` | `Models/Enums/BasicDateField.cs` |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `PaginatedMetadata` | `Models/PaginatedMetadata.cs` |

### ListMetafields

- **Signature**: `ListMetafields(ResourceType resourceType, string? name, SortingDirection? direction, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `name` — nullable, no default → **must pass explicitly**
  - `direction` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `name` ← `name`, `page` ← `page`, `per_page` ← `perPage`, `direction` ← `direction`
- **Returns**: `ListMetafieldsResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ResourceType` | `Models/Enums/ResourceType.cs` |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `ListMetafieldsResponse` | `Models/ListMetafieldsResponse.cs` |

### UpdateMetadata

- **Signature**: `UpdateMetadata(ResourceType resourceType, int resourceId, UpdateMetadataRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `IReadOnlyList<Metadata>`
- **Error**: `SdkException<UpdateMetadataError>` — **Case A (typed)**
- **Error accessors**: `TryGetSingleErrorResponse1(out SingleErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ResourceType` | `Models/Enums/ResourceType.cs` |
| `UpdateMetadataRequest` | `Models/UpdateMetadataRequest.cs` |
| `Metadata` | `Models/Metadata.cs` |
| `UpdateMetadataError` | `Errors/UpdateMetadataError.cs` |
| `SingleErrorResponse1` | `Models/SingleErrorResponse1.cs` |

### UpdateMetafield

- **Signature**: `UpdateMetafield(ResourceType resourceType, UpdateMetafieldsRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `IReadOnlyList<Metafield>`
- **Error**: `SdkException<UpdateMetafieldError>` — **Case A (typed)**
- **Error accessors**: `TryGetSingleErrorResponse1(out SingleErrorResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ResourceType` | `Models/Enums/ResourceType.cs` |
| `UpdateMetafieldsRequest` | `Models/UpdateMetafieldsRequest.cs` |
| `Metafield` | `Models/Metafield.cs` |
| `UpdateMetafieldError` | `Errors/UpdateMetafieldError.cs` |
| `SingleErrorResponse1` | `Models/SingleErrorResponse1.cs` |

