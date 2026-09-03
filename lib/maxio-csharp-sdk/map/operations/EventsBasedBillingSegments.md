<!-- Generated file — do not edit; regenerated with the SDK. -->

# EventsBasedBillingSegments — operations

Accessor: `client.EventsBasedBillingSegments` · Source: `Api/EventsBasedBillingSegments.cs` · 6 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### BulkCreateSegments

- **Signature**: `BulkCreateSegments(string componentId, string pricePointId, BulkCreateSegments? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ListSegmentsResponse`
- **Error**: `SdkException<BulkCreateSegmentsError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetEventBasedBillingSegment1(out EventBasedBillingSegment1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BulkCreateSegments` | `Models/BulkCreateSegments.cs` |
| `ListSegmentsResponse` | `Models/ListSegmentsResponse.cs` |
| `BulkCreateSegmentsError` | `Errors/BulkCreateSegmentsError.cs` |
| `EventBasedBillingSegment1` | `Models/EventBasedBillingSegment1.cs` |

### BulkUpdateSegments

- **Signature**: `BulkUpdateSegments(string componentId, string pricePointId, BulkUpdateSegments? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ListSegmentsResponse`
- **Error**: `SdkException<BulkUpdateSegmentsError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetEventBasedBillingSegment1(out EventBasedBillingSegment1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `BulkUpdateSegments` | `Models/BulkUpdateSegments.cs` |
| `ListSegmentsResponse` | `Models/ListSegmentsResponse.cs` |
| `BulkUpdateSegmentsError` | `Errors/BulkUpdateSegmentsError.cs` |
| `EventBasedBillingSegment1` | `Models/EventBasedBillingSegment1.cs` |

### CreateSegment

- **Signature**: `CreateSegment(string componentId, string pricePointId, CreateSegmentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SegmentResponse`
- **Error**: `SdkException<CreateSegmentError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetEventBasedBillingSegmentErrors1(out EventBasedBillingSegmentErrors1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CreateSegmentRequest` | `Models/CreateSegmentRequest.cs` |
| `SegmentResponse` | `Models/SegmentResponse.cs` |
| `CreateSegmentError` | `Errors/CreateSegmentError.cs` |
| `EventBasedBillingSegmentErrors1` | `Models/EventBasedBillingSegmentErrors1.cs` |

### DeleteSegment

- **Signature**: `DeleteSegment(string componentId, string pricePointId, double id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeleteSegmentError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404, 422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeleteSegmentError` | `Errors/DeleteSegmentError.cs` |

### ListSegmentsForPricePoint

- **Signature**: `ListSegmentsForPricePoint(string componentId, string pricePointId, ListSegmentsFilter? filter, int? page = 1, int? perPage = 30, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `filter` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `30`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `filter` ← `filter`
- **Returns**: `ListSegmentsResponse`
- **Error**: `SdkException<ListSegmentsForPricePointError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetEventBasedBillingListSegmentsErrors1(out EventBasedBillingListSegmentsErrors1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ListSegmentsFilter` | `Models/ListSegmentsFilter.cs` |
| `ListSegmentsResponse` | `Models/ListSegmentsResponse.cs` |
| `ListSegmentsForPricePointError` | `Errors/ListSegmentsForPricePointError.cs` |
| `EventBasedBillingListSegmentsErrors1` | `Models/EventBasedBillingListSegmentsErrors1.cs` |

### UpdateSegment

- **Signature**: `UpdateSegment(string componentId, string pricePointId, double id, UpdateSegmentRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `SegmentResponse`
- **Error**: `SdkException<UpdateSegmentError>` — **Case A (typed)**
- **Error accessors**: `TryGetNoContent(out RawError)` [404] · `TryGetEventBasedBillingSegmentErrors1(out EventBasedBillingSegmentErrors1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `UpdateSegmentRequest` | `Models/UpdateSegmentRequest.cs` |
| `SegmentResponse` | `Models/SegmentResponse.cs` |
| `UpdateSegmentError` | `Errors/UpdateSegmentError.cs` |
| `EventBasedBillingSegmentErrors1` | `Models/EventBasedBillingSegmentErrors1.cs` |

