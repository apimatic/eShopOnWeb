<!-- Generated file — do not edit; regenerated with the SDK. -->

# SubscriptionComponents — operations

Accessor: `client.SubscriptionComponents` · Source: `Api/SubscriptionComponents.cs` · 17 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ActivateEventBasedComponent

- **Signature**: `ActivateEventBasedComponent(int subscriptionId, int componentId, ActivateEventBasedComponent? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ActivateEventBasedComponent` | `Models/ActivateEventBasedComponent.cs` |

### AllocateComponent

- **Signature**: `AllocateComponent(int subscriptionId, int componentId, CreateAllocationRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `AllocationResponse`
- **Error**: `SdkException<AllocateComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateAllocationRequest` | `Models/CreateAllocationRequest.cs` |
| `AllocationResponse` | `Models/AllocationResponse.cs` |
| `AllocateComponentError` | `Errors/AllocateComponentError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### AllocateComponents

- **Signature**: `AllocateComponents(int subscriptionId, AllocateComponents? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `IReadOnlyList<AllocationResponse>`
- **Error**: `SdkException<AllocateComponentsError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `AllocateComponents` | `Models/AllocateComponents.cs` |
| `AllocationResponse` | `Models/AllocationResponse.cs` |
| `AllocateComponentsError` | `Errors/AllocateComponentsError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### BulkRecordEvents

- **Server group**: `Ebb`
- **Signature**: `BulkRecordEvents(string apiHandle, string? storeUid, IReadOnlyList<EbbEvent>? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `storeUid` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `store_uid` ← `storeUid`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `EbbEvent` | `Models/EbbEvent.cs` |

### BulkResetSubscriptionComponentsPricePoints

- **Signature**: `BulkResetSubscriptionComponentsPricePoints(int subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `SubscriptionResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SubscriptionResponse` | `Models/SubscriptionResponse.cs` |

### BulkUpdateSubscriptionComponentsPricePoints

- **Signature**: `BulkUpdateSubscriptionComponentsPricePoints(int subscriptionId, BulkComponentsPricePointAssignment? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `BulkComponentsPricePointAssignment`
- **Error**: `SdkException<BulkUpdateSubscriptionComponentsPricePointsError>` — **Case A (typed)**
- **Error accessors**: `TryGetComponentPricePointError1(out ComponentPricePointError1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BulkComponentsPricePointAssignment` | `Models/BulkComponentsPricePointAssignment.cs` |
| `BulkUpdateSubscriptionComponentsPricePointsError` | `Errors/BulkUpdateSubscriptionComponentsPricePointsError.cs` |
| `ComponentPricePointError1` | `Models/ComponentPricePointError1.cs` |

### CreateUsage

- **Signature**: `CreateUsage(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, CreateUsageRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `UsageResponse`
- **Error**: `SdkException<CreateUsageError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionIdOrReference` | `Models/AnyOf/SubscriptionIdOrReference.cs` |
| `ComponentIdModel` | `Models/AnyOf/ComponentIdModel.cs` |
| `CreateUsageRequest` | `Models/CreateUsageRequest.cs` |
| `UsageResponse` | `Models/UsageResponse.cs` |
| `CreateUsageError` | `Errors/CreateUsageError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### DeactivateEventBasedComponent

- **Signature**: `DeactivateEventBasedComponent(int subscriptionId, int componentId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

### DeletePrepaidUsageAllocation

- **Signature**: `DeletePrepaidUsageAllocation(int subscriptionId, int componentId, int allocationId, CreditSchemeRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeletePrepaidUsageAllocationError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetSubscriptionComponentAllocationError1(out SubscriptionComponentAllocationError1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreditSchemeRequest` | `Models/CreditSchemeRequest.cs` |
| `DeletePrepaidUsageAllocationError` | `Errors/DeletePrepaidUsageAllocationError.cs` |
| `SubscriptionComponentAllocationError1` | `Models/SubscriptionComponentAllocationError1.cs` |

### ListAllocations

- **Signature**: `ListAllocations(int subscriptionId, int componentId, int? page = 1, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - defaults: `page` = `1`
- **Query params (wire ← C#)**: `page` ← `page`
- **Returns**: `IReadOnlyList<AllocationResponse>`
- **Error**: `SdkException<ListAllocationsError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `AllocationResponse` | `Models/AllocationResponse.cs` |
| `ListAllocationsError` | `Errors/ListAllocationsError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListSubscriptionComponents

- **Signature**: `ListSubscriptionComponents(int subscriptionId, SubscriptionListDateField? dateField, SortingDirection? direction, ListSubscriptionComponentsFilter? filter, string? endDate, string? endDatetime, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsSort? sort, string? startDate, string? startDatetime, IReadOnlyList<ListSubscriptionComponentsInclude>? include, bool? inUse, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 12 params (`dateField` … `inUse`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
- **Query params (wire ← C#)**: `date_field` ← `dateField`, `direction` ← `direction`, `filter` ← `filter`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `price_point_ids` ← `pricePointIds`, `product_family_ids` ← `productFamilyIds`, `sort` ← `sort`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `include` ← `include`, `in_use` ← `inUse`
- **Returns**: `IReadOnlyList<SubscriptionComponentResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SubscriptionListDateField` | `Models/Enums/SubscriptionListDateField.cs` |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `ListSubscriptionComponentsFilter` | `Models/ListSubscriptionComponentsFilter.cs` |
| `IncludeNotNull` | `Models/Enums/IncludeNotNull.cs` |
| `ListSubscriptionComponentsSort` | `Models/Enums/ListSubscriptionComponentsSort.cs` |
| `ListSubscriptionComponentsInclude` | `Models/Enums/ListSubscriptionComponentsInclude.cs` |
| `SubscriptionComponentResponse` | `Models/SubscriptionComponentResponse.cs` |

### ListSubscriptionComponentsForSite

- **Signature**: `ListSubscriptionComponentsForSite(ListSubscriptionComponentsSort? sort, SortingDirection? direction, ListSubscriptionComponentsForSiteFilter? filter, SubscriptionListDateField? dateField, string? startDate, string? startDatetime, string? endDate, string? endDatetime, IReadOnlyList<int>? subscriptionIds, IncludeNotNull? pricePointIds, IReadOnlyList<int>? productFamilyIds, ListSubscriptionComponentsInclude? include, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 12 params (`sort` … `include`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `sort` ← `sort`, `direction` ← `direction`, `filter` ← `filter`, `date_field` ← `dateField`, `start_date` ← `startDate`, `start_datetime` ← `startDatetime`, `end_date` ← `endDate`, `end_datetime` ← `endDatetime`, `subscription_ids` ← `subscriptionIds`, `price_point_ids` ← `pricePointIds`, `product_family_ids` ← `productFamilyIds`, `include` ← `include`
- **Returns**: `ListSubscriptionComponentsResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ListSubscriptionComponentsSort` | `Models/Enums/ListSubscriptionComponentsSort.cs` |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `ListSubscriptionComponentsForSiteFilter` | `Models/ListSubscriptionComponentsForSiteFilter.cs` |
| `SubscriptionListDateField` | `Models/Enums/SubscriptionListDateField.cs` |
| `IncludeNotNull` | `Models/Enums/IncludeNotNull.cs` |
| `ListSubscriptionComponentsInclude` | `Models/Enums/ListSubscriptionComponentsInclude.cs` |
| `ListSubscriptionComponentsResponse` | `Models/ListSubscriptionComponentsResponse.cs` |

### ListUsages

- **Signature**: `ListUsages(SubscriptionIdOrReference subscriptionIdOrReference, ComponentIdModel componentId, long? sinceId, long? maxId, DateTimeOffset? sinceDate, DateTimeOffset? untilDate, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`sinceId` … `untilDate`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `since_id` ← `sinceId`, `max_id` ← `maxId`, `since_date` ← `sinceDate`, `until_date` ← `untilDate`, `page` ← `page`, `per_page` ← `perPage`
- **Returns**: `IReadOnlyList<UsageResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SubscriptionIdOrReference` | `Models/AnyOf/SubscriptionIdOrReference.cs` |
| `ComponentIdModel` | `Models/AnyOf/ComponentIdModel.cs` |
| `UsageResponse` | `Models/UsageResponse.cs` |

### PreviewAllocations

- **Signature**: `PreviewAllocations(int subscriptionId, PreviewAllocationsRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `AllocationPreviewResponse`
- **Error**: `SdkException<PreviewAllocationsError>` — **Case A (typed)**
- **Error accessors**: `TryGetComponentAllocationError1(out ComponentAllocationError1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `PreviewAllocationsRequest` | `Models/PreviewAllocationsRequest.cs` |
| `AllocationPreviewResponse` | `Models/AllocationPreviewResponse.cs` |
| `PreviewAllocationsError` | `Errors/PreviewAllocationsError.cs` |
| `ComponentAllocationError1` | `Models/ComponentAllocationError1.cs` |

### ReadSubscriptionComponent

- **Signature**: `ReadSubscriptionComponent(int subscriptionId, int componentId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `SubscriptionComponentResponse`
- **Error**: `SdkException<ReadSubscriptionComponentError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `SubscriptionComponentResponse` | `Models/SubscriptionComponentResponse.cs` |
| `ReadSubscriptionComponentError` | `Errors/ReadSubscriptionComponentError.cs` |

### RecordEvent

- **Server group**: `Ebb`
- **Signature**: `RecordEvent(string apiHandle, string? storeUid, EbbEvent? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `storeUid` — nullable, no default → **must pass explicitly**
  - `body` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `store_uid` ← `storeUid`
- **Returns**: `void` (Task)
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `EbbEvent` | `Models/EbbEvent.cs` |

### UpdatePrepaidUsageAllocationExpirationDate

- **Signature**: `UpdatePrepaidUsageAllocationExpirationDate(int subscriptionId, int componentId, int allocationId, UpdateAllocationExpirationDate? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<UpdatePrepaidUsageAllocationExpirationDateError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetSubscriptionComponentAllocationError1(out SubscriptionComponentAllocationError1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateAllocationExpirationDate` | `Models/UpdateAllocationExpirationDate.cs` |
| `UpdatePrepaidUsageAllocationExpirationDateError` | `Errors/UpdatePrepaidUsageAllocationExpirationDateError.cs` |
| `SubscriptionComponentAllocationError1` | `Models/SubscriptionComponentAllocationError1.cs` |

