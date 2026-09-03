<!-- Generated file — do not edit; regenerated with the SDK. -->

# SubscriptionRenewals — operations

Accessor: `client.SubscriptionRenewals` · Source: `Api/SubscriptionRenewals.cs` · 11 operations

**Type sources**: the file declaring each type an operation names (`RawError` excluded — see sdk-map.md).

### CancelScheduledRenewalConfiguration

- **Signature**: `CancelScheduledRenewalConfiguration(int subscriptionId, int id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ScheduledRenewalConfigurationResponse`
- **Error**: `SdkException<CancelScheduledRenewalConfigurationError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ScheduledRenewalConfigurationResponse` | `Models/ScheduledRenewalConfigurationResponse.cs` |
| `CancelScheduledRenewalConfigurationError` | `Errors/CancelScheduledRenewalConfigurationError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateScheduledRenewalConfiguration

- **Signature**: `CreateScheduledRenewalConfiguration(int subscriptionId, ScheduledRenewalConfigurationRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ScheduledRenewalConfigurationResponse`
- **Error**: `SdkException<CreateScheduledRenewalConfigurationError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ScheduledRenewalConfigurationRequest` | `Models/ScheduledRenewalConfigurationRequest.cs` |
| `ScheduledRenewalConfigurationResponse` | `Models/ScheduledRenewalConfigurationResponse.cs` |
| `CreateScheduledRenewalConfigurationError` | `Errors/CreateScheduledRenewalConfigurationError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### CreateScheduledRenewalConfigurationItem

- **Signature**: `CreateScheduledRenewalConfigurationItem(int subscriptionId, int scheduledRenewalsConfigurationId, ScheduledRenewalConfigurationItemRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ScheduledRenewalConfigurationItemResponse`
- **Error**: `SdkException<CreateScheduledRenewalConfigurationItemError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ScheduledRenewalConfigurationItemRequest` | `Models/ScheduledRenewalConfigurationItemRequest.cs` |
| `ScheduledRenewalConfigurationItemResponse` | `Models/ScheduledRenewalConfigurationItemResponse.cs` |
| `CreateScheduledRenewalConfigurationItemError` | `Errors/CreateScheduledRenewalConfigurationItemError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### DeleteScheduledRenewalConfigurationItem

- **Signature**: `DeleteScheduledRenewalConfigurationItem(int subscriptionId, int scheduledRenewalsConfigurationId, int id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `void` (Task)
- **Error**: `SdkException<DeleteScheduledRenewalConfigurationItemError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `DeleteScheduledRenewalConfigurationItemError` | `Errors/DeleteScheduledRenewalConfigurationItemError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ListScheduledRenewalConfigurations

- **Signature**: `ListScheduledRenewalConfigurations(int subscriptionId, Status? status, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `status` — nullable, no default → **must pass explicitly**
- **Query params (wire ← C#)**: `status` ← `status`
- **Returns**: `ScheduledRenewalConfigurationsResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `Status` | `Models/Enums/Status.cs` |
| `ScheduledRenewalConfigurationsResponse` | `Models/ScheduledRenewalConfigurationsResponse.cs` |

### LockInScheduledRenewalImmediately

- **Signature**: `LockInScheduledRenewalImmediately(int subscriptionId, int id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ScheduledRenewalConfigurationResponse`
- **Error**: `SdkException<LockInScheduledRenewalImmediatelyError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ScheduledRenewalConfigurationResponse` | `Models/ScheduledRenewalConfigurationResponse.cs` |
| `LockInScheduledRenewalImmediatelyError` | `Errors/LockInScheduledRenewalImmediatelyError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### ReadScheduledRenewalConfiguration

- **Signature**: `ReadScheduledRenewalConfiguration(int subscriptionId, int id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ScheduledRenewalConfigurationResponse`
- **Error**: `SdkException<RawError>` — **Case B**

| Type | Source |
| --- | --- |
| `ScheduledRenewalConfigurationResponse` | `Models/ScheduledRenewalConfigurationResponse.cs` |

### ScheduleScheduledRenewalLockIn

- **Signature**: `ScheduleScheduledRenewalLockIn(int subscriptionId, int id, ScheduledRenewalLockInRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ScheduledRenewalConfigurationResponse`
- **Error**: `SdkException<ScheduleScheduledRenewalLockInError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ScheduledRenewalLockInRequest` | `Models/ScheduledRenewalLockInRequest.cs` |
| `ScheduledRenewalConfigurationResponse` | `Models/ScheduledRenewalConfigurationResponse.cs` |
| `ScheduleScheduledRenewalLockInError` | `Errors/ScheduleScheduledRenewalLockInError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### UnpublishScheduledRenewalConfiguration

- **Signature**: `UnpublishScheduledRenewalConfiguration(int subscriptionId, int id, RequestOptions? requestOptions = null, CancellationToken ct = default)`
- **Returns**: `ScheduledRenewalConfigurationResponse`
- **Error**: `SdkException<UnpublishScheduledRenewalConfigurationError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ScheduledRenewalConfigurationResponse` | `Models/ScheduledRenewalConfigurationResponse.cs` |
| `UnpublishScheduledRenewalConfigurationError` | `Errors/UnpublishScheduledRenewalConfigurationError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### UpdateScheduledRenewalConfiguration

- **Signature**: `UpdateScheduledRenewalConfiguration(int subscriptionId, int id, ScheduledRenewalConfigurationRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ScheduledRenewalConfigurationResponse`
- **Error**: `SdkException<UpdateScheduledRenewalConfigurationError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ScheduledRenewalConfigurationRequest` | `Models/ScheduledRenewalConfigurationRequest.cs` |
| `ScheduledRenewalConfigurationResponse` | `Models/ScheduledRenewalConfigurationResponse.cs` |
| `UpdateScheduledRenewalConfigurationError` | `Errors/UpdateScheduledRenewalConfigurationError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

### UpdateScheduledRenewalConfigurationItem

- **Signature**: `UpdateScheduledRenewalConfigurationItem(int subscriptionId, int scheduledRenewalsConfigurationId, int id, ScheduledRenewalUpdateRequest? body, RequestOptions? requestOptions = null, CancellationToken ct = default)`
  - `body` — nullable, no default → **must pass explicitly**
- **Returns**: `ScheduledRenewalConfigurationItemResponse`
- **Error**: `SdkException<UpdateScheduledRenewalConfigurationItemError>` — **Case A (typed)**
- **Error accessors**: `TryGetErrorListResponse1(out ErrorListResponse1)` [422] · `TryGetRawError(out RawError)` [fallback]

| Type | Source |
| --- | --- |
| `ScheduledRenewalUpdateRequest` | `Models/ScheduledRenewalUpdateRequest.cs` |
| `ScheduledRenewalConfigurationItemResponse` | `Models/ScheduledRenewalConfigurationItemResponse.cs` |
| `UpdateScheduledRenewalConfigurationItemError` | `Errors/UpdateScheduledRenewalConfigurationItemError.cs` |
| `ErrorListResponse1` | `Models/ErrorListResponse1.cs` |

