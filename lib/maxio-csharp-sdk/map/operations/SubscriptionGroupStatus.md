<!-- Generated file — do not edit; regenerated with the SDK. -->

# SubscriptionGroupStatus — operations

Accessor: `client.SubscriptionGroupStatus` · Source: `Api/SubscriptionGroupStatus.cs` · 4 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CancelDelayedCancellationForGroup

- **Signature**: `CancelDelayedCancellationForGroup(string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<CancelDelayedCancellationForGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CancelDelayedCancellationForGroupError` | `Errors/CancelDelayedCancellationForGroupError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CancelSubscriptionsInGroup

- **Signature**: `CancelSubscriptionsInGroup(string uid, CancelGroupedSubscriptionsRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `void` (Task)
- **Error**: `SdkException<CancelSubscriptionsInGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `CancelGroupedSubscriptionsRequest` | `Models/CancelGroupedSubscriptionsRequest.cs` |
| `CancelSubscriptionsInGroupError` | `Errors/CancelSubscriptionsInGroupError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### InitiateDelayedCancellationForGroup

- **Signature**: `InitiateDelayedCancellationForGroup(string uid, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<InitiateDelayedCancellationForGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `InitiateDelayedCancellationForGroupError` | `Errors/InitiateDelayedCancellationForGroupError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReactivateSubscriptionGroup

- **Signature**: `ReactivateSubscriptionGroup(string uid, ReactivateSubscriptionGroupRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ReactivateSubscriptionGroupResponse`
- **Error**: `SdkException<ReactivateSubscriptionGroupError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ReactivateSubscriptionGroupRequest` | `Models/ReactivateSubscriptionGroupRequest.cs` |
| `ReactivateSubscriptionGroupResponse` | `Models/ReactivateSubscriptionGroupResponse.cs` |
| `ReactivateSubscriptionGroupError` | `Errors/ReactivateSubscriptionGroupError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

