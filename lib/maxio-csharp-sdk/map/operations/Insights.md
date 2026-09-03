<!-- Generated file — do not edit; regenerated with the SDK. -->

# Insights — operations

Accessor: `client.Insights` · Source: `Api/Insights.cs` · 4 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ListMrrMovements

- **Signature**: `ListMrrMovements(int? subscriptionId, SortingDirection? direction, int? page = 1, int? perPage = 10, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `subscriptionId` — nullable, no default → **must pass explicitly**
  - `direction` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `10`
- **Query params (wire ← C#)**: `subscription_id` ← `subscriptionId`, `page` ← `page`, `per_page` ← `perPage`, `direction` ← `direction`
- **Returns**: `ListMrrResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SortingDirection` | `Models/Enums/SortingDirection.cs` |
| `ListMrrResponse` | `Models/ListMrrResponse.cs` |

### ListMrrPerSubscription

- **Signature**: `ListMrrPerSubscription(ListMrrFilter? filter, string? atTime, Direction? direction, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `filter` — nullable, no default → **must pass explicitly**
  - `atTime` — nullable, no default → **must pass explicitly**
  - `direction` — nullable, no default → **must pass explicitly**
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `filter` ← `filter`, `at_time` ← `atTime`, `page` ← `page`, `per_page` ← `perPage`, `direction` ← `direction`
- **Returns**: `SubscriptionMrrResponse`
- **Error**: `SdkException<ListMrrPerSubscriptionError>` — **Case A (typed)**
- **Error accessors**: `TryGetSubscriptionsMrrErrorResponse1(out SubscriptionsMrrErrorResponse1)` [400] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ListMrrFilter` | `Models/ListMrrFilter.cs` |
| `Direction` | `Models/Enums/Direction.cs` |
| `SubscriptionMrrResponse` | `Models/SubscriptionMrrResponse.cs` |
| `ListMrrPerSubscriptionError` | `Errors/ListMrrPerSubscriptionError.cs` |
| `SubscriptionsMrrErrorResponse1` | `Models/SubscriptionsMrrErrorResponse1.cs` |

### ReadMrr

- **Signature**: `ReadMrr(DateTimeOffset? atTime, int? subscriptionId, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `atTime` — nullable, no default → **must pass explicitly**
  - `subscriptionId` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `at_time` ← `atTime`, `subscription_id` ← `subscriptionId`
- **Returns**: `MrrResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `MrrResponse` | `Models/MrrResponse.cs` |

### ReadSiteStats

- **Signature**: `ReadSiteStats(RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `SiteSummary`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `SiteSummary` | `Models/SiteSummary.cs` |

