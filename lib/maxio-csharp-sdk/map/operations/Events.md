<!-- Generated file — do not edit; regenerated with the SDK. -->

# Events — operations

Accessor: `client.Events` · Source: `Api/Events.cs` · 3 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### ListEvents

- **Signature**: `ListEvents(long? sinceId, long? maxId, Direction? direction, IReadOnlyList<EventKey>? filter, ListEventsDateField? dateField, string? startDate, string? endDate, string? startDatetime, string? endDatetime, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 9 params (`sinceId` … `endDatetime`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `since_id` ← `sinceId`, `max_id` ← `maxId`, `direction` ← `direction`, `filter` ← `filter`, `date_field` ← `dateField`, `start_date` ← `startDate`, `end_date` ← `endDate`, `start_datetime` ← `startDatetime`, `end_datetime` ← `endDatetime`
- **Returns**: `IReadOnlyList<EventResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `Direction` | `Models/Enums/Direction.cs` |
| `EventKey` | `Models/Enums/EventKey.cs` |
| `ListEventsDateField` | `Models/Enums/ListEventsDateField.cs` |
| `EventResponse` | `Models/EventResponse.cs` |

### ListSubscriptionEvents

- **Signature**: `ListSubscriptionEvents(int subscriptionId, long? sinceId, long? maxId, Direction? direction, IReadOnlyList<EventKey>? filter, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`sinceId` … `filter`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `since_id` ← `sinceId`, `max_id` ← `maxId`, `direction` ← `direction`, `filter` ← `filter`
- **Returns**: `IReadOnlyList<EventResponse>`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `Direction` | `Models/Enums/Direction.cs` |
| `EventKey` | `Models/Enums/EventKey.cs` |
| `EventResponse` | `Models/EventResponse.cs` |

### ReadEventsCount

- **Signature**: `ReadEventsCount(long? sinceId, long? maxId, Direction? direction, IReadOnlyList<EventKey>? filter, int? page = 1, int? perPage = 20, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - 4 params (`sinceId` … `filter`) — nullable, no default → **must pass explicitly** (pass `null` to skip)
  - defaults: `page` = `1`, `perPage` = `20`
- **Query params (wire ← C#)**: `page` ← `page`, `per_page` ← `perPage`, `since_id` ← `sinceId`, `max_id` ← `maxId`, `direction` ← `direction`, `filter` ← `filter`
- **Returns**: `CountResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `Direction` | `Models/Enums/Direction.cs` |
| `EventKey` | `Models/Enums/EventKey.cs` |
| `CountResponse` | `Models/CountResponse.cs` |

