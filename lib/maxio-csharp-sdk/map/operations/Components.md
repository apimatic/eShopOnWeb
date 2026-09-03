<!-- Generated file — do not edit; regenerated with the SDK. -->

# Components — operations

Accessor: `client.Components` · Source: `Api/Components.cs` · 12 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ArchiveComponent

- **Signature**: `ArchiveComponent(int productFamilyId, string componentId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `Component`
- **Error**: `SdkException<ArchiveComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `Component` | `Models/Component.cs` |
| `ArchiveComponentError` | `Errors/ArchiveComponentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateEventBasedComponent

- **Signature**: `CreateEventBasedComponent(string productFamilyId, CreateEbbComponent? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<CreateEventBasedComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateEbbComponent` | `Models/CreateEbbComponent.cs` |
| `ComponentResponse` | `Models/ComponentResponse.cs` |
| `CreateEventBasedComponentError` | `Errors/CreateEventBasedComponentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateMeteredComponent

- **Signature**: `CreateMeteredComponent(string productFamilyId, CreateMeteredComponent? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<CreateMeteredComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateMeteredComponent` | `Models/CreateMeteredComponent.cs` |
| `ComponentResponse` | `Models/ComponentResponse.cs` |
| `CreateMeteredComponentError` | `Errors/CreateMeteredComponentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateOnOffComponent

- **Signature**: `CreateOnOffComponent(string productFamilyId, CreateOnOffComponent? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<CreateOnOffComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateOnOffComponent` | `Models/CreateOnOffComponent.cs` |
| `ComponentResponse` | `Models/ComponentResponse.cs` |
| `CreateOnOffComponentError` | `Errors/CreateOnOffComponentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreatePrepaidUsageComponent

- **Signature**: `CreatePrepaidUsageComponent(string productFamilyId, CreatePrepaidComponent? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<CreatePrepaidUsageComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreatePrepaidComponent` | `Models/CreatePrepaidComponent.cs` |
| `ComponentResponse` | `Models/ComponentResponse.cs` |
| `CreatePrepaidUsageComponentError` | `Errors/CreatePrepaidUsageComponentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateQuantityBasedComponent

- **Signature**: `CreateQuantityBasedComponent(string productFamilyId, CreateQuantityBasedComponent? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<CreateQuantityBasedComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateQuantityBasedComponent` | `Models/CreateQuantityBasedComponent.cs` |
| `ComponentResponse` | `Models/ComponentResponse.cs` |
| `CreateQuantityBasedComponentError` | `Errors/CreateQuantityBasedComponentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### FindComponent

- **Signature**: `FindComponent(string handle, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Query params (wire ← C#)**: `handle` ← `handle`
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ComponentResponse` | `Models/ComponentResponse.cs` |

### ListComponents

- **Signature**: `ListComponents(BasicDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, bool? includeArchived, ListComponentsFilter? filter, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 7 params (`dateField` … `filter`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`, `include_archived` ← `includeArchived`, `page` ← `page`, `per_page` ← `perPage`, `filter` ← `filter`
- **Returns**: `IReadOnlyList<ComponentResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `BasicDateField` | `Models/Enums/BasicDateField.cs` |
| `ListComponentsFilter` | `Models/ListComponentsFilter.cs` |
| `ComponentResponse` | `Models/ComponentResponse.cs` |

### ListComponentsForProductFamily

- **Signature**: `ListComponentsForProductFamily(int productFamilyId, bool? includeArchived, ListComponentsFilter? filter, BasicDateField? dateField, string? endDate, string? endDatetime, string? startDate, string? startDatetime, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 7 params (`includeArchived` … `startDatetime`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `include_archived` ← `includeArchived`, `page` ← `page`, `per_page` ← `perPage`, `filter` ← `filter`, `date_field` ← `dateField`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`
- **Returns**: `IReadOnlyList<ComponentResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ListComponentsFilter` | `Models/ListComponentsFilter.cs` |
| `BasicDateField` | `Models/Enums/BasicDateField.cs` |
| `ComponentResponse` | `Models/ComponentResponse.cs` |

### ReadComponent

- **Signature**: `ReadComponent(int productFamilyId, string componentId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ComponentResponse` | `Models/ComponentResponse.cs` |

### UpdateComponent

- **Signature**: `UpdateComponent(string componentId, UpdateComponentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<UpdateComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateComponentRequest` | `Models/UpdateComponentRequest.cs` |
| `ComponentResponse` | `Models/ComponentResponse.cs` |
| `UpdateComponentError` | `Errors/UpdateComponentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### UpdateProductFamilyComponent

- **Signature**: `UpdateProductFamilyComponent(int productFamilyId, string componentId, UpdateComponentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ComponentResponse`
- **Error**: `SdkException<UpdateProductFamilyComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateComponentRequest` | `Models/UpdateComponentRequest.cs` |
| `ComponentResponse` | `Models/ComponentResponse.cs` |
| `UpdateProductFamilyComponentError` | `Errors/UpdateProductFamilyComponentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

